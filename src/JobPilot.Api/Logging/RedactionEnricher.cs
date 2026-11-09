using Serilog.Core;
using Serilog.Events;

namespace JobPilot.Api.Logging;

public class RedactionEnricher : ILogEventEnricher
{
    private static readonly string[] SensitivePropertyKeys =
    [
        "email", "phone", "phonenational", "salary", "expectedsalary", "answervalue",
        "suggestedvalue", "password", "cookie", "authorization", "apikey"
    ];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var propertiesToRedact = logEvent.Properties
            .Where(p => SensitivePropertyKeys.Any(k => p.Key.ToLowerInvariant().Contains(k)))
            .Select(p => p.Key)
            .ToList();

        foreach (var key in propertiesToRedact)
        {
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(key, "[REDACTED]"));
        }
    }
}

