using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("dashboard")]
public class DashboardController : ControllerBase
{
    /// <summary>
    /// Redirects to the static dashboard HTML page.
    /// </summary>
    [HttpGet]
    public IActionResult Index()
    {
        return Redirect("/dashboard.html");
    }
}
