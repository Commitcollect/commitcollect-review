namespace Commitcollect.api.Services;

public sealed class SessionRecord
{
    public required string SessionId { get; init; }
    public required string UserId { get; init; }
    public required string Email { get; init; }
}
