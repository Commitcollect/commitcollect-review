using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc;

namespace Commitcollect.api.Controllers;

[ApiController]
[Route("")]
public sealed class AccountController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAmazonDynamoDB _ddb;
    private readonly IConfiguration _config;
    private readonly IAmazonCognitoIdentityProvider _cognito;

    public AccountController(
        IHttpClientFactory httpClientFactory,
        IAmazonDynamoDB ddb,
        IConfiguration config,
        IAmazonCognitoIdentityProvider cognito)
    {
        _httpClientFactory = httpClientFactory;
        _ddb = ddb;
        _config = config;
        _cognito = cognito;
    }

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

        // 1) If Strava connected: deauth + delete connection + ownership (best effort)
        await BestEffortDisconnectStravaAsync(mainTable, userId, ct);

        // 2) Delete all USER#{userId} items (profile, workouts, anything under that PK)
        await DeleteAllUserItemsAsync(mainTable, userId, ct);

        // 3) Delete current session record
        await BestEffortDeleteSessionAsync(sessionsTable, sessionId, ct);

        // 4) Delete Cognito user
        await _cognito.AdminDeleteUserAsync(new AdminDeleteUserRequest
        {
            UserPoolId = userPoolId,
            Username = userId
        }, ct);

        // 5) Clear cookie
        Response.Cookies.Append("cc_session", "", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Domain = ".commitcollect.com",
            Path = "/",
            Expires = DateTimeOffset.UnixEpoch
        });

        return NoContent();
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

    private async Task BestEffortDisconnectStravaAsync(string table, string userId, CancellationToken ct)
    {
        // Load connection item (uses lowercase attributes in your codebase)
        var resp = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = table,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = $"USER#{userId}" },
                ["SK"] = new AttributeValue { S = "STRAVA#CONNECTION" }
            },
            ConsistentRead = true
        }, ct);

        if (resp.Item is null || resp.Item.Count == 0) return;

        var athleteId = resp.Item.TryGetValue("athleteId", out var aid) && !string.IsNullOrWhiteSpace(aid.N)
            ? long.Parse(aid.N)
            : 0;

        var accessToken = resp.Item.TryGetValue("accessToken", out var at) ? (at.S ?? "") : "";

        // Strava deauth best-effort
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            try
            {
                var http = _httpClientFactory.CreateClient();
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["access_token"] = accessToken
                });
                using var _ = await http.PostAsync("https://www.strava.com/oauth/deauthorize", content, ct);
            }
            catch { /* swallow */ }
        }

        // Dynamo transactional delete (ownership conditional)
        if (athleteId > 0)
        {
            try
            {
                await _ddb.TransactWriteItemsAsync(new TransactWriteItemsRequest
                {
                    TransactItems = new List<TransactWriteItem>
                    {
                        new()
                        {
                            Delete = new Delete
                            {
                                TableName = table,
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    ["PK"] = new AttributeValue { S = $"USER#{userId}" },
                                    ["SK"] = new AttributeValue { S = "STRAVA#CONNECTION" }
                                }
                            }
                        },
                        new()
                        {
                            Delete = new Delete
                            {
                                TableName = table,
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    ["PK"] = new AttributeValue { S = $"STRAVA#ATHLETE#{athleteId}" },
                                    ["SK"] = new AttributeValue { S = "OWNER" }
                                },
                                ConditionExpression = "attribute_exists(PK) AND userId = :uid",
                                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                                {
                                    [":uid"] = new AttributeValue { S = userId }
                                }
                            }
                        }
                    }
                }, ct);
            }
            catch
            {
                // idempotent
            }
        }
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
                    await _ddb.BatchWriteItemAsync(new BatchWriteItemRequest
                    {
                        RequestItems = new Dictionary<string, List<WriteRequest>>
                        {
                            [table] = chunk.Select(it => new WriteRequest
                            {
                                DeleteRequest = new DeleteRequest
                                {
                                    Key = new Dictionary<string, AttributeValue>
                                    {
                                        ["PK"] = it["PK"],
                                        ["SK"] = it["SK"]
                                    }
                                }
                            }).ToList()
                        }
                    }, ct);
                }
            }

            lastKey = (q.LastEvaluatedKey != null && q.LastEvaluatedKey.Count > 0) ? q.LastEvaluatedKey : null;

        } while (lastKey != null);
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
        catch { /* swallow */ }
    }
}
