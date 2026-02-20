using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Http;

namespace Commitcollect.api.Services;

public sealed class SessionResolver : ISessionResolver
{
    private readonly IConfiguration _config;
    private readonly IAmazonDynamoDB _ddb;

    public SessionResolver(IAmazonDynamoDB ddb, IConfiguration config)
    {
        _ddb = ddb;
        _config = config;
    }


    public async Task<SessionRecord?> ResolveAsync(HttpContext httpContext)
    {
        if (!httpContext.Request.Cookies.TryGetValue("cc_session", out var sessionId))
            return null;


        var sessionsTable = _config["DynamoDb:SessionsTable"] ?? _config["DynamoDb__SessionsTable"];
        if (string.IsNullOrWhiteSpace(sessionsTable))
            return null;

        var response = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = sessionsTable,
            Key = new Dictionary<string, AttributeValue>
    {
        { "PK", new AttributeValue { S = $"SESSION#{sessionId}" } },
        { "SK", new AttributeValue { S = "META" } }
    },
            ConsistentRead = true
        });

        if (response.Item == null || response.Item.Count == 0)
            return null;


        {
            if (response.Item == null || response.Item.Count == 0)
                return null;

            if (!response.Item.TryGetValue("userId", out var uid) ||
                string.IsNullOrWhiteSpace(uid.S))
            {
                return null;
            }

            response.Item.TryGetValue("email", out var em);

            return new SessionRecord
            {
                SessionId = sessionId,
                UserId = uid.S,
                Email = em?.S
            };

        }
    }
}
