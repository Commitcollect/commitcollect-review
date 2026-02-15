using Amazon.Lambda;
using Amazon.Lambda.Model;
using System;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Commitcollect.api.Controllers;

[ApiController]
[Route("webhooks/strava")]
public class StravaWebhookController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IAmazonLambda _lambda;

    public StravaWebhookController(IConfiguration config, IAmazonLambda lambda)
    {
        _config = config;
        _lambda = lambda;
    }

    // =========================================================
    // 1) Strava subscription verification handshake (GET)
    // =========================================================
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.verify_token")] string verifyToken,
        [FromQuery(Name = "hub.challenge")] string challenge)
    {
        var expected = _config["Strava:WebhookVerifyToken"];
        if (string.IsNullOrWhiteSpace(expected))
            return StatusCode(500, "Missing Strava:WebhookVerifyToken");

        if (!string.Equals(verifyToken, expected, StringComparison.Ordinal))
            return Unauthorized();

        return Ok(new Dictionary<string, string> { ["hub.challenge"] = challenge });
    }

    // =========================================================
    // 2) Strava webhook events (POST) - ENQUEUE ONLY
    // =========================================================
    [HttpPost]
    public async Task<IActionResult> Receive([FromBody] StravaWebhookEvent ev)
    {
        // Only handle activities for MVP
        if (!string.Equals(ev.ObjectType, "activity", StringComparison.OrdinalIgnoreCase))
            return Ok();

        // We support create/update/delete (skip others)
        var aspect = (ev.AspectType ?? "").ToLowerInvariant();
        if (aspect != "create" && aspect != "update" && aspect != "delete")
            return Ok();

        // Strava requires fast response. Enqueue and return OK immediately.
        // Keep payload minimal: worker does idempotency + lookup + fetch + store.
        var eventTime = ev.EventTime > 0 ? ev.EventTime : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var correlationId = $"strava:{ev.SubscriptionId}:{ev.OwnerId}:{ev.ObjectId}:{eventTime}";

        Console.WriteLine($"[StravaWebhook] correlation_id={correlationId} aspect={aspect} object_id={ev.ObjectId} owner_id={ev.OwnerId}");

        var payload = new
        {
            source = "STRAVA",
            correlation_id = correlationId,
            object_type = ev.ObjectType,
            object_id = ev.ObjectId,
            aspect_type = aspect,               // normalized
            owner_id = ev.OwnerId,
            event_time = eventTime,
            subscription_id = ev.SubscriptionId,
            updates = ev.Updates               // optional
        };

        await EnqueueToWorkerAsync(payload);
        return Ok();
    }

    // =========================================================
    // Worker enqueue (Async invoke Lambda)
    // =========================================================
    private async Task EnqueueToWorkerAsync(object payload)
    {
        // Strava__WorkerFunctionName maps to config key: "Strava:WorkerFunctionName"
        var workerName = _config["Strava:WorkerFunctionName"];

        if (string.IsNullOrWhiteSpace(workerName))
            throw new InvalidOperationException(
                "Missing Strava:WorkerFunctionName configuration. " +
                "Set env var Strava__WorkerFunctionName.");

        var json = JsonSerializer.Serialize(payload);

        await _lambda.InvokeAsync(new InvokeRequest
        {
            FunctionName = workerName,
            InvocationType = InvocationType.Event, // fire-and-forget (async)
            Payload = json
        });
    }

    // =========================================================
    // Models
    // =========================================================
    public record StravaWebhookEvent(
        [property: JsonPropertyName("object_type")] string ObjectType,
        [property: JsonPropertyName("aspect_type")] string AspectType,
        [property: JsonPropertyName("object_id")] long ObjectId,
        [property: JsonPropertyName("owner_id")] long OwnerId,
        [property: JsonPropertyName("event_time")] long EventTime = 0,
        [property: JsonPropertyName("subscription_id")] long SubscriptionId = 0,
        [property: JsonPropertyName("updates")] JsonObject? Updates = null
    );
}
