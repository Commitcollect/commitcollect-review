using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc;
using Commitcollect.api.Services;

namespace Commitcollect.api.Controllers;

[ApiController]
[Route("strava")]
public sealed class StravaConnectionController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAmazonDynamoDB _ddb;
    private readonly IConfiguration _config;
    private readonly AuditEventService _audit;

    public StravaConnectionController(
        IHttpClientFactory httpClientFactory,
        IAmazonDynamoDB ddb,
        IConfiguration config,
        AuditEventService audit)
    {
        _httpClientFactory = httpClientFactory;
        _ddb = ddb;
        _config = config;
        _audit = audit;
    }

    // DELETE /strava/connection
    [HttpDelete("connection")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        var sessionsTable = _config["DynamoDb:SessionsTable"];
        var mainTable = _config["DynamoDb:StravaTokensTable"];
        if (string.IsNullOrWhiteSpace(sessionsTable)) return Problem("Missing DynamoDb:SessionsTable");
        if (string.IsNullOrWhiteSpace(mainTable)) return Problem("Missing DynamoDb:StravaTokensTable");

        var sessionId = Request.Cookies.TryGetValue("cc_session", out var s) ? s : null;
        if (string.IsNullOrWhiteSpace(sessionId)) return Unauthorized(new { status = "missing_session" });

        var userId = await ResolveUserIdFromSessionAsync(sessionsTable, sessionId, ct);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized(new { status = "invalid_session" });

        // 1) Disconnect (best-effort remote + local cleanup)
        var dr = await StravaDisconnect.BestEffortDisconnectAsync(_httpClientFactory, _ddb, mainTable, userId, ct);

        // 2) Audit (after durable attempt completes)
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
            correlationId: HttpContext.TraceIdentifier,
            ct: ct
        );

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
}
