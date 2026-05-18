using Microsoft.EntityFrameworkCore;
using TelemetryService.Data;
using TelemetryService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TelemetryDbContext>(opt =>
    opt.UseSqlite("Data Source=telemetry.db"));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new() { Title = "Telemetry Service", Version = "v1" }));
builder.Services.AddHostedService<TelemetrySimulator>();
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

app.Run("http://0.0.0.0:5001");
