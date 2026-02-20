using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc;
using Commitcollect.api.Services;

namespace Commitcollect.api.Controllers;

[ApiController]
[Route("")]
public sealed class AccountController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAmazonDynamoDB _ddb;
    private readonly IConfiguration _config;
    private readonly IAmazonCognitoIdentityProvider _cognito;


    // DELETE /account
    [HttpDelete("account")]
    public async Task<IActionResult> DeleteAccount(CancellationToken ct)
    {
        var sessionsTable = _config["DynamoDb:SessionsTable"];
        var mainTable = _config["DynamoDb:StravaTokensTable"];
        var userPoolId = _config["Cognito:UserPoolId"] ?? _config["Cognito__UserPoolId"];

        if (string.IsNullOrWhiteSpace(sessionsTable)) return Problem("Missing DynamoDb:SessionsTable");
        if (string.IsNullOrWhiteSpace(mainTable)) return Problem("Missing DynamoDb:StravaTokensTable");
        if (string.IsNullOrWhiteSpace(userPoolId)) return Problem("Missing Cognito:UserPoolId (or Cognito__UserPoolId)");

        var sessionId = Request.Cookies.TryGetValue("cc_session", out var s) ? s : null;
        if (string.IsNullOrWhiteSpace(sessionId)) return Unauthorized(new { status = "missing_session" });

        var userId = await ResolveUserIdFromSessionAsync(sessionsTable, sessionId, ct);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized(new { status = "invalid_session" });


        var correlationId = HttpContext.TraceIdentifier;

        // AUDIT: deletion attempt (best-effort)
        await _audit.TryWriteAsync(
            HttpContext,
            userId: userId,
            eventType: "ACCOUNT_DELETE",
            result: "attempt",
            data: new Dictionary<string, AttributeValue>
            {
                ["sessionId"] = new AttributeValue { S = sessionId }
            },
            correlationId: correlationId,
            ct: ct
        );


        var result = "success"; // success | partial | failed
        var errorMsg = (string?)null;

        var steps = new Dictionary<string, bool>
        {
            ["stravaDisconnectAttempted"] = false,
            ["userItemsDeleted"] = false,
            ["sessionDeleted"] = false,
            ["cognitoGlobalSignOutAttempted"] = false,
            ["cognitoUserDeleted"] = false
        };

        try
        {
            // 1) Best-effort Strava disconnect
            steps["stravaDisconnectAttempted"] = true;
            var dr = await StravaDisconnect.BestEffortDisconnectAsync(_httpClientFactory, _ddb, mainTable, userId, ct);

            var auditResult = dr.Outcome switch
            {
                StravaDisconnect.Outcome.NoConnection => "attempt",
                StravaDisconnect.Outcome.Success => "success",
                StravaDisconnect.Outcome.Partial => "partial",
                _ => "attempt"
            };

            await _audit.TryWriteAsync(
                HttpContext,
                userId: userId,
                eventType: "STRAVA_DEAUTHORIZE",
                result: auditResult,
                data: dr.AthleteId > 0
                    ? new Dictionary<string, AttributeValue>
                    {
                        ["athleteId"] = new AttributeValue { N = dr.AthleteId.ToString() }
                    }
                    : new Dictionary<string, AttributeValue>
                    {
                        ["reason"] = new AttributeValue { S = "no_connection" }
                    },
                metrics: new Dictionary<string, AttributeValue>
                {
                    ["auditTtlDays"] = new AttributeValue { N = "90" }
                },
                correlationId: HttpContext.TraceIdentifier
            );
            steps["stravaDisconnectSucceeded"] = dr.Outcome == StravaDisconnect.Outcome.Success;




            // 2) Delete all USER#{userId} items
            await DeleteAllUserItemsAsync(mainTable, userId, ct);
            steps["userItemsDeleted"] = true;

            // 3) Delete current session record
            await BestEffortDeleteSessionAsync(sessionsTable, sessionId, ct);
            steps["sessionDeleted"] = true;

            // 4) Revoke all active Cognito sessions (best effort)
            steps["cognitoGlobalSignOutAttempted"] = true;
            try
            {
                await _cognito.AdminUserGlobalSignOutAsync(new AdminUserGlobalSignOutRequest
                {
                    UserPoolId = userPoolId,
                    Username = userId
                }, ct);
            }
            catch
            {
                result = "partial";
            }

            // 5) Delete Cognito user (hard requirement)
            await _cognito.AdminDeleteUserAsync(new AdminDeleteUserRequest
            {
                UserPoolId = userPoolId,
                Username = userId
            }, ct);
            steps["cognitoUserDeleted"] = true;

            // 6) Clear cookie
            Response.Cookies.Append("cc_session", "", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Domain = ".commitcollect.com",
                Path = "/",
                Expires = DateTimeOffset.UnixEpoch,
                MaxAge = TimeSpan.Zero
            });

            return NoContent();
        }
        catch (Exception ex)
        {
            result = "failed";
            errorMsg = ex.Message;
            return Problem("Account deletion failed.");
        }
        finally
        {
            // AUDIT: deletion result (best-effort)
            var data = new Dictionary<string, AttributeValue>
            {
                ["sessionId"] = new AttributeValue { S = sessionId },
                ["resultDetail"] = new AttributeValue { S = result }
            };

            if (!string.IsNullOrWhiteSpace(errorMsg))
                data["error"] = new AttributeValue { S = errorMsg.Length > 300 ? errorMsg[..300] : errorMsg };

            var metrics = new Dictionary<string, AttributeValue>
            {
                ["stravaDisconnectAttempted"] = new AttributeValue { BOOL = steps["stravaDisconnectAttempted"] },
                ["userItemsDeleted"] = new AttributeValue { BOOL = steps["userItemsDeleted"] },
                ["sessionDeleted"] = new AttributeValue { BOOL = steps["sessionDeleted"] },
                ["cognitoGlobalSignOutAttempted"] = new AttributeValue { BOOL = steps["cognitoGlobalSignOutAttempted"] },
                ["cognitoUserDeleted"] = new AttributeValue { BOOL = steps["cognitoUserDeleted"] }
            };

            await _audit.TryWriteAsync(
                HttpContext,
                userId: userId,
                eventType: "ACCOUNT_DELETE",
                result: result,
                data: data,
                metrics: metrics,
                correlationId: correlationId,
                ct: ct
            );
        }
    }

    private async Task<string?> ResolveUserIdFromSessionAsync(string sessionsTable, string sessionId, CancellationToken ct)
    {
        var resp = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = sessionsTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = $"SESSION#{sessionId}" },
                ["SK"] = new AttributeValue { S = "META" }
            },
            ConsistentRead = true
        }, ct);

        if (resp.Item is null || resp.Item.Count == 0) return null;
        return resp.Item.TryGetValue("userId", out var uid) ? uid.S : null;
    }

    private async Task DeleteAllUserItemsAsync(string table, string userId, CancellationToken ct)
    {
        var pk = $"USER#{userId}";
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var q = await _ddb.QueryAsync(new QueryRequest
            {
                TableName = table,
                KeyConditionExpression = "PK = :pk",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue { S = pk }
                },
                ProjectionExpression = "PK, SK",
                ExclusiveStartKey = lastKey
            }, ct);

            if (q.Items.Count > 0)
            {
                foreach (var chunk in q.Items.Chunk(25))
                {
                    var keys = chunk.Select(it => new Dictionary<string, AttributeValue>
                    {
                        ["PK"] = it["PK"],
                        ["SK"] = it["SK"]
                    }).ToList();

                    await BatchDeleteWithRetryAsync(table, keys, ct);
                }
            }

            lastKey = (q.LastEvaluatedKey != null && q.LastEvaluatedKey.Count > 0)
                ? q.LastEvaluatedKey
                : null;

        } while (lastKey != null);
    }

    private async Task BatchDeleteWithRetryAsync(string table, List<Dictionary<string, AttributeValue>> keys, CancellationToken ct)
    {
        var request = new BatchWriteItemRequest
        {
            RequestItems = new Dictionary<string, List<WriteRequest>>
            {
                [table] = keys.Select(k => new WriteRequest
                {
                    DeleteRequest = new DeleteRequest { Key = k }
                }).ToList()
            }
        };

        while (true)
        {
            var resp = await _ddb.BatchWriteItemAsync(request, ct);

            if (resp.UnprocessedItems == null || resp.UnprocessedItems.Count == 0)
                return;

            // Retry only the unprocessed items
            request = new BatchWriteItemRequest { RequestItems = resp.UnprocessedItems };

            // Small backoff (simple + effective)
            await Task.Delay(200, ct);
        }
    }

    private async Task BestEffortDeleteSessionAsync(string sessionsTable, string sessionId, CancellationToken ct)
    {
        try
        {
            await _ddb.DeleteItemAsync(new DeleteItemRequest
            {
                TableName = sessionsTable,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new AttributeValue { S = $"SESSION#{sessionId}" },
                    ["SK"] = new AttributeValue { S = "META" }
                }
            }, ct);
        }
        catch
        {
            // swallow
        }
    }

    private readonly AuditEventService _audit;
    public AccountController(
        IHttpClientFactory httpClientFactory,
        IAmazonDynamoDB ddb,
        IConfiguration config,
        IAmazonCognitoIdentityProvider cognito,
        AuditEventService audit)
    {
        _httpClientFactory = httpClientFactory;
        _ddb = ddb;
        _config = config;
        _cognito = cognito;
        _audit = audit;
    }
}

