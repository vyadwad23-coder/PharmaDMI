using Microsoft.EntityFrameworkCore;

namespace AlertService.Models;

public class Alert
{
    public int Id { get; set; }
    public string AlertId { get; set; } = Guid.NewGuid().ToString();
    public string MachineId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info"; // Info, Warning, Critical
    public string Type { get; set; } = string.Empty; // Temperature, Pressure, Vibration, etc.
    public string Message { get; set; } = string.Empty;
    public double ActualValue { get; set; }
    public double ThresholdValue { get; set; }
    public bool IsAcknowledged { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAt { get; set; }
    public string? RootCause { get; set; }
}

public class ThresholdConfig
{
    public int Id { get; set; }
    public string MachineType { get; set; } = string.Empty;
    public string Parameter { get; set; } = string.Empty;
    public double WarningThreshold { get; set; }
    public double CriticalThreshold { get; set; }
    public string Unit { get; set; } = string.Empty;
}

public class AlertDbContext : DbContext
{
    public AlertDbContext(DbContextOptions<AlertDbContext> options) : base(options) { }
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<ThresholdConfig> ThresholdConfigs => Set<ThresholdConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ThresholdConfig>().HasData(
            new ThresholdConfig { Id = 1, MachineType = "Bioreactor", Parameter = "Temperature", WarningThreshold = 40, CriticalThreshold = 45, Unit = "°C" },
            new ThresholdConfig { Id = 2, MachineType = "Bioreactor", Parameter = "Pressure", WarningThreshold = 3.0, CriticalThreshold = 4.0, Unit = "bar" },
            new ThresholdConfig { Id = 3, MachineType = "Mixer", Parameter = "Vibration", WarningThreshold = 1.0, CriticalThreshold = 1.5, Unit = "mm/s" },
            new ThresholdConfig { Id = 4, MachineType = "Dryer", Parameter = "Temperature", WarningThreshold = 95, CriticalThreshold = 105, Unit = "°C" },
            new ThresholdConfig { Id = 5, MachineType = "Filter", Parameter = "Pressure", WarningThreshold = 4.0, CriticalThreshold = 5.0, Unit = "bar" }
        );
    }
}
