using Microsoft.Playwright;

namespace CriatorioVirtual.Infrastructure.Documents;

public interface IHtmlToPdfRenderer
{
    Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken = default);
}
/// <summary>
/// Converts HTML templates with one reusable Chromium process and isolated Playwright contexts.
/// The bounded semaphore keeps document rendering from scaling with the number of API requests.
/// </summary>
public sealed class ChromiumHtmlToPdfRenderer : IHtmlToPdfRenderer, IDisposable
{
    private static readonly string[] WindowsExecutableCandidates =
    [
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
    ];

    private static readonly string[] UnixExecutableCandidates =
    [
        "/usr/bin/chromium",
        "/usr/bin/chromium-browser",
        "/usr/bin/google-chrome",
        "/usr/bin/google-chrome-stable"
    ];

    private static readonly SemaphoreSlim sharedBrowserLock = new(1, 1);
    private static IPlaywright? sharedPlaywright;
    private static IBrowser? sharedBrowser;
    private static int sharedRendererCount;

    private readonly SemaphoreSlim renderSlots;
    private readonly string? configuredExecutablePath;
    private readonly TimeSpan renderTimeout;
    private int disposed;

    public ChromiumHtmlToPdfRenderer()
        : this(new DocumentRenderingOptions())
    {
    }

    public ChromiumHtmlToPdfRenderer(string? executablePath)
        : this(new DocumentRenderingOptions(), executablePath)
    {
    }

    public ChromiumHtmlToPdfRenderer(
        DocumentRenderingOptions options,
        string? executablePath = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxConcurrentRenders is <= 0 or > DocumentRenderingOptions.MaximumConcurrentRenders)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaxConcurrentRenders),
                options.MaxConcurrentRenders,
                $"The maximum number of concurrent document renders must be between 1 and {DocumentRenderingOptions.MaximumConcurrentRenders}.");
        }

        if (options.RenderTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.RenderTimeoutSeconds),
                options.RenderTimeoutSeconds,
                "The document render timeout must be greater than zero.");
        }

        configuredExecutablePath = string.IsNullOrWhiteSpace(executablePath)
            ? Environment.GetEnvironmentVariable("DocumentRendering__ChromiumPath") ??
              Environment.GetEnvironmentVariable("DOCUMENT_RENDERING_CHROMIUM_PATH")
            : executablePath;
        configuredExecutablePath = string.IsNullOrWhiteSpace(configuredExecutablePath)
            ? null
            : configuredExecutablePath.Trim();
        renderTimeout = TimeSpan.FromSeconds(options.RenderTimeoutSeconds);
        renderSlots = new SemaphoreSlim(options.MaxConcurrentRenders, options.MaxConcurrentRenders);
        Interlocked.Increment(ref sharedRendererCount);
    }

    public async Task<byte[]> RenderAsync(
        string html,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        ThrowIfDisposed();

        using var renderCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        renderCancellation.CancelAfter(renderTimeout);
        var renderCancellationToken = renderCancellation.Token;

        try
        {
            await renderSlots.WaitAsync(renderCancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The HTML document rendering queue exceeded the configured timeout.");
        }

        try
        {
            var content = await RenderCoreAsync(html, renderCancellationToken);
            return content;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Chromium exceeded the HTML document rendering timeout.");
        }
        finally
        {
            renderSlots.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (Interlocked.Decrement(ref sharedRendererCount) == 0)
            {
                DisposeSharedBrowser();
            }
        }
        finally
        {
            renderSlots.Dispose();
        }
    }

    private async Task<byte[]> RenderCoreAsync(
        string html,
        CancellationToken cancellationToken)
    {
        var activeBrowser = await GetBrowserAsync(cancellationToken);
        IBrowserContext? context = null;
        try
        {
            context = await activeBrowser.NewContextAsync();
            context.SetDefaultTimeout((float)renderTimeout.TotalMilliseconds);
            context.SetDefaultNavigationTimeout((float)renderTimeout.TotalMilliseconds);
            using var cancellationRegistration = cancellationToken.Register(
                static state => _ = CloseContextAsync((IBrowserContext)state!),
                context);
            var page = await context.NewPageAsync();

            await page.SetContentAsync(
                html,
                new PageSetContentOptions
                {
                    WaitUntil = WaitUntilState.Load,
                    Timeout = (float)renderTimeout.TotalMilliseconds
                });
            cancellationToken.ThrowIfCancellationRequested();

            await page.EvaluateAsync(
                """
                async () => {
                    await document.fonts.ready;
                    await Promise.all(Array.from(document.images).map(image =>
                        image.complete
                            ? Promise.resolve()
                            : new Promise(resolve => {
                                image.addEventListener('load', resolve, { once: true });
                                image.addEventListener('error', resolve, { once: true });
                            })));
                }
                """);
            cancellationToken.ThrowIfCancellationRequested();

            var pdf = await page.PdfAsync(new PagePdfOptions
            {
                DisplayHeaderFooter = false,
                Margin = new Margin
                {
                    Top = "0",
                    Right = "0",
                    Bottom = "0",
                    Left = "0"
                },
                PreferCSSPageSize = true,
                PrintBackground = true,
                Outline = false,
                Tagged = false
            });
            if (pdf.Length == 0)
            {
                throw new InvalidOperationException("Chromium produced an empty PDF document.");
            }

            return pdf;
        }
        catch (PlaywrightException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (PlaywrightException exception)
        {
            if (!activeBrowser.IsConnected)
            {
                await InvalidateBrowserAsync();
            }

            throw new InvalidOperationException(
                "Playwright failed to render the HTML document with Chromium.",
                exception);
        }
        finally
        {
            if (context is not null)
            {
                try
                {
                    await context.CloseAsync();
                }
                catch (PlaywrightException)
                {
                }
            }
        }
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
    {
        await sharedBrowserLock.WaitAsync(cancellationToken);
        try
        {
            if (sharedBrowser?.IsConnected == true)
            {
                return sharedBrowser;
            }

            if (sharedBrowser is not null)
            {
                try
                {
                    await sharedBrowser.CloseAsync();
                }
                catch (PlaywrightException)
                {
                }

                sharedBrowser = null;
            }

            try
            {
                sharedPlaywright ??= await Playwright.CreateAsync();
                sharedBrowser = await sharedPlaywright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Args =
                    [
                        "--disable-gpu",
                        "--disable-dev-shm-usage",
                        "--disable-extensions",
                        "--disable-background-networking",
                        "--disable-crash-reporter",
                        "--disable-sync",
                        "--no-first-run",
                        "--no-default-browser-check",
                        "--no-sandbox"
                    ],
                    ExecutablePath = ResolveExecutablePath(),
                    Headless = true,
                    Timeout = (float)renderTimeout.TotalMilliseconds
                });
                return sharedBrowser;
            }
            catch (PlaywrightException exception)
            {
                throw new InvalidOperationException(
                    "A Chromium-compatible executable is required to render HTML document templates. " +
                    "Configure DocumentRendering:ChromiumPath or install Chromium in the runtime image.",
                    exception);
            }
        }
        finally
        {
            sharedBrowserLock.Release();
        }
    }

    private async Task InvalidateBrowserAsync()
    {
        await sharedBrowserLock.WaitAsync();
        try
        {
            if (sharedBrowser is not null && !sharedBrowser.IsConnected)
            {
                sharedBrowser = null;
            }
        }
        finally
        {
            sharedBrowserLock.Release();
        }
    }

    private static void DisposeSharedBrowser()
    {
        sharedBrowserLock.Wait();
        try
        {
            try
            {
                sharedBrowser?.CloseAsync().GetAwaiter().GetResult();
            }
            catch (PlaywrightException)
            {
            }
            finally
            {
                sharedBrowser = null;
                sharedPlaywright?.Dispose();
                sharedPlaywright = null;
            }
        }
        finally
        {
            sharedBrowserLock.Release();
        }
    }

    private static async Task CloseContextAsync(IBrowserContext context)
    {
        try
        {
            await context.CloseAsync();
        }
        catch (PlaywrightException)
        {
        }
    }

    private string ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(configuredExecutablePath))
        {
            if (Path.IsPathRooted(configuredExecutablePath) && !File.Exists(configuredExecutablePath))
            {
                throw new InvalidOperationException(
                    $"The configured Chromium executable was not found: {configuredExecutablePath}");
            }

            return configuredExecutablePath;
        }

        var candidates = OperatingSystem.IsWindows()
            ? WindowsExecutableCandidates
            : UnixExecutableCandidates;
        var installedPath = candidates.FirstOrDefault(File.Exists);
        return installedPath ?? (OperatingSystem.IsWindows() ? "chrome" : "chromium");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
    }
}
