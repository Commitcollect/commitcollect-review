using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc;

namespace Commitcollect.api.Controllers;

[ApiController]
[Route("")]
public sealed class LogoutController : ControllerBase
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly IConfiguration _config;

    public LogoutController(IAmazonDynamoDB ddb, IConfiguration config)
    {
        _ddb = ddb;
        _config = config;
    }

    // GET /logout  -> FULL logout (local + Cognito Hosted UI)
    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        var sessionsTable = _config["DynamoDb:SessionsTable"];

        // ----- delete local session row (best-effort) -----
        if (!string.IsNullOrWhiteSpace(sessionsTable) &&
            Request.Cookies.TryGetValue("cc_session", out var sessionId) &&
            !string.IsNullOrWhiteSpace(sessionId))
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
                });
            }
            catch
            {
                // Logout should never 500
            }
        }

        // ----- clear cc_session cookie -----
        Response.Cookies.Delete("cc_session", new CookieOptions
        {
            Domain = ".commitcollect.com",
            Path = "/",
            Secure = true,
            SameSite = SameSiteMode.None
        });

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

        // ----- Cognito logout (CUSTOM DOMAIN ONLY) -----
        var clientId = _config["Cognito:ClientId"]; // or Cognito__ClientId if you prefer, but : is safer
        var frontendBaseUrl = _config["FrontendApp:BaseUrl"];

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(frontendBaseUrl))
            return BadRequest("Missing Cognito ClientId or FrontendApp BaseUrl.");

        if (!frontendBaseUrl.EndsWith("/"))
            frontendBaseUrl += "/";

        var logoutUri = Uri.EscapeDataString(frontendBaseUrl);

        var cognitoLogout =
            $"https://auth.commitcollect.com/logout" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&logout_uri={logoutUri}";

        return Redirect(cognitoLogout);

    }
}
