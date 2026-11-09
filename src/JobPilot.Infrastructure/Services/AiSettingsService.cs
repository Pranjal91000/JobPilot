using System.Diagnostics;
using System.Text;
using System.Text.Json;
using JobPilot.Application.Abstractions;
using JobPilot.Application.Features.Mapping;
using JobPilot.Domain.Entities;
using JobPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobPilot.Infrastructure.Services;

public sealed class AiSettingsService : IAiSettingsService
{
    private readonly JobPilotDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiSettingsService> _logger;

    public AiSettingsService(
        JobPilotDbContext db,
        ISecretProtector secretProtector,
        HttpClient httpClient,
        ILogger<AiSettingsService> logger)
    {
        _db = db;
        _secretProtector = secretProtector;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<AiSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var setting = await _db.ProviderSettings.FirstOrDefaultAsync(ct);
        if (setting == null)
        {
            return new AiSettingsDto(
                ProviderName: "OpenAI",
                DefaultModel: "gpt-4o-mini",
                IsEnabled: false,
                HasApiKey: false,
                LastTestedAt: null,
                LastTestStatus: null);
        }

        return new AiSettingsDto(
            ProviderName: setting.ProviderName,
            DefaultModel: setting.DefaultModel ?? "gpt-4o-mini",
            IsEnabled: setting.IsEnabled,
            HasApiKey: setting.ApiKeyEnc != null && setting.ApiKeyEnc.Length > 0,
            LastTestedAt: null,
            LastTestStatus: null);
    }

    public async Task<AiSettingsDto> UpdateSettingsAsync(UpdateAiSettingsRequest request, CancellationToken ct)
    {
        var setting = await _db.ProviderSettings.FirstOrDefaultAsync(ct);
        if (setting == null)
        {
            setting = new ProviderSetting
            {
                ProviderName = request.ProviderName,
                DefaultModel = request.DefaultModel,
                IsEnabled = request.IsEnabled
            };
            if (!string.IsNullOrWhiteSpace(request.ApiKey))
            {
                setting.ApiKeyEnc = _secretProtector.Protect(Encoding.UTF8.GetBytes(request.ApiKey));
            }
            _db.ProviderSettings.Add(setting);
        }
        else
        {
            setting.ProviderName = request.ProviderName;
            setting.DefaultModel = request.DefaultModel;
            setting.IsEnabled = request.IsEnabled;
            if (!string.IsNullOrWhiteSpace(request.ApiKey))
            {
                setting.ApiKeyEnc = _secretProtector.Protect(Encoding.UTF8.GetBytes(request.ApiKey));
            }
        }

        await _db.SaveChangesAsync(ct);

        return new AiSettingsDto(
            ProviderName: setting.ProviderName,
            DefaultModel: setting.DefaultModel ?? "gpt-4o-mini",
            IsEnabled: setting.IsEnabled,
            HasApiKey: setting.ApiKeyEnc != null && setting.ApiKeyEnc.Length > 0,
            LastTestedAt: null,
            LastTestStatus: null);
    }

    public async Task<string?> GetDecryptedApiKeyAsync(CancellationToken ct)
    {
        var setting = await _db.ProviderSettings.FirstOrDefaultAsync(ct);
        if (setting?.ApiKeyEnc == null || setting.ApiKeyEnc.Length == 0)
            return null;

        try
        {
            var bytes = _secretProtector.Unprotect(setting.ApiKeyEnc);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt stored API key using DPAPI");
            return null;
        }
    }

    public async Task<TestAiConnectionResponse> TestConnectionAsync(CancellationToken ct)
    {
        var apiKey = await GetDecryptedApiKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new TestAiConnectionResponse(false, "No API key configured. Please enter and save an API key first.", null);
        }

        var settings = await GetSettingsAsync(ct);
        var sw = Stopwatch.StartNew();

        try
        {
            var endpoint = settings.ProviderName.Contains("gemini", StringComparison.OrdinalIgnoreCase)
                ? "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions"
                : "https://api.openai.com/v1/chat/completions";

            var payload = new
            {
                model = settings.DefaultModel,
                max_tokens = 20,
                messages = new object[]
                {
                    new { role = "user", content = "Reply with 'ok' to verify connectivity." }
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var resp = await _httpClient.SendAsync(req, cts.Token);
            sw.Stop();

            if (resp.IsSuccessStatusCode)
            {
                return new TestAiConnectionResponse(true, $"Successfully connected to {settings.ProviderName} ({settings.DefaultModel})!", (int)sw.ElapsedMilliseconds);
            }

            var errBody = await resp.Content.ReadAsStringAsync(ct);
            return new TestAiConnectionResponse(false, $"Connection failed with HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestAiConnectionResponse(false, $"Connection error: {ex.Message}", (int)sw.ElapsedMilliseconds);
        }
    }
}

