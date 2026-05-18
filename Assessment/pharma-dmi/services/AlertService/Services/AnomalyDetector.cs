using AlertService.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AlertService.Services;

public class AnomalyDetector : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AnomalyDetector> _logger;
    private readonly HttpClient _httpClient;

    private readonly Dictionary<string, (string Type, double WarnThreshold, double CritThreshold, string Unit)[]> _thresholds = new()
    {
        { "M001", new[] { ("Temperature", 40.0, 45.0, "°C"), ("Pressure", 3.0, 4.0, "bar"), ("Vibration", 0.6, 0.9, "mm/s") } },
        { "M002", new[] { ("Temperature", 55.0, 65.0, "°C"), ("Vibration", 1.0, 1.5, "mm/s"), ("PowerConsumption", 180.0, 210.0, "kW") } },
        { "M003", new[] { ("Pressure", 4.0, 5.0, "bar"), ("Temperature", 30.0, 35.0, "°C") } },
        { "M004", new[] { ("Temperature", 95.0, 105.0, "°C"), ("PowerConsumption", 185.0, 215.0, "kW") } },
        { "M005", new[] { ("Vibration", 1.2, 1.8, "mm/s"), ("Temperature", 40.0, 48.0, "°C") } },
    };

    private readonly Dictionary<string, string> _machineNames = new()
    {
        { "M001", "Reactor Vessel A" }, { "M002", "Mixing Unit B" },
        { "M003", "Filtration Unit C" }, { "M004", "Dryer Unit D" }, { "M005", "Granulator E" }
    };

    public AnomalyDetector(IServiceProvider serviceProvider, ILogger<AnomalyDetector> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await DetectAnomalies();
            await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
        }
    }

    private async Task DetectAnomalies()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlertDbContext>();

        foreach (var (machineId, thresholds) in _thresholds)
        {
            try
            {
                var response = await _httpClient.GetAsync($"http://localhost:5001/api/machines/{machineId}/latest");
                if (!response.IsSuccessStatusCode) continue;

                var json = await response.Content.ReadAsStringAsync();
                var reading = JsonSerializer.Deserialize<JsonElement>(json);

                foreach (var (paramType, warnThresh, critThresh, unit) in thresholds)
                {
                    double value = paramType switch
                    {
                        "Temperature" => reading.GetProperty("temperature").GetDouble(),
                        "Pressure" => reading.GetProperty("pressure").GetDouble(),
                        "Vibration" => reading.GetProperty("vibration").GetDouble(),
                        "PowerConsumption" => reading.GetProperty("powerConsumption").GetDouble(),
                        _ => 0
                    };

                    string severity = value > critThresh ? "Critical" : value > warnThresh ? "Warning" : "Normal";
                    if (severity == "Normal") continue;

                    // Avoid duplicate alerts within 2 minutes
                    var recentAlert = await db.Alerts
                        .Where(a => a.MachineId == machineId && a.Type == paramType && a.CreatedAt > DateTime.UtcNow.AddMinutes(-2))
                        .FirstOrDefaultAsync();

                    if (recentAlert != null) continue;

                    var alert = new Alert
                    {
                        AlertId = Guid.NewGuid().ToString(),
                        MachineId = machineId,
                        MachineName = _machineNames.GetValueOrDefault(machineId, machineId),
                        Severity = severity,
                        Type = paramType,
                        Message = $"{paramType} {severity.ToLower()} on {_machineNames.GetValueOrDefault(machineId, machineId)}: {value:F2}{unit} (threshold: {(severity == "Critical" ? critThresh : warnThresh)}{unit})",
                        ActualValue = value,
                        ThresholdValue = severity == "Critical" ? critThresh : warnThresh,
                        RootCause = GenerateRootCause(paramType, severity),
                        CreatedAt = DateTime.UtcNow
                    };

                    db.Alerts.Add(alert);
                    _logger.LogWarning("Alert: {Message}", alert.Message);
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting anomalies for {MachineId}", machineId);
            }
        }
    }

    private static string GenerateRootCause(string paramType, string severity) => paramType switch
    {
        "Temperature" => severity == "Critical"
            ? "Possible cooling system failure or blocked heat exchanger. Check coolant flow and heat exchanger fouling."
            : "Minor temperature deviation. Verify ambient temperature and cooling water supply.",
        "Pressure" => severity == "Critical"
            ? "Possible blockage in downstream piping or failed pressure relief valve. Inspect filters and valves immediately."
            : "Slight pressure buildup. Check for partial blockage or viscosity change in process fluid.",
        "Vibration" => severity == "Critical"
            ? "Severe mechanical imbalance detected. Possible bearing failure or rotor misalignment. Immediate inspection required."
            : "Elevated vibration. Check for loose fasteners, minor imbalance, or worn bearings.",
        "PowerConsumption" => severity == "Critical"
            ? "Abnormally high power draw. Possible motor winding issue or excessive mechanical load. Check motor temperature."
            : "Elevated power consumption. Review process load and check for mechanical friction.",
        _ => "Anomaly detected. Manual inspection recommended."
    };
}
