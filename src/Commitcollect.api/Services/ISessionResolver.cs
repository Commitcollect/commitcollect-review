using Microsoft.AspNetCore.Http;

namespace Commitcollect.api.Services;

public interface ISessionResolver
{
    Task<SessionRecord?> ResolveAsync(HttpContext httpContext);
}