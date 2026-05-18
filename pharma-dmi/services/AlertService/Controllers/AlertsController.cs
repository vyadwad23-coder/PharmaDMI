using AlertService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlertService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlertsController : ControllerBase
{
    private readonly AlertDbContext _db;
    public AlertsController(AlertDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool unacknowledgedOnly = false)
    {
        var query = _db.Alerts.AsQueryable();
        if (unacknowledgedOnly) query = query.Where(a => !a.IsAcknowledged);
        var alerts = await query.OrderByDescending(a => a.CreatedAt).Take(100).ToListAsync();
        return Ok(alerts);
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var alerts = await _db.Alerts
            .Where(a => !a.IsAcknowledged)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
        return Ok(alerts);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var total = await _db.Alerts.CountAsync();
        var active = await _db.Alerts.CountAsync(a => !a.IsAcknowledged);
        var critical = await _db.Alerts.CountAsync(a => !a.IsAcknowledged && a.Severity == "Critical");
        var warning = await _db.Alerts.CountAsync(a => !a.IsAcknowledged && a.Severity == "Warning");
        var today = await _db.Alerts.CountAsync(a => a.CreatedAt >= DateTime.UtcNow.Date);

        return Ok(new { Total = total, Active = active, Critical = critical, Warning = warning, Today = today });
    }

    [HttpGet("{machineId}")]
    public async Task<IActionResult> GetByMachine(string machineId)
    {
        var alerts = await _db.Alerts
            .Where(a => a.MachineId == machineId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(20)
            .ToListAsync();
        return Ok(alerts);
    }

    [HttpPost("{id}/acknowledge")]
    public async Task<IActionResult> Acknowledge(int id)
    {
        var alert = await _db.Alerts.FindAsync(id);
        if (alert is null) return NotFound();
        alert.IsAcknowledged = true;
        alert.AcknowledgedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(alert);
    }

    [HttpPost("acknowledge-all")]
    public async Task<IActionResult> AcknowledgeAll()
    {
        var alerts = await _db.Alerts.Where(a => !a.IsAcknowledged).ToListAsync();
        foreach (var alert in alerts)
        {
            alert.IsAcknowledged = true;
            alert.AcknowledgedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        return Ok(new { Acknowledged = alerts.Count });
    }
}
