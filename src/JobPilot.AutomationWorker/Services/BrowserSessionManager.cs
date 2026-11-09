using JobPilot.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace JobPilot.AutomationWorker.Services;

public interface IBrowserSessionManager : IAsyncDisposable
{
    Task<IPage> LaunchOrAttachAsync(Guid sessionId, string url, string tenantKey, bool headless = false, CancellationToken ct = default);
    IPage? GetActivePage(Guid sessionId);
    Task CloseSessionAsync(Guid sessionId, CancellationToken ct = default);
}

public sealed class BrowserSessionManager(ILogger<BrowserSessionManager> logger) : IBrowserSessionManager
{
    private IPlaywright? _playwright;
    private IBrowserContext? _browserContext;
    private IPage? _activePage;
    private Guid? _activeSessionId;

    public async Task<IPage> LaunchOrAttachAsync(
        Guid sessionId,
        string url,
        string tenantKey,
        bool headless = false,
        CancellationToken ct = default)
    {
        if (_activePage != null && _activeSessionId == sessionId && !_activePage.IsClosed)
        {
            return _activePage;
        }

        // Close any prior session to enforce single active session (ADR-012)
        if (_browserContext != null)
        {
            await CloseCurrentContextAsync();
        }

        _playwright ??= await Playwright.CreateAsync();

        // Local isolated directory per tenant (ADR-006)
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var safeTenant = string.IsNullOrWhiteSpace(tenantKey) ? "default" : string.Concat(tenantKey.Split(Path.GetInvalidFileNameChars()));
        var userProfileDir = Path.Combine(appData, "JobPilot", "browser-profiles", safeTenant);
        Directory.CreateDirectory(userProfileDir);

        logger.LogInformation("Launching Chromium session {SessionId} with profile at {Dir} (Headless: {Headless})",
            sessionId, userProfileDir, headless);

        _browserContext = await _playwright.Chromium.LaunchPersistentContextAsync(userProfileDir, new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = headless,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            AcceptDownloads = true
        });

        // Use existing page or create new
        _activePage = _browserContext.Pages.Count > 0
            ? _browserContext.Pages[0]
            : await _browserContext.NewPageAsync();

        _activeSessionId = sessionId;

        if (!string.IsNullOrWhiteSpace(url))
        {
            logger.LogInformation("Navigating to {Url}", url);
            await _activePage.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30000
            });
        }

        return _activePage;
    }

    public IPage? GetActivePage(Guid sessionId)
    {
        if (_activeSessionId == sessionId && _activePage != null && !_activePage.IsClosed)
        {
            return _activePage;
        }
        return null;
    }

    public async Task CloseSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (_activeSessionId == sessionId)
        {
            await CloseCurrentContextAsync();
        }
    }

    private async Task CloseCurrentContextAsync()
    {
        try
        {
            if (_activePage != null && !_activePage.IsClosed)
            {
                await _activePage.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error closing page");
        }

        try
        {
            if (_browserContext != null)
            {
                await _browserContext.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error closing browser context");
        }

        _activePage = null;
        _browserContext = null;
        _activeSessionId = null;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseCurrentContextAsync();
        _playwright?.Dispose();
        _playwright = null;
    }
}
