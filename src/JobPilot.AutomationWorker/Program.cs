using JobPilot.Application.Features.Automation;
using JobPilot.AutomationWorker;
using JobPilot.AutomationWorker.Adapters;
using JobPilot.AutomationWorker.Services;
using JobPilot.Infrastructure.Persistence;
using JobPilot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// Database configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=jobpilot_dev;Username=postgres;Password=postgres";

builder.Services.AddDbContext<JobPilotDbContext>(options =>
    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention());

// Automation Services
builder.Services.AddScoped<IAutomationCommandService, AutomationCommandService>();
builder.Services.AddScoped<IWorkerHeartbeatService, WorkerHeartbeatService>();
builder.Services.AddSingleton<IBrowserSessionManager, BrowserSessionManager>();
builder.Services.AddSingleton<IWorkdayAdapter, WorkdayAdapter>();

// Background Worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
