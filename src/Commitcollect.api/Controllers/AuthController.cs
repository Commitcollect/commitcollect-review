using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Commitcollect.api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.Eventing.Reader;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAmazonDynamoDB _ddb;
    private readonly IConfiguration _config;
    private readonly AuditEventService _audit;


    public AuthController(
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



    [HttpGet("debug/config")]
    public IActionResult DebugConfig()
    {
        var value1 = _config["DynamoDb__SessionsTable"];
        var value2 = _config["DynamoDb:SessionsTable"];

        return Ok(new
        {
            doubleUnderscore = value1,
            colonVersion = value2
        });
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        var clientId = "7g5ua9dd7sn1ma6kr8j1piakpp";
        var redirectUri = "https://api.commitcollect.com/auth/callback";

        var url =
            "https://auth.commitcollect.com/oauth2/authorize" +
            $"?client_id={clientId}" +
            $"&response_type=code" +
            $"&scope=openid+email" +
            $"&prompt=login" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}";


        return Redirect(url);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return BadRequest(new { status = "denied", error });

        if (string.IsNullOrWhiteSpace(code))
            return BadRequest(new { status = "missing_code" });

        var sessionsTable = _config["DynamoDb:SessionsTable"]; // DynamoDb__SessionsTable
        if (string.IsNullOrWhiteSpace(sessionsTable))
            return Problem("Missing DynamoDb:SessionsTable");

        var mainTable = _config["DynamoDb:StravaTokensTable"];
        if (string.IsNullOrWhiteSpace(mainTable))
            return Problem("Missing DynamoDb:StravaTokensTable");



        var tokenEndpoint = _config["Cognito:TokenEndpoint"]; // Cognito__TokenEndpoint
        var clientId = _config["Cognito:ClientId"];           // Cognito__ClientId
        var redirectUri = _config["Cognito:RedirectUri"];     // Cognito__RedirectUri

        if (string.IsNullOrWhiteSpace(tokenEndpoint) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUri))
            return Problem("Missing Cognito config (TokenEndpoint/ClientId/RedirectUri)");

        // Exchange code for tokens
        var http = _httpClientFactory.CreateClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        });

        var resp = await http.PostAsync(tokenEndpoint, content);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode, new { status = "token_exchange_failed", body });

        using var doc = JsonDocument.Parse(body);
        var idToken = doc.RootElement.GetProperty("id_token").GetString();
        var refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;

        if (string.IsNullOrWhiteSpace(idToken))
            return Problem("Missing id_token from Cognito token response");

        // Parse user identity (sub)
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(idToken);

        var sub = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        var email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

        if (string.IsNullOrWhiteSpace(sub))
            return Problem("id_token missing sub claim");

        var userId = sub; // MVP: userId == cognito sub

        var isNewUser = false;

        try
        {
            var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            await _ddb.PutItemAsync(new PutItemRequest
            {
                TableName = mainTable,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new AttributeValue { S = $"USER#{userId}" },
                    ["SK"] = new AttributeValue { S = "PROFILE" },

                    ["entityType"] = new AttributeValue { S = "UserProfile" },
                    ["userId"] = new AttributeValue { S = userId },
                    ["email"] = new AttributeValue { S = email ?? "" },
                    ["createdAtUtc"] = new AttributeValue { N = createdAt.ToString() }
                },
                // Stronger + explicit for composite key (either is fine; this avoids any ambiguity)
                ConditionExpression = "attribute_not_exists(PK) AND attribute_not_exists(SK)"
            });

            isNewUser = true;
        }
        catch (ConditionalCheckFailedException)
        {
            // profile already exists => not a signup
        }

        // Create session
        var sessionId = NewOpaqueId();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiresAt = now + (60L * 60L * 24L * 30L); // 30 days

        await _ddb.PutItemAsync(new PutItemRequest
        {
            TableName = sessionsTable,
            Item = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = $"SESSION#{sessionId}" },
                ["SK"] = new AttributeValue { S = "META" },
                ["entityType"] = new AttributeValue { S = "Session" },
                ["userId"] = new AttributeValue { S = userId },
                ["email"] = new AttributeValue { S = email ?? "" },
                ["refreshToken"] = new AttributeValue { S = refreshToken ?? "" },
                ["createdAtUtc"] = new AttributeValue { N = now.ToString() },
                ["ExpiresAt"] = new AttributeValue { N = expiresAt.ToString() },

                // ✅ GSI_UserSessions
                ["GSI1PK"] = new AttributeValue { S = $"USER#{userId}" },
                ["GSI1SK"] = new AttributeValue { S = $"SESSION#{now}#{sessionId}" }
            }
        });

        // Single audit write: AUTH_SIGNUP (first) OR AUTH_LOGIN (subsequent)
        var eventType = isNewUser ? "AUTH_SIGNUP" : "AUTH_LOGIN";

        await _audit.TryWriteAsync(
            HttpContext,
            userId: userId,
            eventType: eventType,
            result: "success",
            data: new Dictionary<string, AttributeValue>
            {
                ["sessionId"] = new AttributeValue { S = sessionId },
                ["method"] = new AttributeValue { S = "cognito_hosted_ui" },
                ["email"] = new AttributeValue { S = email ?? "" }
            },
            metrics: new Dictionary<string, AttributeValue>
            {
                ["sessionTtlDays"] = new AttributeValue { N = "30" }
            },
            correlationId: HttpContext.TraceIdentifier
        );



        // Set cookie (cross-subdomain)
        Response.Cookies.Append("cc_session", sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,      // IMPORTANT for cross-site OAuth redirects
            Domain = ".commitcollect.com",
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });

        // Redirect to frontend (reviewer-friendly)
        var frontendBaseUrl = _config["FrontendApp:BaseUrl"];
        if (string.IsNullOrWhiteSpace(frontendBaseUrl))
            frontendBaseUrl = "https://app.commitcollect.com"; // default for production

        return Redirect($"{frontendBaseUrl.TrimEnd('/')}/app?login=success");
    }

    private static string NewOpaqueId()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
