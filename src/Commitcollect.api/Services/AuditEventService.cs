using System.Security.Cryptography;
using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;


namespace Commitcollect.api.Services;

public sealed class AuditEventService
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly IConfiguration _config;

    private const int TTL_DAYS = 90;

    public AuditEventService(IAmazonDynamoDB ddb, IConfiguration config)
    {
        _ddb = ddb;
        _config = config;
    }

    public async Task TryWriteAsync(
    HttpContext httpContext,
    string userId,
    string eventType,
    string result,
    Dictionary<string, AttributeValue>? data = null,
    Dictionary<string, AttributeValue>? metrics = null,
    string? correlationId = null,
    CancellationToken ct = default)
    {
        try 
        {
            await WriteAsync(httpContext, userId, eventType, result, data, metrics, correlationId, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AUDIT_WRITE_FAILED eventType={eventType} userId={userId} err={ex.GetType().Name} msg={ex.Message}");
        }
    }
    


    public async Task WriteAsync(
    HttpContext httpContext,
    string userId,
    string eventType,
    string result,
    Dictionary<string, AttributeValue>? data = null,
    Dictionary<string, AttributeValue>? metrics = null,
    string? correlationId = null,
    CancellationToken ct = default)


    {
      
           
        var table = _config["DynamoDb:AuditTable"] ?? "CommitCollectAudit";

        Console.WriteLine($"AUDIT_TABLE_RESOLVED={table}");


        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expires = DateTimeOffset.UtcNow.AddDays(TTL_DAYS).ToUnixTimeSeconds();

        var requestId =
            httpContext.TraceIdentifier ??
            Guid.NewGuid().ToString("N");

        // correlationId: if caller passes one, use it; else fall back to requestId
        correlationId ??= requestId;

        var origin = httpContext.Request.Headers["Origin"].FirstOrDefault() ?? "";
        var userAgent = httpContext.Request.Headers["User-Agent"].FirstOrDefault() ?? "";
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "";

        var yyyyMM = DateTimeOffset.UtcNow.ToString("yyyyMM");

        var pk = $"USER#{userId}";
        var sk = $"AUDIT#{now}#{requestId}";

        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = pk },
            ["SK"] = new AttributeValue { S = sk },

            // --- NEW: GSI keys ---
            ["GSI1PK"] = new AttributeValue { S = $"CORR#{correlationId}" },
            ["GSI1SK"] = new AttributeValue { S = sk },

            ["GSI2PK"] = new AttributeValue { S = $"EVENT#{eventType}#{yyyyMM}" },
            ["GSI2SK"] = new AttributeValue { S = sk },

            ["entityType"] = new AttributeValue { S = "AuditEvent" },
            ["eventType"] = new AttributeValue { S = eventType },
            ["eventVersion"] = new AttributeValue { N = "1" },
            ["occurredAtUtc"] = new AttributeValue { N = now.ToString() },

            ["requestId"] = new AttributeValue { S = requestId },
            ["correlationId"] = new AttributeValue { S = correlationId },

            ["actorType"] = new AttributeValue { S = "user" },
            ["actorUserId"] = new AttributeValue { S = userId },
            ["result"] = new AttributeValue { S = result },

            ["http"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["method"] = new AttributeValue { S = httpContext.Request.Method },
                    ["path"] = new AttributeValue { S = httpContext.Request.Path },
                }
            },

            ["client"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["origin"] = new AttributeValue { S = origin },
                    ["ipHash"] = new AttributeValue { S = Hash(ip) },
                    ["userAgent"] = new AttributeValue { S = Trim(userAgent, 512) }
                }
            },

            ["ExpiresAt"] = new AttributeValue { N = expires.ToString() }
        };

        if (data is not null)
            item["data"] = new AttributeValue { M = data };

        if (metrics is not null)
            item["metrics"] = new AttributeValue { M = metrics };

        await _ddb.PutItemAsync(new PutItemRequest
        {
            TableName = table,
            Item = item
        }, ct);
    }

    private static string Hash(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private static string Trim(string value, int max)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Length <= max ? value : value.Substring(0, max);


}