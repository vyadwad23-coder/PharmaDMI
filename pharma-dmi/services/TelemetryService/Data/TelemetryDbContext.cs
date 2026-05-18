using Microsoft.EntityFrameworkCore;
using TelemetryService.Models;

namespace TelemetryService.Data;

public class TelemetryDbContext : DbContext
{
    public TelemetryDbContext(DbContextOptions<TelemetryDbContext> options) : base(options) { }

    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<TelemetryReading> TelemetryReadings => Set<TelemetryReading>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Machine>().HasData(
            new Machine { Id = 1, MachineId = "M001", Name = "Reactor Vessel A", Type = "Bioreactor", Location = "Block A", Status = "Online" },
            new Machine { Id = 2, MachineId = "M002", Name = "Mixing Unit B", Type = "Mixer", Location = "Block B", Status = "Online" },
            new Machine { Id = 3, MachineId = "M003", Name = "Filtration Unit C", Type = "Filter", Location = "Block C", Status = "Warning" },
            new Machine { Id = 4, MachineId = "M004", Name = "Dryer Unit D", Type = "Dryer", Location = "Block D", Status = "Online" },
            new Machine { Id = 5, MachineId = "M005", Name = "Granulator E", Type = "Granulator", Location = "Block A", Status = "Offline" }
        );
    }
}
