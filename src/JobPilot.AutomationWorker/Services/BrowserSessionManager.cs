using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace JobPilot.AutomationWorker.Services;

public interface IBrowserSessionManager : IAsyncDisposable
{
    Task<IPage> LaunchOrAttachAsync(Guid sessionId, string url, string tenantKey, bool headless = false, CancellationToken ct = default);
    IPage? GetActivePage(Guid sessionId);
    Task CloseSessionAsync(Guid sessionId, CancellationToken ct = default);
}

/// <summary>
/// One visible Chromium window with one tab per application session.
///
/// Batch runs leave several applications open at Workday's Review page at the same time, each
/// in its own tab, for the user to check and submit. This replaces the earlier one-browser-per-
/// tenant model (ADR-006 profile isolation): Workday logins are per site (per domain) anyway, and
/// a single shared profile keeps every site's sign-in between runs.
/// </summary>
public sealed class BrowserSessionManager(ILogger<BrowserSessionManager> logger) : IBrowserSessionManager
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<Guid, IPage> _pages = new();
    private IPlaywright? _playwright;
    private IBrowser? _cdpBrowser;
    private IBrowserContext? _context;

    public async Task<IPage> LaunchOrAttachAsync(
        Guid sessionId,
        string url,
        string tenantKey,
        bool headless = false,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_pages.TryGetValue(sessionId, out var existing) && !existing.IsClosed)
            {
                await existing.BringToFrontAsync();
                return existing;
            }

            var context = await GetContextAsync(headless);

            // Reuse a tab already showing this job (e.g. the worker restarted and re-attached),
            // else an unused blank tab, else open a new one.
            var claimed = _pages.Values.Where(p => !p.IsClosed).ToHashSet();
            var page = context.Pages.FirstOrDefault(p => !p.IsClosed && !claimed.Contains(p) && SameJob(p.Url, url))
                       ?? context.Pages.FirstOrDefault(p => !p.IsClosed && !claimed.Contains(p) && p.Url == "about:blank")
                       ?? await context.NewPageAsync();

            _pages[sessionId] = page;

            if (!string.IsNullOrWhiteSpace(url) && !page.Url.Contains("myworkdayjobs.com", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Session {SessionId}: opening {Url}", sessionId, url);
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });
            }

            await page.BringToFrontAsync();
            return page;
        }
        finally
        {
            _lock.Release();
        }
    }

    public IPage? GetActivePage(Guid sessionId) =>
        _pages.TryGetValue(sessionId, out var page) && !page.IsClosed ? page : null;

    public async Task CloseSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (_pages.Remove(sessionId, out var page) && !page.IsClosed)
        {
            try { await page.CloseAsync(); }
            catch (Exception ex) { logger.LogDebug(ex, "Error closing tab for session {SessionId}", sessionId); }
        }
    }

    private async Task<IBrowserContext> GetContextAsync(bool headless)
    {
        if (_context != null)
        {
            try
            {
                _ = _context.Pages; // throws if the browser was closed by the user
                if (_cdpBrowser == null || _cdpBrowser.IsConnected) return _context;
            }
            catch
            {
                // fall through and relaunch
            }
            _context = null;
            _cdpBrowser = null;
            _pages.Clear();
        }

        _playwright ??= await Playwright.CreateAsync();

        // A browser left running with the debugging port (worker restarted): attach to it.
        try
        {
            _cdpBrowser = await _playwright.Chromium.ConnectOverCDPAsync("http://localhost:9222", new BrowserTypeConnectOverCDPOptions { Timeout = 2000 });
            if (_cdpBrowser.Contexts.Count > 0)
            {
                logger.LogInformation("Attached to the running Chromium on port 9222");
                _context = _cdpBrowser.Contexts[0];
                return _context;
            }
        }
        catch
        {
            _cdpBrowser = null;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profileDir = Path.Combine(appData, "JobPilot", "browser-profiles", "shared");
        Directory.CreateDirectory(profileDir);

        var options = new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = headless,
            ViewportSize = ViewportSize.NoViewport,
            AcceptDownloads = true,
            Args = new[]
            {
                "--no-sandbox",
                "--disable-blink-features=AutomationControlled",
                "--start-maximized",
                "--remote-debugging-port=9222"
            }
        };

        var bundledExe = Path.Combine(appData, "ms-playwright", "chromium-1155", "chrome-win", "chrome.exe");
        if (File.Exists(bundledExe))
        {
            options.ExecutablePath = bundledExe;
        }

        logger.LogInformation("Launching Chromium with shared profile at {Dir}", profileDir);
        _context = await _playwright.Chromium.LaunchPersistentContextAsync(profileDir, options);
        return _context;
    }

    private static bool SameJob(string pageUrl, string jobUrl)
    {
        if (string.IsNullOrWhiteSpace(pageUrl) || string.IsNullOrWhiteSpace(jobUrl)) return false;
        // The job id is the last path segment of the posting URL; /apply/... pages extend it.
        var jobId = jobUrl.Split('?')[0].TrimEnd('/').Split('/').LastOrDefault();
        return !string.IsNullOrWhiteSpace(jobId) && pageUrl.Contains(jobId, StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_cdpBrowser != null) await _cdpBrowser.CloseAsync();
            else if (_context != null) await _context.CloseAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error closing browser");
        }
        _pages.Clear();
        _context = null;
        _cdpBrowser = null;
        _playwright?.Dispose();
        _playwright = null;
    }
}
