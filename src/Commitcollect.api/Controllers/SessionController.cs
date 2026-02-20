using Commitcollect.api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Commitcollect.api.Controllers;

[ApiController]
[Route("session")]
public sealed class SessionController : ControllerBase
{
    private readonly ISessionResolver _sessions;

    public SessionController(ISessionResolver sessions)
    {
        _sessions = sessions;
    }

    // GET /session
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var session = await _sessions.ResolveAsync(HttpContext);

        if (session is null)
            return Unauthorized();

        return Ok(new
        {
            userId = session.UserId,
            email = session.Email
        });
    }
}
