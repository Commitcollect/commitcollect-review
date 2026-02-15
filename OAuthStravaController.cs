using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Commitcollect.api.Configuration;
using Commitcollect.api.Exceptions;


namespace Commitcollect.api.Controllers;

[ApiController]
[Route("oauth/strava")]


<<<<<<< Updated upstream
=======
//


>>>>>>> Stashed changes
public class OAuthStravaController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAmazonDynamoDB _ddb;
    private readonly StravaOptions _strava;
    private readonly OAuthOptions _oauth;
    private readonly AppOptions _app;
    private readonly IConfiguration _config;

    public OAuthStravaController(
        IHttpClientFactory httpClientFactory,
        IAmazonDynamoDB ddb,
        IOptions<StravaOptions> stravaOptions,
        IOptions<OAuthOptions> oauthOptions,
        IOptions<AppOptions> appOptions,
        IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _ddb = ddb;
        _strava = stravaOptions.Value;
        _oauth = oauthOptions.Value;
        _app = appOptions.Value;
        _config = config;
    }

    // GET /oauth/strava/start?userId=123
    // MVP: userId comes from query string. Later: derive from JWT auth.
    [HttpGet("start")]
    public IActionResult Start([FromQuery] string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest("Missing userId");

        if (string.IsNullOrWhiteSpace(_strava.ClientId))
            return Problem("Strava ClientId missing. Set Strava:ClientId in user-secrets / env vars.");

        if (string.IsNullOrWhiteSpace(_strava.RedirectUri))
            return Problem("Strava RedirectUri missing. Set Strava:RedirectUri in appsettings / env vars.");

        if (string.IsNullOrWhiteSpace(_oauth.StateSigningKey))
            return Problem("OAuth StateSigningKey missing. Set OAuth:StateSigningKey in user-secrets / env vars.");

        var state = CreateSignedState(userId);

        // You already decided to request activity:read_all (good for private activities)
        var authorizeUrl =
            "https://www.strava.com/oauth/authorize" +
            $"?client_id={Uri.EscapeDataString(_strava.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(_strava.RedirectUri)}" +
            $"&response_type=code" +
            $"&approval_prompt=auto" +
            $"&scope={Uri.EscapeDataString("read,activity:read_all")}" +
            $"&state={Uri.EscapeDataString(state)}";

        return Redirect(authorizeUrl);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery] string? scope)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return BadRequest(new { status = "denied", error });

        if (string.IsNullOrWhiteSpace(_oauth.StateSigningKey))
            return BadRequest(new { status = "missing_state_signing_key" });


        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return BadRequest(new { status = "missing_code_or_state" });

        if (!TryValidateSignedState(state, out var userId))
            return BadRequest(new { status = "invalid_state" });

        var token = await ExchangeCodeForTokenAsync(code);

        try
        {
            await SaveTokenWithUniquenessAsync(userId, token, scope);
        }
        catch (StravaAlreadyConnectedException ex)
        {
            return Conflict(new { status = "athlete_already_connected", message = ex.Message });
        }


        return Ok(new
        {
            status = "connected",
            userId,
            athleteId = token.Athlete?.Id ?? 0,
            expiresAtUtc = token.Expires_At
        });
    }


    private async Task<StravaTokenResponse> ExchangeCodeForTokenAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(_strava.ClientId))
            throw new InvalidOperationException("Strava ClientId missing.");

        if (string.IsNullOrWhiteSpace(_strava.ClientSecret))
            throw new InvalidOperationException("Strava ClientSecret missing. Set Strava:ClientSecret in user-secrets / env vars.");

        var http = _httpClientFactory.CreateClient();

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _strava.ClientId,
            ["client_secret"] = _strava.ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
        });

        var resp = await http.PostAsync("https://www.strava.com/oauth/token", content);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Strava token exchange failed: {(int)resp.StatusCode} {body}");

        var token = JsonSerializer.Deserialize<StravaTokenResponse>(
            body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return token ?? throw new InvalidOperationException("Failed to parse Strava token response.");
    }

    /// <summary>
    /// Saves Strava connection AND enforces uniqueness of Strava athlete across users.
    /// - Creates/updates an "ownership lock" item: PK=STRAVA#ATHLETE#<id>, SK=OWNER
    /// - Writes connection item: PK=USER#<userId>, SK=STRAVA#CONNECTION (this is the only item that uses GSI1)
    ///
    /// Behavior:
    /// - If athleteId is unclaimed -> connect succeeds.
    /// - If athleteId is claimed by SAME user -> reconnect/refresh succeeds.
    /// - If athleteId is claimed by DIFFERENT user -> transaction fails (blocked).
    /// </summary>
    private async Task SaveTokenWithUniquenessAsync(string userId, StravaTokenResponse token, string? scope)
    {
        var tableName = _config["DynamoDb:StravaTokensTable"]; // env: DynamoDb__StravaTokensTable
        if (string.IsNullOrWhiteSpace(tableName))
            throw new InvalidOperationException("DynamoDb:StravaTokensTable missing from config.");

        var athleteId = token.Athlete?.Id ?? 0;
        if (athleteId <= 0)
            throw new InvalidOperationException("Strava token response missing athlete.id (cannot enforce uniqueness).");

        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();



        // --- Ownership lock item (NOT indexed) ---
        // PK = STRAVA#ATHLETE#<athleteId>, SK = OWNER
        // Condition: either it doesn't exist OR it's already owned by this user.
        var ownershipItem = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = $"STRAVA#ATHLETE#{athleteId}" },
            ["SK"] = new AttributeValue { S = "OWNER" },

            ["entityType"] = new AttributeValue { S = "AthleteOwnership" },
            ["source"] = new AttributeValue { S = "STRAVA" },
            ["athleteId"] = new AttributeValue { N = athleteId.ToString() },

            ["userId"] = new AttributeValue { S = userId },
            ["updatedAtUtc"] = new AttributeValue { N = nowEpoch.ToString() }
        };

        // --- Connection item (ONLY item that uses GSI1PK/GSI1SK) ---
        var connectionItem = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = $"USER#{userId}" },
            ["SK"] = new AttributeValue { S = "STRAVA#CONNECTION" },

            ["entityType"] = new AttributeValue { S = "StravaConnection" },
            ["status"] = new AttributeValue { S = "CONNECTED" },
            ["source"] = new AttributeValue { S = "STRAVA" },

            ["athleteId"] = new AttributeValue { N = athleteId.ToString() },

            ["accessToken"] = new AttributeValue { S = token.Access_Token ?? "" },
            ["refreshToken"] = new AttributeValue { S = token.Refresh_Token ?? "" },

            // ✅ expiresAtUtc only (epoch seconds)
            ["expiresAtUtc"] = new AttributeValue { N = token.Expires_At.ToString() },

            ["scope"] = new AttributeValue { S = scope ?? "" },
            ["updatedAtUtc"] = new AttributeValue { N = nowEpoch.ToString() },

            // ✅ GSI1 is exclusive to StravaConnection items
            ["GSI1PK"] = new AttributeValue { S = $"STRAVA#ATHLETE#{athleteId}" },
            ["GSI1SK"] = new AttributeValue { S = $"USER#{userId}" }
        };

        try
        {
            await _ddb.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems = new List<TransactWriteItem>
                {
                    new TransactWriteItem
                    {
                        Put = new Put
                        {
                            TableName = tableName,
                            Item = ownershipItem,
                            // Allow if:
                            // - ownership doesn't exist, OR
                            // - ownership exists and is already for this user
                            ConditionExpression = "attribute_not_exists(PK) OR userId = :uid",
                            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                            {
                                [":uid"] = new AttributeValue { S = userId }
                            }
                        }
                    },
                    new TransactWriteItem
                    {
                        Put = new Put
                        {
                            TableName = tableName,
                            Item = connectionItem
                            // No condition here: reconnect should overwrite tokens for this user.
                        }
                    }
                }
            });
        }
        catch (TransactionCanceledException tex)
        {
            // Only translate conditional failures into "already connected"
            if (tex.CancellationReasons?.Any(r => r?.Code == "ConditionalCheckFailed") == true)
            {
                throw new StravaAlreadyConnectedException(
                    "This Strava account is already connected to another CommitCollect user.", tex);
            }

            // Otherwise let it surface as real infrastructure error
            throw;
        }
    }

    // ===== STATE SIGNING =====
    // state = base64url(payload).base64url(hmac)
    // payload includes issued timestamp to expire it.

    private string CreateSignedState(string userId)
    {
        var payload = JsonSerializer.Serialize(new StatePayload
        {
            userId = userId,
            issued = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var keyBytes = Encoding.UTF8.GetBytes(_oauth.StateSigningKey);

        using var hmac = new HMACSHA256(keyBytes);
        var signature = hmac.ComputeHash(payloadBytes);

        return $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
    }

    private bool TryValidateSignedState(string state, out string userId)
    {
        userId = "";

        var parts = state.Split('.', 2);
        if (parts.Length != 2) return false;

        byte[] payloadBytes;
        byte[] signatureBytes;

        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            signatureBytes = Base64UrlDecode(parts[1]);
        }
        catch
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_oauth.StateSigningKey));
        var expectedSig = hmac.ComputeHash(payloadBytes);

        if (!CryptographicOperations.FixedTimeEquals(expectedSig, signatureBytes))
            return false;

        StatePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<StatePayload>(payloadBytes);
        }
        catch
        {
            return false;
        }

        if (payload == null || string.IsNullOrWhiteSpace(payload.userId))
            return false;

        // Expire state after 10 minutes
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now - payload.issued > 600) return false;

        userId = payload.userId;
        return true;
    }

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        input = input.Replace('-', '+').Replace('_', '/');
        switch (input.Length % 4)
        {
            case 2: input += "=="; break;
            case 3: input += "="; break;
        }
        return Convert.FromBase64String(input);
    }

    private sealed class StatePayload
    {
        public string userId { get; set; } = "";
        public long issued { get; set; }
    }

    // NOTE: Token response includes athlete details needed for uniqueness + GSI mapping
    private sealed class StravaTokenResponse
    {
        public string? Access_Token { get; set; }
        public string? Refresh_Token { get; set; }
        public long Expires_At { get; set; }
        public StravaAthlete? Athlete { get; set; }
    }

    private sealed class StravaAthlete
    {
        public long Id { get; set; }
    }
}
