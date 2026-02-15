using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc;

namespace Commitcollect.api.Controllers;

[ApiController]
[Route("strava")]
public sealed class StravaConnectionController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAmazonDynamoDB _ddb;
    private readonly IConfiguration _config;

    public StravaConnectionController(IHttpClientFactory httpClientFactory, IAmazonDynamoDB ddb, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _ddb = ddb;
        _config = config;
    }

    // DELETE /strava/connection
    [HttpDelete("connection")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        var sessionsTable = _config["DynamoDb:SessionsTable"];
        var mainTable = _config["DynamoDb:StravaTokensTable"]; // your CommitCollect table
        if (string.IsNullOrWhiteSpace(sessionsTable)) return Problem("Missing DynamoDb:SessionsTable");
        if (string.IsNullOrWhiteSpace(mainTable)) return Problem("Missing DynamoDb:StravaTokensTable");

        var sessionId = Request.Cookies.TryGetValue("cc_session", out var s) ? s : null;
        if (string.IsNullOrWhiteSpace(sessionId)) return Unauthorized(new { status = "missing_session" });

        var userId = await ResolveUserIdFromSessionAsync(sessionsTable, sessionId, ct);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized(new { status = "invalid_session" });

        // Load connection (idempotent)
        var conn = await GetStravaConnectionAsync(mainTable, userId, ct);
        if (conn is null)
            return NoContent();

        // Best-effort Strava deauth (do not block disconnect if Strava is down)
        await BestEffortStravaDeauthorizeAsync(conn.AccessToken, ct);

        // Transactional delete: connection + athlete ownership (only if owned by this user)
        await BestEffortDeleteConnectionAndOwnershipAsync(mainTable, userId, conn.AthleteId, ct);

        // Optional: delete workouts for this user (comment in if you want it now)
        // await DeleteUserWorkoutsAsync(mainTable, userId, ct);

        return NoContent();
    }

    private sealed record StravaConn(long AthleteId, string AccessToken);

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

    private async Task<StravaConn?> GetStravaConnectionAsync(string table, string userId, CancellationToken ct)
    {
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

        if (resp.Item is null || resp.Item.Count == 0) return null;

        var athleteId = resp.Item.TryGetValue("athleteId", out var aid) && !string.IsNullOrWhiteSpace(aid.N)
            ? long.Parse(aid.N)
            : 0;

        var accessToken = resp.Item.TryGetValue("accessToken", out var at) ? (at.S ?? "") : "";

        if (athleteId <= 0 || string.IsNullOrWhiteSpace(accessToken))
            return null;

        return new StravaConn(athleteId, accessToken);
    }

    private async Task BestEffortStravaDeauthorizeAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            var http = _httpClientFactory.CreateClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["access_token"] = accessToken
            });

            using var resp = await http.PostAsync("https://www.strava.com/oauth/deauthorize", content, ct);
            // Ignore failures, but you should log in real code
        }
        catch
        {
            // swallow (best-effort)
        }
    }

    private async Task BestEffortDeleteConnectionAndOwnershipAsync(string table, string userId, long athleteId, CancellationToken ct)
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
            // idempotent / safe: if already deleted or ownership not ours, treat as disconnected
        }
    }

    private async Task DeleteUserWorkoutsAsync(string table, string userId, CancellationToken ct)
    {
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var q = await _ddb.QueryAsync(new QueryRequest
            {
                TableName = table,
                KeyConditionExpression = "PK = :pk AND begins_with(SK, :sk)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue { S = $"USER#{userId}" },
                    [":sk"] = new AttributeValue { S = "WORKOUT#STRAVA#" }
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
}
