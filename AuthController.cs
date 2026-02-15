using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAmazonDynamoDB _ddb;
    private readonly IConfiguration _config;

    public AuthController(IHttpClientFactory httpClientFactory, IAmazonDynamoDB ddb, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _ddb = ddb;
        _config = config;
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
                ["ExpiresAt"] = new AttributeValue { N = expiresAt.ToString() }
            }
        });

        // Set cookie (cross-subdomain)
        Response.Cookies.Append("cc_session", sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Domain = ".commitcollect.com",
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });

        return Ok(new { status = "session_created", userId });
    }

    private static string NewOpaqueId()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
