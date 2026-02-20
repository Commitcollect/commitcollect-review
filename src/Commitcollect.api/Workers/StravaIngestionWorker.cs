using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Commitcollect.api.Workers;

/// <summary>
/// CommitCollect Strava ingestion worker.
/// Invoked asynchronously by the webhook lambda with a small payload:
/// {
///   "object_type":"activity",
///   "object_id":123,
///   "aspect_type":"create|update|delete",
///   "owner_id":51557072,
///   "event_time":1730000000
/// }
/// </summary>
public sealed class StravaIngestionWorker
{
    private static readonly HttpClient Http = new HttpClient();

    private readonly IAmazonDynamoDB _ddb;
    private readonly string _commitCollectTable;
    private readonly string _idempotencyTable;
    private readonly int _idempotencyTtlDays;

    private readonly string _stravaClientId;
    private readonly string _stravaClientSecret;

    public StravaIngestionWorker()
        : this(new AmazonDynamoDBClient(), Environment.GetEnvironmentVariable("DynamoDb__StravaTokensTable"))
    { }

    // Allow DI/testing
    public StravaIngestionWorker(IAmazonDynamoDB ddb, string? commitCollectTable)
    {
        _ddb = ddb;

        _commitCollectTable = commitCollectTable ?? throw new InvalidOperationException("Missing env var DynamoDb__StravaTokensTable (CommitCollect table name).");
        _idempotencyTable = Environment.GetEnvironmentVariable("DynamoDb__IdempotencyTableName") ?? "CommitCollectIdempotency";

        var ttlDaysRaw = Environment.GetEnvironmentVariable("DynamoDb__IdempotencyTtlDays");
        _idempotencyTtlDays = int.TryParse(ttlDaysRaw, out var d) ? d : 7;

        _stravaClientId = Environment.GetEnvironmentVariable("Strava__ClientId") ?? throw new InvalidOperationException("Missing env var Strava__ClientId.");
        _stravaClientSecret = Environment.GetEnvironmentVariable("Strava__ClientSecret") ?? throw new InvalidOperationException("Missing env var Strava__ClientSecret.");
    }

    public async Task Handler(StravaWebhookEvent evt, ILambdaContext context)
    {
        // 0) Only ingest activities for MVP
        if (!string.Equals(evt.object_type, "activity", StringComparison.OrdinalIgnoreCase))
        {
            context.Logger.LogLine($"Skipping non-activity webhook: object_type={evt.object_type}");
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 1) Idempotency FIRST
        var idemKey = BuildIdempotencyKey(evt);
        var inserted = await TryPutIdempotencyAsync(idemKey, now);

        if (!inserted)
        {
            context.Logger.LogLine($"Idempotency hit (already processed): {idemKey}");
            return;
        }

        // 2) Resolve athlete -> user + connection via GSI1
        var athleteId = evt.owner_id;
        var connection = await GetStravaConnectionByAthleteAsync(athleteId);

        if (connection == null)
        {
            context.Logger.LogLine($"No connection found for athleteId={athleteId}. Ignoring event.");
            return;
        }

        // 3) Refresh token if needed
        var accessToken = connection.AccessToken;
        var refreshToken = connection.RefreshToken;
        var expiresAtUtc = connection.ExpiresAtUtc;

        if (expiresAtUtc <= now + 300) // 5 min buffer
        {
            context.Logger.LogLine($"Token expiring soon (expiresAtUtc={expiresAtUtc}). Refreshing...");
            var refreshed = await RefreshAccessTokenAsync(refreshToken);

            accessToken = refreshed.access_token;
            refreshToken = refreshed.refresh_token;
            expiresAtUtc = refreshed.expires_at;

            await UpdateConnectionTokensAsync(connection.UserId, athleteId, accessToken, refreshToken, expiresAtUtc, now);
            context.Logger.LogLine($"Token refreshed. New expiresAtUtc={expiresAtUtc}");
        }

        // 4) If delete -> mark workout deleted
        if (string.Equals(evt.aspect_type, "delete", StringComparison.OrdinalIgnoreCase))
        {
            await UpsertWorkoutDeletedAsync(connection.UserId, athleteId, evt.object_id, evt.aspect_type, evt.event_time, now);
            context.Logger.LogLine($"Marked workout deleted activityId={evt.object_id} userId={connection.UserId}");
            return;
        }

        // 5) Fetch activity details from Strava
        var activity = await FetchActivityAsync(evt.object_id, accessToken);

        // 6) Project fields into small payloadJson (NO polyline)
        var projected = ProjectActivity(activity);

        // 7) Persist workout item (no GSI fields)
        await UpsertWorkoutAsync(connection.UserId, athleteId, evt.object_id, evt.aspect_type, evt.event_time, projected, now);

        context.Logger.LogLine($"Ingested activityId={evt.object_id} userId={connection.UserId} aspect={evt.aspect_type}");
    }

    // -------------------------
    // Idempotency
    // -------------------------

    private static string BuildIdempotencyKey(StravaWebhookEvent evt)
        => $"STRAVA#EVT#{evt.object_type}#{evt.aspect_type}#{evt.object_id}#{evt.event_time}";

    private async Task<bool> TryPutIdempotencyAsync(string key, long nowEpoch)
    {
        var ttlEpoch = nowEpoch + (_idempotencyTtlDays * 86400L);

        try
        {
            await _ddb.PutItemAsync(new PutItemRequest
            {
                TableName = _idempotencyTable,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["IdempotencyKey"] = new AttributeValue { S = key },
                    ["createdAtUtc"] = new AttributeValue { N = nowEpoch.ToString() },
                    // Your TTL attribute is configured as ttlEpoch
                    ["ttlEpoch"] = new AttributeValue { N = ttlEpoch.ToString() }
                },
                ConditionExpression = "attribute_not_exists(IdempotencyKey)"
            });

            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    // -------------------------
    // Dynamo: connection lookup
    // -------------------------

    private async Task<StravaConnection?> GetStravaConnectionByAthleteAsync(long athleteId)
    {
        var gsiPk = $"STRAVA#ATHLETE#{athleteId}";

        var resp = await _ddb.QueryAsync(new QueryRequest
        {
            TableName = _commitCollectTable,
            IndexName = "GSI1",
            KeyConditionExpression = "GSI1PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new AttributeValue { S = gsiPk }
            },
            Limit = 1
        });

        var item = resp.Items?.FirstOrDefault();
        if (item == null) return null;

        // PK = USER#<userId>
        var pk = item.TryGetValue("PK", out var pkVal) ? pkVal.S : null;
        var userId = (pk != null && pk.StartsWith("USER#")) ? pk.Substring("USER#".Length) : null;

        if (string.IsNullOrWhiteSpace(userId)) return null;

        return new StravaConnection(
            UserId: userId,
            AccessToken: item.TryGetValue("accessToken", out var at) ? at.S ?? "" : "",
            RefreshToken: item.TryGetValue("refreshToken", out var rt) ? rt.S ?? "" : "",
            ExpiresAtUtc: item.TryGetValue("expiresAtUtc", out var ex) ? long.Parse(ex.N) : 0
        );
    }

    private async Task UpdateConnectionTokensAsync(string userId, long athleteId, string accessToken, string refreshToken, long expiresAtUtc, long nowEpoch)
    {
        // PK/SK fixed for StravaConnection
        await _ddb.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _commitCollectTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = $"USER#{userId}" },
                ["SK"] = new AttributeValue { S = "STRAVA#CONNECTION" }
            },
            UpdateExpression = "SET accessToken = :at, refreshToken = :rt, expiresAtUtc = :ex, updatedAtUtc = :u, athleteId = :aid, status = :st, source = :src",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":at"] = new AttributeValue { S = accessToken },
                [":rt"] = new AttributeValue { S = refreshToken },
                [":ex"] = new AttributeValue { N = expiresAtUtc.ToString() },
                [":u"] = new AttributeValue { N = nowEpoch.ToString() },
                [":aid"] = new AttributeValue { N = athleteId.ToString() },
                [":st"] = new AttributeValue { S = "CONNECTED" },
                [":src"] = new AttributeValue { S = "STRAVA" }
            }
        });
    }

    // -------------------------
    // Strava API
    // -------------------------

    private async Task<JsonElement> FetchActivityAsync(long activityId, string accessToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.strava.com/api/v3/activities/{activityId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Strava GET /activities/{activityId} failed: {(int)resp.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private async Task<(string access_token, string refresh_token, long expires_at)> RefreshAccessTokenAsync(string refreshToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _stravaClientId,
            ["client_secret"] = _stravaClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });

        var resp = await Http.PostAsync("https://www.strava.com/oauth/token", content);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Strava refresh token failed: {(int)resp.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        return (
            access_token: root.GetProperty("access_token").GetString() ?? "",
            refresh_token: root.GetProperty("refresh_token").GetString() ?? "",
            expires_at: root.GetProperty("expires_at").GetInt64()
        );
    }

    // -------------------------
    // Projection (payloadJson)
    // -------------------------

    private static ProjectedActivity ProjectActivity(JsonElement a)
    {
        // Helper getters (safe-ish)
        static string? S(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static double? D(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        static long? L(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;
        static bool? B(JsonElement e, string name) => e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : null;

        // sport_type preferred (Strava newer), fallback to type
        var sportType = S(a, "sport_type") ?? S(a, "type") ?? "Unknown";

        return new ProjectedActivity
        {
            id = L(a, "id") ?? 0,
            name = S(a, "name"),
            sport_type = sportType,
            start_date = S(a, "start_date"),
            start_date_local = S(a, "start_date_local"),
            timezone = S(a, "timezone"),

            distance = D(a, "distance"),
            moving_time = L(a, "moving_time"),
            elapsed_time = L(a, "elapsed_time"),
            total_elevation_gain = D(a, "total_elevation_gain"),

            average_speed = D(a, "average_speed"),
            max_speed = D(a, "max_speed"),

            average_heartrate = D(a, "average_heartrate"),
            max_heartrate = D(a, "max_heartrate"),

            average_watts = D(a, "average_watts"),
            kilojoules = D(a, "kilojoules"),
            average_cadence = D(a, "average_cadence"),

            trainer = B(a, "trainer"),
            commute = B(a, "commute"),
            manual = B(a, "manual"),

            device_watts = B(a, "device_watts"),
            has_heartrate = B(a, "has_heartrate"),

            achievement_count = L(a, "achievement_count"),
            kudos_count = L(a, "kudos_count"),
            comment_count = L(a, "comment_count"),

            gear_id = S(a, "gear_id"),
            external_id = S(a, "external_id"),
        };
    }

    // -------------------------
    // Dynamo: workout storage
    // -------------------------

    private async Task UpsertWorkoutAsync(
        string userId,
        long athleteId,
        long activityId,
        string aspectType,
        long eventTime,
        ProjectedActivity projected,
        long nowEpoch)
    {
        var payloadJson = JsonSerializer.Serialize(projected);

        // Also store extracted fields at top-level (fast queries)
        var startDateUtcEpoch = TryParseStartDateToEpoch(projected.start_date);

        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = $"USER#{userId}" },
            ["SK"] = new AttributeValue { S = $"WORKOUT#STRAVA#{activityId}" },

            ["entityType"] = new AttributeValue { S = "Workout" },
            ["source"] = new AttributeValue { S = "STRAVA" },
            ["athleteId"] = new AttributeValue { N = athleteId.ToString() },
            ["activityId"] = new AttributeValue { N = activityId.ToString() },

            ["aspectType"] = new AttributeValue { S = aspectType },
            ["eventTimeUtc"] = new AttributeValue { N = eventTime.ToString() },

            ["payloadJson"] = new AttributeValue { S = payloadJson },

            ["sportType"] = new AttributeValue { S = projected.sport_type ?? "Unknown" },

            ["distanceMeters"] = new AttributeValue { N = ((long)Math.Round(projected.distance ?? 0)).ToString() },
            ["movingTimeSec"] = new AttributeValue { N = (projected.moving_time ?? 0).ToString() },
            ["elapsedTimeSec"] = new AttributeValue { N = (projected.elapsed_time ?? 0).ToString() },

            ["startDateUtc"] = new AttributeValue { N = startDateUtcEpoch.ToString() },

            ["ingestedAtUtc"] = new AttributeValue { N = nowEpoch.ToString() },
            ["updatedAtUtc"] = new AttributeValue { N = nowEpoch.ToString() }
        };

        // IMPORTANT: Do NOT include GSI1PK/GSI1SK here. Leave them absent.

        await _ddb.PutItemAsync(new PutItemRequest
        {
            TableName = _commitCollectTable,
            Item = item
        });
    }

    private async Task UpsertWorkoutDeletedAsync(
        string userId,
        long athleteId,
        long activityId,
        string aspectType,
        long eventTime,
        long nowEpoch)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = $"USER#{userId}" },
            ["SK"] = new AttributeValue { S = $"WORKOUT#STRAVA#{activityId}" },

            ["entityType"] = new AttributeValue { S = "Workout" },
            ["source"] = new AttributeValue { S = "STRAVA" },
            ["athleteId"] = new AttributeValue { N = athleteId.ToString() },
            ["activityId"] = new AttributeValue { N = activityId.ToString() },

            ["aspectType"] = new AttributeValue { S = aspectType },
            ["eventTimeUtc"] = new AttributeValue { N = eventTime.ToString() },

            ["status"] = new AttributeValue { S = "DELETED" },

            ["payloadJson"] = new AttributeValue { S = "{}" },

            ["ingestedAtUtc"] = new AttributeValue { N = nowEpoch.ToString() },
            ["updatedAtUtc"] = new AttributeValue { N = nowEpoch.ToString() }
        };

        await _ddb.PutItemAsync(new PutItemRequest
        {
            TableName = _commitCollectTable,
            Item = item
        });
    }

    private static long TryParseStartDateToEpoch(string? isoUtc)
    {
        if (string.IsNullOrWhiteSpace(isoUtc)) return 0;
        if (DateTimeOffset.TryParse(isoUtc, out var dto))
            return dto.ToUnixTimeSeconds();
        return 0;
    }

    // -------------------------
    // Models
    // -------------------------

    public sealed class StravaWebhookEvent
    {
        public string object_type { get; set; } = "";
        public long object_id { get; set; }
        public string aspect_type { get; set; } = "";
        public long owner_id { get; set; }
        public long event_time { get; set; }
    }

    private sealed record StravaConnection(string UserId, string AccessToken, string RefreshToken, long ExpiresAtUtc);

    private sealed class ProjectedActivity
    {
        public long id { get; set; }
        public string? name { get; set; }
        public string? sport_type { get; set; }
        public string? start_date { get; set; }
        public string? start_date_local { get; set; }
        public string? timezone { get; set; }

        public double? distance { get; set; }
        public long? moving_time { get; set; }
        public long? elapsed_time { get; set; }
        public double? total_elevation_gain { get; set; }

        public double? average_speed { get; set; }
        public double? max_speed { get; set; }

        public double? average_heartrate { get; set; }
        public double? max_heartrate { get; set; }

        public double? average_watts { get; set; }
        public double? kilojoules { get; set; }
        public double? average_cadence { get; set; }

        public bool? trainer { get; set; }
        public bool? commute { get; set; }
        public bool? manual { get; set; }

        public bool? device_watts { get; set; }
        public bool? has_heartrate { get; set; }

        public long? achievement_count { get; set; }
        public long? kudos_count { get; set; }
        public long? comment_count { get; set; }

        public string? gear_id { get; set; }
        public string? external_id { get; set; }
    }
}
