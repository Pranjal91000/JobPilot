using JobPilot.Api.Logging;
using JobPilot.Application.Abstractions;
using JobPilot.Application.Features.Automation;
using JobPilot.Application.Features.Mapping;
using JobPilot.Application.Features.Retention;
using JobPilot.Infrastructure.AI;
using JobPilot.Infrastructure.Persistence;
using JobPilot.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog with redaction policies (Spec §20.6)
var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var logPath = Path.Combine(localAppData, "JobPilot", "logs", "jobpilot-.log");

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.With<RedactionEnricher>()
    .WriteTo.Console()
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
    .CreateLogger();

builder.Host.UseSerilog();

// Data Protection with DPAPI (Spec §20.2, §20.5)
var keyPath = Path.Combine(localAppData, "JobPilot", "keys");
Directory.CreateDirectory(keyPath);

var dpBuilder = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath));

if (OperatingSystem.IsWindows())
{
    dpBuilder.ProtectKeysWithDpapi();
}

// Database Context
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=jobpilot_dev;Username=postgres;Password=postgres;";

builder.Services.AddDbContext<JobPilotDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.MigrationsAssembly("JobPilot.Infrastructure");
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "jobpilot");
    });
    options.UseSnakeCaseNamingConvention();
});

// Core services
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
builder.Services.AddSingleton<IFileStore, LocalEncryptedFileStore>();

builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddScoped<IResumeService, ResumeService>();
builder.Services.AddScoped<IJobPostService, JobPostService>();
builder.Services.AddScoped<IJobApplicationService, JobApplicationService>();
builder.Services.AddScoped<JobApplicationService>();

// Mapping & AI Services (Spec §16, §17)
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IAdapterRuleMapper, AdapterRuleMapper>();
builder.Services.AddSingleton<IDictionaryMapper, DictionaryMapper>();
builder.Services.AddSingleton<ISavedAnswerMatcher, SavedAnswerMatcher>();
builder.Services.AddSingleton<IGroundingValidator, GroundingValidator>();
builder.Services.AddSingleton<IConfidenceCalculator, ConfidenceCalculator>();
builder.Services.AddScoped<IAiSettingsService, AiSettingsService>();
builder.Services.AddScoped<IAiProvider, OpenAiCompatibleAiProvider>();
builder.Services.AddScoped<IFieldMappingService, FieldMappingService>();
builder.Services.AddScoped<IAutomationCommandService, AutomationCommandService>();
builder.Services.AddScoped<IWorkerHeartbeatService, WorkerHeartbeatService>();
builder.Services.AddScoped<IWorkdayAutomationService, WorkdayAutomationService>();
builder.Services.AddScoped<IDataRetentionService, DataRetentionService>();

// CORS policy for frontend dev server
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalDev", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddControllers();
builder.Services.AddProblemDetails();

var app = builder.Build();

// CLI command handling: "migrate" (Spec §13.4)
if (args.Contains("migrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<JobPilotDbContext>();
    Console.WriteLine("Applying EF Core database migrations...");
    await db.Database.MigrateAsync();
    Console.WriteLine("Database migrations applied successfully.");
    return;
}

app.UseCors("LocalDev");
app.UseRouting();

var fixturesDir = Path.Combine(Directory.GetCurrentDirectory(), "fixtures", "mock-workday");
if (!Directory.Exists(fixturesDir))
{
    fixturesDir = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "fixtures", "mock-workday"));
}
if (Directory.Exists(fixturesDir))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(fixturesDir),
        RequestPath = "/mock-workday"
    });
}

app.MapControllers();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
