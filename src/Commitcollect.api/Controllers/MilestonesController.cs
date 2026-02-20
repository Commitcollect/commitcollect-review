using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Commitcollect.api.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("milestones")]
public sealed class MilestonesController : ControllerBase
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly IConfiguration _config;
    private readonly ISessionResolver _sessions;

    private const int MaxWorkoutsToInspect = 3000;
    private const int WorkoutsPageSize = 200;

    public MilestonesController(IAmazonDynamoDB ddb, IConfiguration config, ISessionResolver sessions)
    {
        _ddb = ddb;
        _config = config;
        _sessions = sessions;
    }


    public sealed class CreateMilestoneRequest
    {
        public string? ModelId { get; set; }
        public string? Sport { get; set; }
        public string? TargetType { get; set; }
        public long? TotalTarget { get; set; }
        public long? PeriodStartAtUtc { get; set; }
        public int? PartsTotal { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMilestoneRequest req, CancellationToken ct)
    {
        var session = await _sessions.ResolveAsync(HttpContext);
        if (session is null)
            return Unauthorized(new { status = "invalid_session" });

        var table = _config["DynamoDb:StravaTokensTable"] ?? _config["DynamoDb__StravaTokensTable"];
        if (string.IsNullOrWhiteSpace(table))
            return Problem("Missing DynamoDb:StravaTokensTable");

        if (req is null)
            return BadRequest(new { error = "invalid_body" });

        var modelId = (req.ModelId ?? string.Empty).Trim();
        var sport = (req.Sport ?? string.Empty).Trim();
        var targetType = (req.TargetType ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(modelId)) return BadRequest(new { error = "modelId_required" });
        if (string.IsNullOrWhiteSpace(sport)) return BadRequest(new { error = "sport_required" });
        if (string.IsNullOrWhiteSpace(targetType)) return BadRequest(new { error = "targetType_required" });

        var totalTarget = req.TotalTarget.GetValueOrDefault();
        if (totalTarget <= 0) return BadRequest(new { error = "totalTarget_required" });

        var periodStartAtUtc = req.PeriodStartAtUtc.GetValueOrDefault();
        if (periodStartAtUtc <= 0) return BadRequest(new { error = "periodStartAtUtc_required" });

        if (!(sport is "RIDE" or "RUN" or "SWIM"))
            return BadRequest(new { error = "unsupported_sport" });

        if (!(targetType is "DISTANCE_METERS" or "ELEVATION_METERS"))
            return BadRequest(new { error = "unsupported_targetType" });

        // Validate model META (MODEL#{modelId} / META)
        var modelMeta = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = table,
            Key = new Dictionary<string, Amazon.DynamoDBv2.Model.AttributeValue>
            {
                ["PK"] = new Amazon.DynamoDBv2.Model.AttributeValue { S = $"MODEL#{modelId}" },
                ["SK"] = new Amazon.DynamoDBv2.Model.AttributeValue { S = "META" }
            },
            ConsistentRead = true
        }, ct);

        if (modelMeta.Item is null || modelMeta.Item.Count == 0)
            return NotFound(new { error = "model_not_found" });

        var isActive = (modelMeta.Item.TryGetValue("isActive", out var act) && act.BOOL == true);
        if (!isActive)
            return BadRequest(new { error = "model_not_active" });

        var modelPartsTotal = TryInt(modelMeta.Item, "partsTotal") ?? 0;
        if (modelPartsTotal <= 0)
            return Problem("Model META missing partsTotal");

        var partsTotal = req.PartsTotal.GetValueOrDefault(modelPartsTotal);

        if (partsTotal != modelPartsTotal)
            return BadRequest(new { error = "partsTotal_must_match_model" });

        if (partsTotal != 12)
            return BadRequest(new { error = "partsTotal_must_be_12_for_mvp" });

        var partTarget = CeilDiv(totalTarget, partsTotal);

        var userPk = $"USER#{session.UserId}";
        var milestoneId = NewUlid();
        var milestoneSk = $"MILESTONE#{milestoneId}";
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var item = new Dictionary<string, Amazon.DynamoDBv2.Model.AttributeValue>
        {
            ["PK"] = new Amazon.DynamoDBv2.Model.AttributeValue { S = userPk },
            ["SK"] = new Amazon.DynamoDBv2.Model.AttributeValue { S = milestoneSk },

            ["entityType"] = new Amazon.DynamoDBv2.Model.AttributeValue { S = "Milestone" },
            ["milestoneId"] = new Amazon.DynamoDBv2.Model.AttributeValue { S = milestoneId },

            ["modelId"] = new Amazon.DynamoDBv2.Model.AttributeValue { S = modelId },

            ["sport"] = new Amazon.DynamoDBv2.Model.AttributeValue { S = sport },
            ["targetType"] = new Amazon.DynamoDBv2.Model.AttributeValue { S = targetType },

            ["totalTarget"] = new Amazon.DynamoDBv2.Model.AttributeValue { N = totalTarget.ToString(CultureInfo.InvariantCulture) },
            ["period"] = new Amazon.DynamoDBv2.Model.AttributeValue { S = "YEAR" },
            ["periodStartAtUtc"] = new Amazon.DynamoDBv2.Model.AttributeValue { N = periodStartAtUtc.ToString(CultureInfo.InvariantCulture) },

            ["partsTotal"] = new Amazon.DynamoDBv2.Model.AttributeValue { N = partsTotal.ToString(CultureInfo.InvariantCulture) },
            ["partTarget"] = new Amazon.DynamoDBv2.Model.AttributeValue { N = partTarget.ToString(CultureInfo.InvariantCulture) },

            ["status"] = new Amazon.DynamoDBv2.Model.AttributeValue { S = "ACTIVE" },

            ["progressValue"] = new Amazon.DynamoDBv2.Model.AttributeValue { N = "0" },
            ["progressUpdatedAtUtc"] = new Amazon.DynamoDBv2.Model.AttributeValue { N = nowUtc.ToString(CultureInfo.InvariantCulture) },

            ["partsAwardedCount"] = new Amazon.DynamoDBv2.Model.AttributeValue { N = "0" },
            ["lastAwardedAtUtc"] = new Amazon.DynamoDBv2.Model.AttributeValue { NULL = true },

            ["completedAtUtc"] = new Amazon.DynamoDBv2.Model.AttributeValue { NULL = true },

            ["createdAtUtc"] = new Amazon.DynamoDBv2.Model.AttributeValue { N = nowUtc.ToString(CultureInfo.InvariantCulture) },
            ["updatedAtUtc"] = new Amazon.DynamoDBv2.Model.AttributeValue { N = nowUtc.ToString(CultureInfo.InvariantCulture) },

            ["version"] = new Amazon.DynamoDBv2.Model.AttributeValue { N = "1" }
        };

        try
        {
            await _ddb.PutItemAsync(new PutItemRequest
            {
                TableName = table,
                Item = item,
                ConditionExpression = "attribute_not_exists(PK) AND attribute_not_exists(SK)"
            }, ct);
        }
        catch (ConditionalCheckFailedException)
        {
            return Conflict(new { error = "milestone_id_collision" });
        }

        return Ok(new { milestoneId, status = "ACTIVE" });
    }

    // --- helpers required by Create ---

    private static long CeilDiv(long numerator, int denominator)
        => denominator <= 0 ? 0 : (numerator + denominator - 1L) / denominator;

    private static int? TryInt(Dictionary<string, Amazon.DynamoDBv2.Model.AttributeValue> item, string key)
    {
        if (!item.TryGetValue(key, out var av)) return null;
        if (!string.IsNullOrWhiteSpace(av.N) && int.TryParse(av.N, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return n;
        return null;
    }

    // Minimal ULID generator (no new deps)
    private static string NewUlid()
    {
        var timeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Span<byte> bytes = stackalloc byte[16];

        bytes[0] = (byte)((timeMs >> 40) & 0xFF);
        bytes[1] = (byte)((timeMs >> 32) & 0xFF);
        bytes[2] = (byte)((timeMs >> 24) & 0xFF);
        bytes[3] = (byte)((timeMs >> 16) & 0xFF);
        bytes[4] = (byte)((timeMs >> 8) & 0xFF);
        bytes[5] = (byte)(timeMs & 0xFF);

        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes[6..]);
        return CrockfordBase32Encode(bytes);
    }

    private static string CrockfordBase32Encode(ReadOnlySpan<byte> data)
    {
        const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        Span<char> chars = stackalloc char[26];

        int bitBuffer = 0, bitCount = 0, idx = 0;

        foreach (var b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;

            while (bitCount >= 5 && idx < 26)
            {
                bitCount -= 5;
                var c = (bitBuffer >> bitCount) & 31;
                chars[idx++] = Alphabet[c];
            }
        }

        while (idx < 26) chars[idx++] = '0';
        return new string(chars);
    }

    // GET /milestones/{id}
    [HttpGet("{milestoneId}")]
    public async Task<IActionResult> GetById(string milestoneId, CancellationToken ct)
    {
        var session = await _sessions.ResolveAsync(HttpContext);
        if (session is null)
            return Unauthorized(new { status = "invalid_session" });

        var table = _config["DynamoDb:StravaTokensTable"] ?? _config["DynamoDb__StravaTokensTable"];
        if (string.IsNullOrWhiteSpace(table))
            return Problem("Missing DynamoDb:StravaTokensTable");

        var userId = session.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { status = "invalid_session" });

        var userPk = $"USER#{userId}";
        var skPrefix = $"MILESTONE#{milestoneId}";

        // One query: milestone item + its awards (max 13 items typically)
        Dictionary<string, AttributeValue>? lastKey = null;
        var inspected = 0;

        Dictionary<string, AttributeValue>? milestoneItem = null;
        var awards = new List<AwardVm>(capacity: 16);

        do
        {
            ct.ThrowIfCancellationRequested();

            var q = await _ddb.QueryAsync(new QueryRequest
            {
                TableName = table,
                KeyConditionExpression = "#pk = :pk AND begins_with(#sk, :skPrefix)",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#pk"] = "PK",
                    ["#sk"] = "SK",
                    ["#status"] = "status"
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue { S = userPk },
                    [":skPrefix"] = new AttributeValue { S = skPrefix }
                },

                // Keep it tight. ProjectionExpression can list fields that don't exist on some items.
                ProjectionExpression = string.Join(", ", new[]
{
                        "PK", "SK", "entityType",
                        "milestoneId", "modelId", "sport", "targetType",
                        "totalTarget", "partTarget", "partsTotal",
                        "periodStartAtUtc", "period",
                        "#status",
                        "progressValue", "progressUpdatedAtUtc",
                        "partsAwardedCount", "lastAwardedAtUtc",
                        "completedAtUtc",
                        "createdAtUtc", "updatedAtUtc",
                        "version",
                        "partIndex", "partName", "meshFile", "attachPoint",
                        "awardedAtUtc", "progressValueAtAward"
                    }),

                     ExclusiveStartKey = lastKey,
                Limit = 50 // defensive; should never need more than a page
            }, ct);

            inspected += q.ScannedCount ?? q.Items.Count;

            foreach (var it in q.Items)
            {
                var sk = GetString(it, "SK") ?? string.Empty;

                // Milestone root item is exactly SK == MILESTONE#{id}
                if (string.Equals(sk, skPrefix, StringComparison.Ordinal))
                {
                    milestoneItem = it;
                    continue;
                }

                // Awards are SK = MILESTONE#{id}#AWARD#{xx}
                if (sk.Contains("#AWARD#", StringComparison.Ordinal))
                {
                    awards.Add(new AwardVm
                    {
                        PartIndex = (int)(GetLong(it, "partIndex") ?? 0),
                        PartName = GetString(it, "partName"),
                        MeshFile = GetString(it, "meshFile"),
                        AttachPoint = GetString(it, "attachPoint"),
                        AwardedAtUtc = GetLong(it, "awardedAtUtc"),
                        ProgressValueAtAward = GetLong(it, "progressValueAtAward")
                    });
                }
            }

            lastKey = q.LastEvaluatedKey;

        } while (lastKey is { Count: > 0 });

        if (milestoneItem is null)
            return NotFound(new { error = "milestone_not_found" });

        // Build frontend-ready milestone VM
        var modelId = GetString(milestoneItem, "modelId") ?? string.Empty;
        var sport = GetString(milestoneItem, "sport") ?? string.Empty;
        var targetType = GetString(milestoneItem, "targetType") ?? string.Empty;

        var totalTarget = GetLong(milestoneItem, "totalTarget") ?? 0;
        var partTarget = GetLong(milestoneItem, "partTarget") ?? 0;
        var partsTotal = (int)(GetLong(milestoneItem, "partsTotal") ?? 12);

        var progressValue = GetLong(milestoneItem, "progressValue") ?? 0;
        var partsAwardedCount = (int)(GetLong(milestoneItem, "partsAwardedCount") ?? 0);

        var status = GetString(milestoneItem, "status") ?? "ACTIVE";
        var isComplete = string.Equals(status, "COMPLETED", StringComparison.Ordinal);

        // computed fields
        var percentComplete = totalTarget > 0
            ? Math.Round((progressValue / (double)totalTarget) * 100.0, 2)
            : 0.0;

        var partsEarned = (partTarget > 0)
            ? (int)Math.Min(partsTotal, progressValue / partTarget)
            : 0;

        var nextThreshold = (partTarget > 0 && partsEarned < partsTotal)
            ? (partsEarned + 1L) * partTarget
            : 0L;

        var remainingToNextPart = (partTarget > 0 && partsEarned < partsTotal)
            ? Math.Max(0L, nextThreshold - progressValue)
            : 0L;

        awards = awards
            .Where(a => a.PartIndex > 0)
            .OrderBy(a => a.PartIndex)
            .ToList();

        var milestoneVm = new MilestoneVm
        {
            MilestoneId = GetString(milestoneItem, "milestoneId") ?? milestoneId,
            ModelId = modelId,

            Sport = sport,
            TargetType = targetType,
            Status = status,

            TotalTarget = totalTarget,
            PartTarget = partTarget,
            PartsTotal = partsTotal,

            Period = GetString(milestoneItem, "period") ?? "YEAR",
            PeriodStartAtUtc = GetLong(milestoneItem, "periodStartAtUtc"),

            ProgressValue = progressValue,
            PercentComplete = percentComplete,

            PartsEarned = partsEarned,
            PartsAwardedCount = partsAwardedCount,
            RemainingToNextPart = remainingToNextPart,

            ProgressUpdatedAtUtc = GetLong(milestoneItem, "progressUpdatedAtUtc"),
            LastAwardedAtUtc = GetLong(milestoneItem, "lastAwardedAtUtc"),
            CompletedAtUtc = GetLong(milestoneItem, "completedAtUtc"),

            CreatedAtUtc = GetLong(milestoneItem, "createdAtUtc"),
            UpdatedAtUtc = GetLong(milestoneItem, "updatedAtUtc"),
            Version = GetLong(milestoneItem, "version") ?? 1
        };

        return Ok(new
        {
            milestone = milestoneVm,
            awards,
            debug = new
            {
                pk = userPk,
                skPrefix,
                inspected,
                awardsCount = awards.Count,
                hasMore = false // we paged until completion; kept for clarity
            }
        });
    }

    private sealed class MilestoneVm
    {
        public string MilestoneId { get; set; } = "";
        public string ModelId { get; set; } = "";

        public string Sport { get; set; } = "";
        public string TargetType { get; set; } = "";
        public string Status { get; set; } = "";

        public long TotalTarget { get; set; }
        public long PartTarget { get; set; }
        public int PartsTotal { get; set; }

        public string Period { get; set; } = "YEAR";
        public long? PeriodStartAtUtc { get; set; }

        public long ProgressValue { get; set; }
        public double PercentComplete { get; set; }

        public int PartsEarned { get; set; }
        public int PartsAwardedCount { get; set; }
        public long RemainingToNextPart { get; set; }

        public long? ProgressUpdatedAtUtc { get; set; }
        public long? LastAwardedAtUtc { get; set; }
        public long? CompletedAtUtc { get; set; }

        public long? CreatedAtUtc { get; set; }
        public long? UpdatedAtUtc { get; set; }
        public long Version { get; set; }
    }

    private sealed class AwardVm
    {
        public int PartIndex { get; set; }
        public string? PartName { get; set; }
        public string? MeshFile { get; set; }
        public string? AttachPoint { get; set; }
        public long? AwardedAtUtc { get; set; }
        public long? ProgressValueAtAward { get; set; }
    }


     // POST /milestones/{id}/recompute
    [HttpPost("{milestoneId}/recompute")]
    public async Task<IActionResult> Recompute(string milestoneId, CancellationToken ct)
    {
        var session = await _sessions.ResolveAsync(HttpContext);
        if (session is null)
            return Unauthorized(new { status = "invalid_session" });

        var table = _config["DynamoDb:StravaTokensTable"] ?? _config["DynamoDb__StravaTokensTable"];
        if (string.IsNullOrWhiteSpace(table))
            return Problem("Missing DynamoDb:StravaTokensTable");

        var userId = session.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { status = "invalid_session" });

        var userPk = $"USER#{userId}";
        var milestoneSk = $"MILESTONE#{milestoneId}";

        // ------------------------------------------------------------
        // 1) Load milestone (authoritative config for recompute)
        // ------------------------------------------------------------
        var milestoneResp = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = table,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = userPk },
                ["SK"] = new AttributeValue { S = milestoneSk }
            },
            ConsistentRead = true
        }, ct);




        if (milestoneResp.Item is null || milestoneResp.Item.Count == 0)
            return NotFound(new { error = "milestone_not_found" });

        var m = milestoneResp.Item;

        var modelId = GetString(m, "modelId") ?? string.Empty;
        var sport = NormSport(GetString(m, "sport"));
        var targetType = GetString(m, "targetType") ?? string.Empty;

        var totalTarget = GetLong(m, "totalTarget") ?? 0;
        var periodStartAtUtc = GetLong(m, "periodStartAtUtc") ?? 0;

        var partsTotal = (int)(GetLong(m, "partsTotal") ?? 12);
        var partTarget = GetLong(m, "partTarget") ?? 0;

        var existingStatus = GetString(m, "status") ?? "ACTIVE";
        var existingPartsAwarded = (int)(GetLong(m, "partsAwardedCount") ?? 0);
        var completedAtUtc = GetLong(m, "completedAtUtc"); // may be null
        var version = GetLong(m, "version") ?? 1;

        if (string.IsNullOrWhiteSpace(modelId))
            return Problem("Milestone missing modelId");

        // ------------------------------------------------------------
        // 2) Read workouts (PK query + pagination) and compute progress
        // ------------------------------------------------------------
        long progressValue = 0;

        int inspected = 0;
        int excludedBeforeStartCount = 0;

        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            ct.ThrowIfCancellationRequested();

            var q = await _ddb.QueryAsync(new QueryRequest
            {
                TableName = table,
                KeyConditionExpression = "#pk = :pk AND begins_with(#sk, :skPrefix)",
                ProjectionExpression = string.Join(", ", new[]
                {
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
                    ["#sportType"] = "sportType",
                    ["#startDateUtc"] = "startDateUtc",
                    ["#distanceMeters"] = "distanceMeters",
                    ["#elevGain"] = "total_elevation_gain",
                    ["#isDeleted"] = "isDeleted"
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue { S = userPk },
                    [":skPrefix"] = new AttributeValue { S = "WORKOUT#STRAVA#" }
                },
                ExclusiveStartKey = lastKey,
                Limit = WorkoutsPageSize
            }, ct);

            inspected += q.ScannedCount ?? 0;
            if (inspected > MaxWorkoutsToInspect)
            {
                return StatusCode(409, new
                {
                    error = "too_many_workouts_for_mvp_recompute",
                    max = MaxWorkoutsToInspect
                });
            }

            foreach (var w in q.Items)
            {
                // Deleted workouts do not contribute
                if (GetBool(w, "isDeleted") == true)
                    continue;

                var wSport = NormSport(GetString(w, "sportType"));
                if (wSport != sport)
                    continue;

                var startUtc = GetLong(w, "startDateUtc") ?? 0;
                if (startUtc < periodStartAtUtc)
                {
                    excludedBeforeStartCount++;
                    continue;
                }

                if (string.Equals(targetType, "DISTANCE_METERS", StringComparison.Ordinal))
                {
                    progressValue += GetLong(w, "distanceMeters") ?? 0;
                }
                else if (string.Equals(targetType, "ELEVATION_METERS", StringComparison.Ordinal))
                {
                    progressValue += GetLong(w, "total_elevation_gain") ?? 0;
                }
                else
                {
                    return BadRequest(new { error = "unsupported_targetType" });
                }
            }

            lastKey = q.LastEvaluatedKey;

        } while (lastKey is { Count: > 0 });

        // ------------------------------------------------------------
        // 3) Compute milestone results (monotonic awards + completion)
        // ------------------------------------------------------------

        // Earned parts based on current progress (can go down due to deletions)
        var partsEarned = (partTarget > 0)
            ? (int)Math.Min(partsTotal, progressValue / partTarget)
            : 0;

        // Awards are monotonic (never decrease)
        var newPartsAwardedCount = Math.Max(existingPartsAwarded, partsEarned);
        var partsDelta = Math.Max(0, newPartsAwardedCount - existingPartsAwarded);

        // Completion is monotonic (never revert)
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        string newStatus;
        long? newCompletedAtUtc = completedAtUtc;

        if (string.Equals(existingStatus, "COMPLETED", StringComparison.Ordinal))
        {
            newStatus = "COMPLETED";
        }
        else if (progressValue >= totalTarget && totalTarget > 0)
        {
            newStatus = "COMPLETED";
            newCompletedAtUtc ??= nowUtc;
        }
        else
        {
            newStatus = "ACTIVE";
        }

        var isComplete = string.Equals(newStatus, "COMPLETED", StringComparison.Ordinal);

        // ------------------------------------------------------------
        // 4) Build transactional writes: update milestone + mint awards
        // ------------------------------------------------------------

        var tx = new List<TransactWriteItem>(capacity: 1 + partsDelta);

        // Milestone update (optimistic concurrency)
        var updateExpr = "SET " +
                         "progressValue = :pv, " +
                         "progressUpdatedAtUtc = :now, " +
                         "partsAwardedCount = :pac, " +
                         "updatedAtUtc = :now, " +
                         "#status = :st, " +
                         "version = :newVersion";

        var exprNames = new Dictionary<string, string> { ["#status"] = "status" };
        var exprValues = new Dictionary<string, AttributeValue>
        {
            [":pv"] = new AttributeValue { N = progressValue.ToString(CultureInfo.InvariantCulture) },
            [":pac"] = new AttributeValue { N = newPartsAwardedCount.ToString(CultureInfo.InvariantCulture) },
            [":st"] = new AttributeValue { S = newStatus },
            [":now"] = new AttributeValue { N = nowUtc.ToString(CultureInfo.InvariantCulture) },
            [":expectedVersion"] = new AttributeValue { N = version.ToString(CultureInfo.InvariantCulture) },
            [":newVersion"] = new AttributeValue { N = (version + 1).ToString(CultureInfo.InvariantCulture) }
        };

        if (newCompletedAtUtc.HasValue)
        {
            updateExpr += ", completedAtUtc = :completedAtUtc";
            exprValues[":completedAtUtc"] = new AttributeValue { N = newCompletedAtUtc.Value.ToString(CultureInfo.InvariantCulture) };
        }

        // Optional: set lastAwardedAtUtc only when delta > 0
        if (partsDelta > 0)
        {
            updateExpr += ", lastAwardedAtUtc = :lastAwardedAtUtc";
            exprValues[":lastAwardedAtUtc"] = new AttributeValue { N = nowUtc.ToString(CultureInfo.InvariantCulture) };
        }

        tx.Add(new TransactWriteItem
        {
            Update = new Update
            {
                TableName = table,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new AttributeValue { S = userPk },
                    ["SK"] = new AttributeValue { S = milestoneSk }
                },
                ConditionExpression = "version = :expectedVersion",
                UpdateExpression = updateExpr,
                ExpressionAttributeNames = exprNames,
                ExpressionAttributeValues = exprValues
            }
        });

        // Mint new award items (idempotent)
        // For each newly earned partIndex, copy Model Part metadata into the award row.
        for (var partIndex = existingPartsAwarded + 1; partIndex <= newPartsAwardedCount; partIndex++)
        {
            var partSk = $"PART#{partIndex:D2}";
            var partResp = await _ddb.GetItemAsync(new GetItemRequest
            {
                TableName = table,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new AttributeValue { S = $"MODEL#{modelId}" },
                    ["SK"] = new AttributeValue { S = partSk }
                },
                ConsistentRead = true
            }, ct);

            // If admin config is missing, fail hard: better than minting incomplete awards
            if (partResp.Item is null || partResp.Item.Count == 0)
                return Problem($"Model part missing: MODEL#{modelId} / {partSk}");

            var p = partResp.Item;

            var partName = GetString(p, "partName") ?? string.Empty;
            var meshFile = GetString(p, "meshFile") ?? string.Empty;
            var attachPoint = GetString(p, "attachPoint") ?? string.Empty;

            var awardSk = $"MILESTONE#{milestoneId}#AWARD#{partIndex:D2}";

            tx.Add(new TransactWriteItem
            {
                Put = new Put
                {
                    TableName = table,
                    ConditionExpression = "attribute_not_exists(PK) AND attribute_not_exists(SK)",
                    Item = new Dictionary<string, AttributeValue>
                    {
                        ["PK"] = new AttributeValue { S = userPk },
                        ["SK"] = new AttributeValue { S = awardSk },

                        ["entityType"] = new AttributeValue { S = "MilestoneAward" },
                        ["milestoneId"] = new AttributeValue { S = milestoneId },
                        ["modelId"] = new AttributeValue { S = modelId },

                        ["partIndex"] = new AttributeValue { N = partIndex.ToString(CultureInfo.InvariantCulture) },

                        ["partName"] = new AttributeValue { S = partName },
                        ["meshFile"] = new AttributeValue { S = meshFile },
                        ["attachPoint"] = new AttributeValue { S = attachPoint },

                        ["awardedAtUtc"] = new AttributeValue { N = nowUtc.ToString(CultureInfo.InvariantCulture) },
                        ["progressValueAtAward"] = new AttributeValue { N = progressValue.ToString(CultureInfo.InvariantCulture) }
                    }
                }
            });
        }

        try
        {
            await _ddb.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems = tx
            }, ct);
        }
        catch (TransactionCanceledException)
        {
            return Conflict(new { error = "milestone_version_conflict" });
        }

        // ---- response context (debug + frontend-friendly) ----
        var percentComplete = totalTarget > 0
            ? Math.Round((progressValue / (double)totalTarget) * 100.0, 2)
            : 0.0;

        // Next part threshold is based on partsEarned (not awarded), because it's about raw progress.
        var nextThreshold = (partsEarned < partsTotal)
            ? (partsEarned + 1L) * partTarget
            : partsTotal * partTarget;

        var remainingToNextPart = (partTarget > 0 && partsEarned < partsTotal)
            ? Math.Max(0L, nextThreshold - progressValue)
            : 0L;


        return Ok(new
        {
            milestoneId,
            modelId,

            // milestone context
            totalTarget,
            partTarget,
            partsTotal,
            percentComplete,
            remainingToNextPart,

            // computed progress
            progressValue,
            partsEarned,
            partsDelta,

            status = newStatus,
            isComplete,
            excludedBeforeStartCount
        });

    }
    // ---------------------------
    // Null-safe parsing helpers
    // ---------------------------


    private static string NormSport(string? s)
    => (s ?? string.Empty).Trim().ToUpperInvariant();

    private static string? GetString(Dictionary<string, AttributeValue> item, string key)
    {
        if (!item.TryGetValue(key, out var av) || av is null) return null;
        if (av.NULL == true) return null;
        return av.S ?? av.N;
    }

    private static long? GetLong(Dictionary<string, AttributeValue> item, string key)
    {
        if (!item.TryGetValue(key, out var av) || av is null) return null;
        if (av.NULL == true) return null;

        if (!string.IsNullOrWhiteSpace(av.N))
            return long.TryParse(av.N, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

        if (!string.IsNullOrWhiteSpace(av.S))
            return long.TryParse(av.S, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : null;

        return null;
    }

    private static bool? GetBool(Dictionary<string, AttributeValue> item, string key)
    {
        if (!item.TryGetValue(key, out var av) || av is null) return null;
        if (av.NULL == true) return null;
        if (av.BOOL.HasValue) return av.BOOL.Value;

        if (!string.IsNullOrWhiteSpace(av.S) && bool.TryParse(av.S, out var b)) return b;
        return null;
    }
}