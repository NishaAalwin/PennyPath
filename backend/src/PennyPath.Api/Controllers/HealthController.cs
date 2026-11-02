using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PennyPath.Infrastructure.Persistence;

namespace PennyPath.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var dbOk = await db.Database.CanConnectAsync();
        return Ok(new { status = dbOk ? "healthy" : "degraded", database = dbOk });
    }
}
