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


}


