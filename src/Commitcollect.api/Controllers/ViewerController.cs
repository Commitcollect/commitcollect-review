using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc;

namespace Commitcollect.api.Controllers;

[ApiController]
[Route("")]
public sealed class ViewerController : ControllerBase
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly IConfiguration _config;

    public ViewerController(IAmazonDynamoDB ddb, IConfiguration config)
    {
        _ddb = ddb;
        _config = config;
    }

    [HttpGet("viewer")]
    public async Task<IActionResult> GetViewer(CancellationToken ct)
    {
        var sessionsTable = _config["DynamoDb:SessionsTable"];
        var mainTable = _config["DynamoDb:StravaTokensTable"]; // your CommitCollect table

        if (string.IsNullOrWhiteSpace(sessionsTable) || string.IsNullOrWhiteSpace(mainTable))
            return Problem("Missing Dynamo configuration");

        // 1️⃣ Resolve session from cookie
        if (!Request.Cookies.TryGetValue("cc_session", out var sessionId) ||
            string.IsNullOrWhiteSpace(sessionId))
        {
            return Ok(new { signedIn = false });
        }

        var sessionResp = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = sessionsTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = $"SESSION#{sessionId}" },
                ["SK"] = new AttributeValue { S = "META" }
            },
            ConsistentRead = true
        }, ct);

        if (sessionResp.Item is null || sessionResp.Item.Count == 0)
            return Ok(new { signedIn = false });

        if (!sessionResp.Item.TryGetValue("userId", out var uidAv) ||
            string.IsNullOrWhiteSpace(uidAv.S))
        {
            return Ok(new { signedIn = false });
        }

        var userId = uidAv.S!;

        // 2️⃣ Load PROFILE
        var profileResp = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = mainTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = $"USER#{userId}" },
                ["SK"] = new AttributeValue { S = "PROFILE" }
            },
            ConsistentRead = true
        }, ct);

        var email = "";
        var plan = "free";
        var role = "user";

        if (profileResp.Item is not null && profileResp.Item.Count > 0)
        {
            if (profileResp.Item.TryGetValue("email", out var emailAv) &&
                !string.IsNullOrWhiteSpace(emailAv.S))
                email = emailAv.S!;

            if (profileResp.Item.TryGetValue("plan", out var planAv) &&
                !string.IsNullOrWhiteSpace(planAv.S))
                plan = planAv.S!;

            if (profileResp.Item.TryGetValue("role", out var roleAv) &&
                !string.IsNullOrWhiteSpace(roleAv.S))
                role = roleAv.S!;
        }

        // 3️⃣ Load STRAVA CONNECTION
        var connResp = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = mainTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = $"USER#{userId}" },
                ["SK"] = new AttributeValue { S = "STRAVA#CONNECTION" }
            },
            ConsistentRead = true
        }, ct);

        long athleteId = 0;
        long expiresAtUtc = 0;
        var scope = "";

        if (connResp.Item is not null && connResp.Item.Count > 0)
        {
            if (connResp.Item.TryGetValue("athleteId", out var aidAv) &&
                !string.IsNullOrWhiteSpace(aidAv.N))
                long.TryParse(aidAv.N, out athleteId);

            if (connResp.Item.TryGetValue("expiresAtUtc", out var expAv) &&
                !string.IsNullOrWhiteSpace(expAv.N))
                long.TryParse(expAv.N, out expiresAtUtc);

            if (connResp.Item.TryGetValue("scope", out var scopeAv) &&
                !string.IsNullOrWhiteSpace(scopeAv.S))
                scope = scopeAv.S!;
        }

        var stravaConnected = athleteId > 0;

        return Ok(new
        {
            signedIn = true,
            user = new
            {
                userId,
                email
            },
            role,
            plan,
            strava = new
            {
                connected = stravaConnected,
                athleteId,
                expiresAtUtc,
                scope
            }
        });
    }
}
