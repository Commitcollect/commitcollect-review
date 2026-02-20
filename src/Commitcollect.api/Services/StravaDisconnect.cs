using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Commitcollect.api.Services;

public static class StravaDisconnect
{
    public sealed record Conn(long AthleteId, string AccessToken);

    // Outcome for audit mapping:
    // NoConnection  -> audit result: attempt
    // Success       -> audit result: success
    // Partial       -> audit result: partial (remote failed, local cleanup attempted)
    public enum Outcome
    {
        NoConnection,
        Success,
        Partial
    }

    public sealed record DisconnectResult(Outcome Outcome, long AthleteId);

    public static async Task<Conn?> LoadAsync(
        IAmazonDynamoDB ddb,
        string table,
        string userId,
        CancellationToken ct)
    {
        var resp = await ddb.GetItemAsync(new GetItemRequest
        {
            TableName = table,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = $"USER#{userId}" },
                ["SK"] = new AttributeValue { S = "STRAVA#CONNECTION" }
            },
            ConsistentRead = true
        }, ct);

        if (resp.Item is null || resp.Item.Count == 0) return null;

        var athleteId =
            resp.Item.TryGetValue("athleteId", out var aid) && !string.IsNullOrWhiteSpace(aid.N)
                ? long.Parse(aid.N)
                : 0;

        var accessToken =
            resp.Item.TryGetValue("accessToken", out var at)
                ? (at.S ?? "")
                : "";

        // If athleteId is missing we can't safely clean up ownership record.
        if (athleteId <= 0) return null;

        // Token may be empty in some edge cases; still allow Dynamo cleanup.
        return new Conn(athleteId, accessToken);
    }

    /// <summary>
    /// Best-effort Strava deauthorize.
    /// Returns true if request was sent and Strava responded with success (2xx),
    /// or if accessToken is empty (nothing to revoke remotely).
    /// Never throws.
    /// </summary>
    private static async Task<bool> TryDeauthorizeAsync(
        IHttpClientFactory httpClientFactory,
        string accessToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return true; // nothing to revoke remotely

        try
        {
            var http = httpClientFactory.CreateClient();

            // Strava docs accept access_token parameter; this matches the documented shape.
            var url = "https://www.strava.com/oauth/deauthorize?access_token=" + Uri.EscapeDataString(accessToken);

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            using var resp = await http.SendAsync(req, ct);

            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static async Task BestEffortDeleteConnectionAndOwnershipAsync(
        IAmazonDynamoDB ddb,
        string table,
        string userId,
        long athleteId,
        CancellationToken ct)
    {
        try
        {
            await ddb.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems = new List<TransactWriteItem>
                {
                    new()
                    {
                        Delete = new Delete
                        {
                            TableName = table,
                            Key = new Dictionary<string, AttributeValue>
                            {
                                ["PK"] = new AttributeValue { S = $"USER#{userId}" },
                                ["SK"] = new AttributeValue { S = "STRAVA#CONNECTION" }
                            }
                        }
                    },
                    new()
                    {
                        Delete = new Delete
                        {
                            TableName = table,
                            Key = new Dictionary<string, AttributeValue>
                            {
                                ["PK"] = new AttributeValue { S = $"STRAVA#ATHLETE#{athleteId}" },
                                ["SK"] = new AttributeValue { S = "OWNER" }
                            },
                            ConditionExpression = "attribute_exists(PK) AND userId = :uid",
                            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                            {
                                [":uid"] = new AttributeValue { S = userId }
                            }
                        }
                    }
                }
            }, ct);
        }
        catch
        {
            // idempotent: if already deleted or ownership not ours, treat as disconnected
        }
    }

    /// <summary>
    /// Best-effort disconnect that returns an outcome suitable for STRAVA_DEAUTHORIZE audit.
    /// - Never throws
    /// - Always attempts local cleanup even if remote deauth fails
    /// </summary>
    public static async Task<DisconnectResult> BestEffortDisconnectAsync(
        IHttpClientFactory httpClientFactory,
        IAmazonDynamoDB ddb,
        string table,
        string userId,
        CancellationToken ct)
    {
        var conn = await LoadAsync(ddb, table, userId, ct);
        if (conn is null)
            return new DisconnectResult(Outcome.NoConnection, 0);

        // Do NOT block Dynamo cleanup if Strava call fails.
        var remoteOk = await TryDeauthorizeAsync(httpClientFactory, conn.AccessToken, ct);

        await BestEffortDeleteConnectionAndOwnershipAsync(ddb, table, userId, conn.AthleteId, ct);

        return remoteOk
            ? new DisconnectResult(Outcome.Success, conn.AthleteId)
            : new DisconnectResult(Outcome.Partial, conn.AthleteId);
    }
}
