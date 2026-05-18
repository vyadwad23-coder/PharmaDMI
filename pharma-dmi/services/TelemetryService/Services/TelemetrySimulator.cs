using TelemetryService.Data;
using TelemetryService.Models;

namespace TelemetryService.Services;

public class TelemetrySimulator : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TelemetrySimulator> _logger;
    private readonly Random _random = new();

    private readonly Dictionary<string, (double BaseTemp, double BasePressure, double BaseVibration)> _machineProfiles = new()
    {
        { "M001", (37.5, 2.1, 0.3) },  // Bioreactor - controlled temp
        { "M002", (45.0, 1.5, 0.8) },  // Mixer - higher vibration
        { "M003", (22.0, 3.5, 0.2) },  // Filter - higher pressure
        { "M004", (85.0, 1.2, 0.4) },  // Dryer - high temp
        { "M005", (30.0, 1.8, 1.2) },  // Granulator - high vibration
    };

    public TelemetrySimulator(IServiceProvider serviceProvider, ILogger<TelemetrySimulator> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await GenerateTelemetry();
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task GenerateTelemetry()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();
        var machines = new[] { "M001", "M002", "M003", "M004", "M005" };

        foreach (var machineId in machines)
        {
            if (!_machineProfiles.TryGetValue(machineId, out var profile)) continue;

            // Occasionally inject anomalies
            bool isAnomaly = _random.NextDouble() < 0.05;
            double anomalyMultiplier = isAnomaly ? 1.4 + _random.NextDouble() * 0.6 : 1.0;

            var reading = new TelemetryReading
            {
                MachineId = machineId,
                Temperature = Math.Round(profile.BaseTemp + (_random.NextDouble() - 0.5) * 4 * anomalyMultiplier, 2),
                Pressure = Math.Round(profile.BasePressure + (_random.NextDouble() - 0.5) * 0.4 * anomalyMultiplier, 3),
                Vibration = Math.Round(profile.BaseVibration + (_random.NextDouble() - 0.5) * 0.2 * anomalyMultiplier, 3),
                Humidity = Math.Round(45 + (_random.NextDouble() - 0.5) * 20, 1),
                PowerConsumption = Math.Round(120 + _random.NextDouble() * 80 * anomalyMultiplier, 1),
                ProductionRate = Math.Round(85 + (_random.NextDouble() - 0.5) * 30, 1),
                Timestamp = DateTime.UtcNow
            };

            db.TelemetryReadings.Add(reading);
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Telemetry generated at {Time}", DateTime.UtcNow);
    }
}
