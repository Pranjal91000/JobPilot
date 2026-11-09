using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JobPilot.Application.Abstractions;
using JobPilot.Application.Features.Mapping;
using Microsoft.Extensions.Logging;

namespace JobPilot.Infrastructure.AI;

public sealed class OpenAiCompatibleAiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly IAiSettingsService _settingsService;
    private readonly ILogger<OpenAiCompatibleAiProvider> _logger;

    public OpenAiCompatibleAiProvider(
        HttpClient httpClient,
        IAiSettingsService settingsService,
        ILogger<OpenAiCompatibleAiProvider> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _logger = logger;
    }

    public string Name => "OpenAI-Compatible";

    public AiCapabilities Capabilities => new(
        NativeJsonSchema: true,
        MaxContextTokens: 128000,
        SupportsSystemPrompt: true);

    public async Task<AiStructuredResponse> CompleteStructuredAsync(AiStructuredRequest request, CancellationToken ct)
    {
        var settings = await _settingsService.GetSettingsAsync(ct);
        if (!settings.IsEnabled)
        {
            return new AiStructuredResponse(
                Success: false,
                Output: null,
                SchemaValid: false,
                RawText: null,
                InputTokens: 0,
                OutputTokens: 0,
                Latency: TimeSpan.Zero,
                Model: settings.DefaultModel,
                Error: "AI mapping is disabled in settings.");
        }

        var apiKey = await _settingsService.GetDecryptedApiKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiStructuredResponse(
                Success: false,
                Output: null,
                SchemaValid: false,
                RawText: null,
                InputTokens: 0,
                OutputTokens: 0,
                Latency: TimeSpan.Zero,
                Model: settings.DefaultModel,
                Error: "No API key configured for AI provider.");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var endpoint = DetermineEndpoint(settings.ProviderName);
            var model = string.IsNullOrWhiteSpace(settings.DefaultModel) ? "gpt-4o-mini" : settings.DefaultModel;

            var payload = new
            {
                model = model,
                temperature = (double)request.Options.Temperature,
                max_tokens = request.Options.MaxOutputTokens,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user", content = request.UserContentJson }
                }
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(request.Options.Timeout);

            var response = await _httpClient.SendAsync(httpRequest, cts.Token);
            sw.Stop();

            var respText = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI provider returned error {StatusCode}: {Response}", response.StatusCode, respText);
                return new AiStructuredResponse(
                    Success: false,
                    Output: null,
                    SchemaValid: false,
                    RawText: respText,
                    InputTokens: 0,
                    OutputTokens: 0,
                    Latency: sw.Elapsed,
                    Model: model,
                    Error: $"AI Provider HTTP error {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            using var doc = JsonDocument.Parse(respText);
            var root = doc.RootElement;

            string contentStr = "";
            int inputTokens = 0;
            int outputTokens = 0;

            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt)) inputTokens = pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ctProp)) outputTokens = ctProp.GetInt32();
            }

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                {
                    contentStr = content.GetString() ?? "";
                }
            }

            // Parse and validate structured output JSON locally
            try
            {
                var structuredDoc = JsonDocument.Parse(contentStr);
                return new AiStructuredResponse(
                    Success: true,
                    Output: structuredDoc,
                    SchemaValid: true,
                    RawText: contentStr,
                    InputTokens: inputTokens,
                    OutputTokens: outputTokens,
                    Latency: sw.Elapsed,
                    Model: model,
                    Error: null);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse structured JSON from AI output: {Raw}", contentStr);
                return new AiStructuredResponse(
                    Success: false,
                    Output: null,
                    SchemaValid: false,
                    RawText: contentStr,
                    InputTokens: inputTokens,
                    OutputTokens: outputTokens,
                    Latency: sw.Elapsed,
                    Model: model,
                    Error: $"Malformed JSON output from model: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "AI provider request threw exception");
            return new AiStructuredResponse(
                Success: false,
                Output: null,
                SchemaValid: false,
                RawText: null,
                InputTokens: 0,
                OutputTokens: 0,
                Latency: sw.Elapsed,
                Model: settings.DefaultModel,
                Error: ex.Message);
        }
    }

    private static string DetermineEndpoint(string providerName)
    {
        var prov = providerName.ToLowerInvariant();
        if (prov.Contains("gemini"))
        {
            return "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";
        }
        if (prov.Contains("groq"))
        {
            return "https://api.groq.com/openai/v1/chat/completions";
        }
        if (prov.Contains("openrouter"))
        {
            return "https://openrouter.ai/api/v1/chat/completions";
        }
        // Default standard OpenAI endpoint
        return "https://api.openai.com/v1/chat/completions";
    }
}

