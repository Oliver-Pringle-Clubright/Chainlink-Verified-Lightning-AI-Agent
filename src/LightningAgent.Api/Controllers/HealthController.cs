using LightningAgent.Data;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public HealthController(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [HttpGet]
    public IActionResult GetHealth()
    {
        string dbStatus;
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            dbStatus = "connected";
        }
        catch
        {
            dbStatus = "disconnected";
        }

        return Ok(new
        {
            status = "healthy",
            database = dbStatus,
            timestamp = DateTime.UtcNow
        });
    }
}
