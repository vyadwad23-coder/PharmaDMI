namespace TelemetryService.Models;

public class Machine
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Status { get; set; } = "Online";
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

public class TelemetryReading
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public double Pressure { get; set; }
    public double Vibration { get; set; }
    public double Humidity { get; set; }
    public double PowerConsumption { get; set; }
    public double ProductionRate { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class TelemetryDto
{
    public string MachineId { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public double Pressure { get; set; }
    public double Vibration { get; set; }
    public double Humidity { get; set; }
    public double PowerConsumption { get; set; }
    public double ProductionRate { get; set; }
}
