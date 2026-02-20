using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Commitcollect.api.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("strava")]
public class StravaStatusController : ControllerBase
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly IConfiguration _config;
    private readonly ISessionResolver _sessions;

    public StravaStatusController(IAmazonDynamoDB ddb, IConfiguration config, ISessionResolver sessions)
    {
        _ddb = ddb;
        _config = config;
        _sessions = sessions;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var sessionsTable = _config["DynamoDb:SessionsTable"];
        var table = _config["DynamoDb:StravaTokensTable"]; // CommitCollect

        var session = await _sessions.ResolveAsync(HttpContext);
        if (session is null)
            return Unauthorized(new { status = "invalid_session" });

        var userId = session.UserId;

        // 1️⃣ STRAVA#CONNECTION
        var conn = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = table,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = $"USER#{userId}" },
                ["SK"] = new AttributeValue { S = "STRAVA#CONNECTION" }
            },
            ConsistentRead = true
        }, ct);

        var connected = conn.Item != null && conn.Item.Count > 0;

        long? athleteId = null;
        long? expiresAtUtc = null;

        if (connected && conn.Item != null)
        {
            if (conn.Item.TryGetValue("athleteId", out var a) &&
                !string.IsNullOrWhiteSpace(a.N) &&
                long.TryParse(a.N, out var aid))
            {
                athleteId = aid;
            }

            if (conn.Item.TryGetValue("expiresAtUtc", out var e) &&
                !string.IsNullOrWhiteSpace(e.N) &&
                long.TryParse(e.N, out var exp))
            {
                expiresAtUtc = exp;
            }
        }

        // 2️⃣ Workout summary (paged + capped)
        int workoutCount = 0;
        long? latestActivityAtUtc = null;

        const int MaxWorkoutsToInspect = 3000;
        int inspected = 0;

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
                ProjectionExpression = "startDateUtc, isDeleted",
                ExclusiveStartKey = lastKey,
                Limit = 200
            }, ct);

            foreach (var item in q.Items)
            {
                inspected++;
                if (inspected > MaxWorkoutsToInspect)
                {
                    return StatusCode(409, new
                    {
                        error = "too_many_workouts_for_strava_status",
                        max = MaxWorkoutsToInspect
                    });
                }

                if (item.TryGetValue("isDeleted", out var d) && d.BOOL == true)
                    continue;

                workoutCount++;

                if (item.TryGetValue("startDateUtc", out var sdt) &&
                    !string.IsNullOrWhiteSpace(sdt.N) &&
                    long.TryParse(sdt.N, out var start))
                {
                    if (!latestActivityAtUtc.HasValue || start > latestActivityAtUtc.Value)
                        latestActivityAtUtc = start;
                }
            }

            lastKey = q.LastEvaluatedKey;

        } while (lastKey != null && lastKey.Count > 0);

        bool? isFresh = null;
        if (latestActivityAtUtc.HasValue)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            isFresh = (now - latestActivityAtUtc.Value) <= (7 * 24 * 60 * 60);
        }

        return Ok(new
        {
            connected,
            athleteId,
            expiresAtUtc,
            workoutCount,
            latestActivityAtUtc,
            isFresh
        });
    }
}

[ApiController]
public sealed class ActivitiesController : ControllerBase
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly IConfiguration _config;
    private readonly ISessionResolver _sessions;

    private const int DefaultLimit = 10;
    private const int MaxLimit = 50;
    private const int MaxWorkoutsToInspect = 3000;
    private const string SkPrefix = "WORKOUT#STRAVA#";
    private const int PageSize = 200;

    public ActivitiesController(IAmazonDynamoDB ddb, IConfiguration config, ISessionResolver sessions)
    {
        _ddb = ddb;
        _config = config;
        _sessions = sessions;
    }

    // GET /activities/recent?limit=10
    [HttpGet("activities/recent")]
    public async Task<IActionResult> GetRecent([FromQuery] int limit = DefaultLimit, CancellationToken ct = default)
    {
        if (limit <= 0) limit = DefaultLimit;
        if (limit > MaxLimit) limit = MaxLimit;

        var session = await _sessions.ResolveAsync(HttpContext);
        if (session is null) return Unauthorized(new { status = "invalid_session" });

        var table = _config["DynamoDb:StravaTokensTable"] ?? _config["DynamoDb__StravaTokensTable"];
        if (string.IsNullOrWhiteSpace(table)) return Problem("Missing DynamoDb:StravaTokensTable");

        var userId = session.UserId;
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized(new { status = "invalid_session" });

        var inspected = 0;
        var buffer = new List<ActivityDto>(capacity: Math.Min(limit * 5, 250));

        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            ct.ThrowIfCancellationRequested();

            var q = await _ddb.QueryAsync(new QueryRequest
            {
                TableName = table,
                KeyConditionExpression = "#pk = :pk AND begins_with(#sk, :sk)",
                FilterExpression = "attribute_not_exists(#isDeleted) OR #isDeleted <> :true",
                ProjectionExpression = string.Join(", ", new[]
                {
                    "#activityId",
                    "#activityName",
                    "#sportType",
                    "#startDateUtc",
                    "#distanceMeters",
                    "#elevGain",
                    "#isDeleted"
                }),
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#pk"] = "PK",
                    ["#sk"] = "SK",
                    ["#activityId"] = "activityId",
                    ["#activityName"] = "activityName",
                    ["#sportType"] = "sportType",
                    ["#startDateUtc"] = "startDateUtc",
                    ["#distanceMeters"] = "distanceMeters",
                    ["#elevGain"] = "total_elevation_gain",
                    ["#isDeleted"] = "isDeleted"
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue { S = $"USER#{userId}" },
                    [":sk"] = new AttributeValue { S = SkPrefix },
                    [":true"] = new AttributeValue { BOOL = true }
                },
                ExclusiveStartKey = lastKey,
                Limit = PageSize
            }, ct);

            // Guardrail: evaluated items (pre-filter)
            inspected += q.ScannedCount ?? 0;
            if (inspected > MaxWorkoutsToInspect)
            {
                return StatusCode(409, new
                {
                    error = "too_many_workouts_for_mvp_recent",
                    max = MaxWorkoutsToInspect
                });
            }

            foreach (var it in q.Items)
            {
                // Defensive: should already be filtered out
                if (TryBool(it, "isDeleted") == true)
                    continue;

                buffer.Add(new ActivityDto
                {
                    ActivityId = TryLong(it, "activityId"),
                    Name = TryString(it, "activityName"),
                    SportType = TryString(it, "sportType"),
                    StartDateUtc = TryLong(it, "startDateUtc"),
                    DistanceMeters = TryLong(it, "distanceMeters"),
                    TotalElevationGain = TryLong(it, "total_elevation_gain"),
                    IsDeleted = false
                });
            }

            lastKey = q.LastEvaluatedKey;

            // MVP early stop: enough candidates for sort
            if (buffer.Count >= limit * 5)
                break;

        } while (lastKey != null && lastKey.Count > 0);

        var ordered = buffer
            .Where(x => x.ActivityId.HasValue && x.StartDateUtc.HasValue)
            .OrderByDescending(x => x.StartDateUtc!.Value)
            .Take(limit)
            .Select(x => new
            {
                activityId = x.ActivityId!.Value,
                name = x.Name ?? string.Empty,
                sportType = x.SportType ?? string.Empty,
                startDateUtc = x.StartDateUtc!.Value,
                distanceMeters = x.DistanceMeters ?? 0,
                total_elevation_gain = x.TotalElevationGain ?? 0
            })
            .ToList();

        return Ok(new
        {
            items = ordered,
            nextToken = (string?)null // MVP: not implementing client pagination yet
        });
    }

    private static string? TryString(Dictionary<string, AttributeValue> item, string key)
    {
        if (!item.TryGetValue(key, out var av) || av is null) return null;
        if (av.NULL == true) return null;
        return av.S ?? av.N;
    }

    private static long? TryLong(Dictionary<string, AttributeValue> item, string key)
    {
        if (!item.TryGetValue(key, out var av) || av is null) return null;
        if (av.NULL == true) return null;

        if (!string.IsNullOrWhiteSpace(av.N))
        {
            return long.TryParse(av.N, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n
                : null;
        }

        if (!string.IsNullOrWhiteSpace(av.S))
        {
            return long.TryParse(av.S, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
                ? s
                : null;
        }

        return null;
    }

    private static bool? TryBool(Dictionary<string, AttributeValue> item, string key)
    {
        if (!item.TryGetValue(key, out var av) || av is null) return null;
        if (av.NULL == true) return null;
        if (av.BOOL.HasValue) return av.BOOL.Value;

        if (!string.IsNullOrWhiteSpace(av.S) && bool.TryParse(av.S, out var b)) return b;
        return null;
    }

    private sealed class ActivityDto
    {
        public long? ActivityId { get; set; }
        public string? Name { get; set; }
        public string? SportType { get; set; }
        public long? StartDateUtc { get; set; }
        public long? DistanceMeters { get; set; }
        public long? TotalElevationGain { get; set; }
        public bool IsDeleted { get; set; }
    }
}