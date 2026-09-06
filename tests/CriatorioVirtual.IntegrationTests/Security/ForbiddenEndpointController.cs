using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.IntegrationTests.Security;

[ApiController]
[Route("api/test/security")]
public sealed class ForbiddenEndpointController : ControllerBase
{
    [HttpGet("forbidden")]
    [Authorize(Policy = "TestForbidden")]
    public IActionResult Get() => Ok();
}
