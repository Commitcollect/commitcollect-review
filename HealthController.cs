using Microsoft.AspNetCore.Mvc;
using Amazon.CognitoIdentityProvider;


namespace CommitCollect.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "healthy",
            service = "commitcollect-api",
            utc = DateTime.UtcNow
        });
    }

    [HttpGet("debug/di-cognito")]
    public IActionResult DebugDiCognito([FromServices] IAmazonCognitoIdentityProvider cognito)
    => Ok(new { ok = true });

}


