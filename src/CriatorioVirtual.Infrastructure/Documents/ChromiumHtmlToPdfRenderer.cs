using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CriatorioVirtual.Infrastructure.Documents;

public interface IHtmlToPdfRenderer
{
    Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken = default);
}

/// <summary>
/// Converts the embedded HTML templates through a bounded pool of long-lived Chromium workers.
/// A worker owns one browser process and one DevTools page, so concurrent requests cannot create
/// an unbounded number of Chromium processes.
/// </summary>
public sealed class ChromiumHtmlToPdfRenderer : IHtmlToPdfRenderer, IDisposable
{
    private static readonly TimeSpan BrowserStartupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReadinessPollInterval = TimeSpan.FromMilliseconds(50);

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

    private static readonly HttpClient DevToolsHttpClient = new(new SocketsHttpHandler
    {
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });

    private readonly ConcurrentDictionary<ChromiumWorker, byte> allWorkers = [];
    private readonly ConcurrentBag<ChromiumWorker> availableWorkers = [];
    private readonly SemaphoreSlim workerSlots;
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
        workerSlots = new SemaphoreSlim(options.MaxConcurrentRenders, options.MaxConcurrentRenders);
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
            await workerSlots.WaitAsync(renderCancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The HTML document rendering queue exceeded the configured timeout.");
        }

        ChromiumWorker? worker = null;
        try
        {
            worker = TakeAvailableWorker();
            if (worker is null)
            {
                worker = await ChromiumWorker.StartAsync(
                    ResolveExecutablePath(),
                    renderCancellationToken);
                allWorkers.TryAdd(worker, 0);
            }

            var content = await worker.RenderAsync(html, renderCancellationToken);
            if (Volatile.Read(ref disposed) == 0)
            {
                availableWorkers.Add(worker);
            }
            else
            {
                RemoveAndDisposeWorker(worker);
            }
            worker = null;
            return content;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Chromium exceeded the HTML document rendering timeout.");
        }
        finally
        {
            if (worker is not null)
            {
                RemoveAndDisposeWorker(worker);
            }

            workerSlots.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        while (availableWorkers.TryTake(out var worker))
        {
            RemoveAndDisposeWorker(worker);
        }

        foreach (var worker in allWorkers.Keys)
        {
            RemoveAndDisposeWorker(worker);
        }

        workerSlots.Dispose();
    }

    private ChromiumWorker? TakeAvailableWorker()
    {
        while (availableWorkers.TryTake(out var worker))
        {
            if (worker.IsHealthy)
            {
                return worker;
            }

            RemoveAndDisposeWorker(worker);
        }

        return null;
    }

    private void RemoveAndDisposeWorker(ChromiumWorker worker)
    {
        allWorkers.TryRemove(worker, out _);
        worker.Dispose();
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

    private sealed class ChromiumWorker : IDisposable
    {
        private readonly Process process;
        private readonly ClientWebSocket webSocket;
        private readonly string temporaryDirectory;
        private long commandId;
        private int disposed;
        private int healthy = 1;

        private ChromiumWorker(
            Process process,
            ClientWebSocket webSocket,
            string temporaryDirectory)
        {
            this.process = process;
            this.webSocket = webSocket;
            this.temporaryDirectory = temporaryDirectory;
        }

        public bool IsHealthy =>
            Volatile.Read(ref healthy) != 0 &&
            Volatile.Read(ref disposed) == 0 &&
            !process.HasExited &&
            webSocket.State == WebSocketState.Open;

        public static async Task<ChromiumWorker> StartAsync(
            string executablePath,
            CancellationToken cancellationToken)
        {
            var temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                $"criatorio-virtual-chromium-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryDirectory);

            Process? process = null;
            ClientWebSocket? webSocket = null;
            try
            {
                var port = GetAvailablePort();
                var startInfo = CreateStartInfo(executablePath, temporaryDirectory, port);
                process = new Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    throw new InvalidOperationException("Chromium could not be started for HTML document rendering.");
                }

                _ = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();

                var target = await WaitForPageTargetAsync(port, process, cancellationToken);
                webSocket = new ClientWebSocket();
                webSocket.Options.SetRequestHeader("Origin", "http://127.0.0.1");
                await webSocket.ConnectAsync(new Uri(target.WebSocketDebuggerUrl!), cancellationToken);

                var worker = new ChromiumWorker(process, webSocket, temporaryDirectory);
                process = null;
                webSocket = null;
                await worker.EnablePageAsync(cancellationToken);
                return worker;
            }
            catch (Win32Exception exception)
            {
                webSocket?.Dispose();
                KillProcess(process);
                process?.Dispose();
                TryDeleteTemporaryDirectory(temporaryDirectory);
                throw new InvalidOperationException(
                    "A Chromium-compatible executable is required to render HTML document templates. " +
                    "Configure DocumentRendering:ChromiumPath or install Chromium in the runtime image.",
                    exception);
            }
            catch
            {
                webSocket?.Dispose();
                KillProcess(process);
                process?.Dispose();
                TryDeleteTemporaryDirectory(temporaryDirectory);
                throw;
            }
        }

        public async Task<byte[]> RenderAsync(
            string html,
            CancellationToken cancellationToken)
        {
            var temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                $"criatorio-virtual-pdf-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryDirectory);
            var htmlPath = Path.Combine(temporaryDirectory, "document.html");

            try
            {
                await File.WriteAllTextAsync(
                    htmlPath,
                    html,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken);

                await SendCommandAsync(
                    "Page.navigate",
                    new { url = new Uri(htmlPath).AbsoluteUri },
                    cancellationToken);
                await WaitForDocumentReadyAsync(cancellationToken);

                var result = await SendCommandAsync(
                    "Page.printToPDF",
                    new
                    {
                        displayHeaderFooter = false,
                        generateTaggedPDF = false,
                        marginBottom = 0,
                        marginLeft = 0,
                        marginRight = 0,
                        marginTop = 0,
                        preferCSSPageSize = true,
                        printBackground = true
                    },
                    cancellationToken);
                var base64 = result.GetProperty("data").GetString();
                if (string.IsNullOrWhiteSpace(base64))
                {
                    throw new InvalidOperationException("Chromium produced an empty PDF document.");
                }

                var content = Convert.FromBase64String(base64);
                if (content.Length == 0)
                {
                    throw new InvalidOperationException("Chromium produced an empty PDF document.");
                }

                return content;
            }
            catch
            {
                Interlocked.Exchange(ref healthy, 0);
                throw;
            }
            finally
            {
                TryDeleteTemporaryDirectory(temporaryDirectory);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            Interlocked.Exchange(ref healthy, 0);
            webSocket.Dispose();
            KillProcess(process);
            process.Dispose();
            TryDeleteTemporaryDirectory(temporaryDirectory);
        }

        private async Task EnablePageAsync(CancellationToken cancellationToken)
        {
            await SendCommandAsync("Page.enable", null, cancellationToken);
            await SendCommandAsync("Runtime.enable", null, cancellationToken);
        }

        private async Task WaitForDocumentReadyAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var result = await SendCommandAsync(
                    "Runtime.evaluate",
                    new
                    {
                        expression = "document.readyState === 'complete' && Array.from(document.images).every(image => image.complete)",
                        returnByValue = true
                    },
                    cancellationToken);
                if (result
                    .GetProperty("result")
                    .GetProperty("value")
                    .GetBoolean())
                {
                    return;
                }

                await Task.Delay(ReadinessPollInterval, cancellationToken);
            }
        }

        private async Task<JsonElement> SendCommandAsync(
            string method,
            object? parameters,
            CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref commandId);
            var command = JsonSerializer.SerializeToUtf8Bytes(new
            {
                id,
                method,
                @params = parameters
            });
            await webSocket.SendAsync(command, WebSocketMessageType.Text, true, cancellationToken);

            while (true)
            {
                using var response = await ReceiveMessageAsync(cancellationToken);
                var root = response.RootElement;
                if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt64() != id)
                {
                    continue;
                }

                if (root.TryGetProperty("error", out var error))
                {
                    throw new InvalidOperationException(
                        $"Chromium DevTools command '{method}' failed: {error}");
                }

                return root.TryGetProperty("result", out var result)
                    ? result.Clone()
                    : throw new InvalidOperationException(
                        $"Chromium DevTools command '{method}' returned no result.");
            }
        }

        private async Task<JsonDocument> ReceiveMessageAsync(CancellationToken cancellationToken)
        {
            using var message = new MemoryStream();
            var buffer = new byte[16 * 1024];
            WebSocketReceiveResult receiveResult;
            do
            {
                receiveResult = await webSocket.ReceiveAsync(buffer, cancellationToken);
                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException("Chromium closed the DevTools connection.");
                }

                message.Write(buffer, 0, receiveResult.Count);
            }
            while (!receiveResult.EndOfMessage);

            return JsonDocument.Parse(message.ToArray());
        }

        private static ProcessStartInfo CreateStartInfo(
            string executablePath,
            string temporaryDirectory,
            int port)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            startInfo.ArgumentList.Add("--headless=new");
            startInfo.ArgumentList.Add("--disable-gpu");
            startInfo.ArgumentList.Add("--disable-dev-shm-usage");
            startInfo.ArgumentList.Add("--disable-extensions");
            startInfo.ArgumentList.Add("--disable-background-networking");
            startInfo.ArgumentList.Add("--disable-crash-reporter");
            startInfo.ArgumentList.Add("--disable-sync");
            startInfo.ArgumentList.Add("--no-first-run");
            startInfo.ArgumentList.Add("--no-default-browser-check");
            startInfo.ArgumentList.Add("--no-sandbox");
            startInfo.ArgumentList.Add("--remote-allow-origins=http://127.0.0.1");
            startInfo.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
            startInfo.ArgumentList.Add($"--remote-debugging-port={port}");
            startInfo.ArgumentList.Add($"--user-data-dir={temporaryDirectory}");
            startInfo.ArgumentList.Add("about:blank");
            return startInfo;
        }

        private static async Task<DevToolsTarget> WaitForPageTargetAsync(
            int port,
            Process process,
            CancellationToken cancellationToken)
        {
            using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupCancellation.CancelAfter(BrowserStartupTimeout);
            var endpoint = new Uri($"http://127.0.0.1:{port}/json/list");

            while (true)
            {
                startupCancellation.Token.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    throw new InvalidOperationException("Chromium exited before its DevTools endpoint became ready.");
                }

                try
                {
                    var response = await DevToolsHttpClient.GetStringAsync(
                        endpoint,
                        startupCancellation.Token);
                    var targets = JsonSerializer.Deserialize<DevToolsTarget[]>(response);
                    var page = targets?.FirstOrDefault(target =>
                        string.Equals(target.Type, "page", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl));
                    if (page is not null)
                    {
                        return page;
                    }
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Chromium did not become ready within the startup timeout.");
                }

                await Task.Delay(ReadinessPollInterval, startupCancellation.Token);
            }
        }

        private static int GetAvailablePort()
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static void KillProcess(Process? process)
        {
            if (process is null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(1_000);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static void TryDeleteTemporaryDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed record DevToolsTarget(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("webSocketDebuggerUrl")] string? WebSocketDebuggerUrl);

}
