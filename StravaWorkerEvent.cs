using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Commitcollect.StravaWorker;

public record StravaWorkerEvent(
    [property: JsonPropertyName("object_type")] string ObjectType,
    [property: JsonPropertyName("aspect_type")] string AspectType,
    [property: JsonPropertyName("object_id")] long ObjectId,
    [property: JsonPropertyName("owner_id")] long OwnerId,
    [property: JsonPropertyName("event_time")] long EventTime = 0,
    [property: JsonPropertyName("subscription_id")] long SubscriptionId = 0,
    [property: JsonPropertyName("updates")] JsonObject? Updates = null
);
