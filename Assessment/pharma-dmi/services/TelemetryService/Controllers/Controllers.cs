using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TelemetryService.Data;
using TelemetryService.Models;

namespace TelemetryService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MachinesController : ControllerBase
{
    private readonly TelemetryDbContext _db;
    public MachinesController(TelemetryDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _db.Machines.ToListAsync());

    [HttpGet("{machineId}")]
    public async Task<IActionResult> Get(string machineId)
    {
        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.MachineId == machineId);
        return machine is null ? NotFound() : Ok(machine);
    }

    [HttpGet("{machineId}/telemetry")]
    public async Task<IActionResult> GetTelemetry(string machineId, [FromQuery] int limit = 50)
    {
        var readings = await _db.TelemetryReadings
            .Where(t => t.MachineId == machineId)
            .OrderByDescending(t => t.Timestamp)
            .Take(limit)
            .ToListAsync();
        return Ok(readings);
    }

    [HttpGet("{machineId}/latest")]
    public async Task<IActionResult> GetLatest(string machineId)
    {
        var latest = await _db.TelemetryReadings
            .Where(t => t.MachineId == machineId)
            .OrderByDescending(t => t.Timestamp)
            .FirstOrDefaultAsync();
        return latest is null ? NotFound() : Ok(latest);
    }

    [HttpPost("{machineId}/telemetry")]
    public async Task<IActionResult> PostTelemetry(string machineId, [FromBody] TelemetryDto dto)
    {
        var reading = new TelemetryReading
        {
            MachineId = machineId,
            Temperature = dto.Temperature,
            Pressure = dto.Pressure,
            Vibration = dto.Vibration,
            Humidity = dto.Humidity,
            PowerConsumption = dto.PowerConsumption,
            ProductionRate = dto.ProductionRate,
            Timestamp = DateTime.UtcNow
        };
        _db.TelemetryReadings.Add(reading);
        await _db.SaveChangesAsync();
        return Ok(reading);
    }
}

[ApiController]
[Route("api/[controller]")]
public class TelemetryController : ControllerBase
{
    private readonly TelemetryDbContext _db;
    public TelemetryController(TelemetryDbContext db) => _db = db;

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var allMachines = await _db.Machines.ToListAsync();
        var latestReadings = new List<object>();

        foreach (var machine in allMachines)
        {
            var latest = await _db.TelemetryReadings
                .Where(t => t.MachineId == machine.MachineId)
                .OrderByDescending(t => t.Timestamp)
                .FirstOrDefaultAsync();

            if (latest != null)
            {
                latestReadings.Add(new
                {
                    machine.MachineId,
                    machine.Name,
                    machine.Status,
                    machine.Location,
                    latest.Temperature,
                    latest.Pressure,
                    latest.Vibration,
                    latest.Humidity,
                    latest.PowerConsumption,
                    latest.ProductionRate,
                    latest.Timestamp
                });
            }
        }

        return Ok(new
        {
            TotalMachines = allMachines.Count,
            Online = allMachines.Count(m => m.Status == "Online"),
            Warning = allMachines.Count(m => m.Status == "Warning"),
            Offline = allMachines.Count(m => m.Status == "Offline"),
            Readings = latestReadings
        });
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int minutes = 30)
    {
        var since = DateTime.UtcNow.AddMinutes(-minutes);
        var readings = await _db.TelemetryReadings
            .Where(t => t.Timestamp >= since)
            .OrderByDescending(t => t.Timestamp)
            .Take(200)
            .ToListAsync();
        return Ok(readings);
    }
}
