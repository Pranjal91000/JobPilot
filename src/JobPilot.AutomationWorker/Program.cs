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
// One adapter per ATS; the worker picks the right one for each application.
builder.Services.AddSingleton<WorkdayAdapter>();
builder.Services.AddSingleton<IWorkdayAdapter>(sp => sp.GetRequiredService<WorkdayAdapter>());
builder.Services.AddSingleton<IApplyFormAdapter>(sp => sp.GetRequiredService<WorkdayAdapter>());
builder.Services.AddSingleton<IApplyFormAdapter, GreenhouseAdapter>();
builder.Services.AddSingleton<IApplyFormAdapter, LeverAdapter>();
builder.Services.AddSingleton<IApplyFormAdapter, AshbyAdapter>();
builder.Services.AddSingleton<AdapterRegistry>();

// The API holds the encryption keys (resume, AI key, Workday password) and makes the per-step
// decisions; the worker asks it over localhost.
var apiBaseUrl = builder.Configuration["JobPilotApi:BaseUrl"] ?? "http://localhost:5080/";
builder.Services.AddHttpClient<JobPilotApiClient>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl);
    c.Timeout = TimeSpan.FromMinutes(3); // AI mapping + drafting can take a while
    c.DefaultRequestHeaders.Add("X-JobPilot-Worker", "1");
});

// Background Worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
