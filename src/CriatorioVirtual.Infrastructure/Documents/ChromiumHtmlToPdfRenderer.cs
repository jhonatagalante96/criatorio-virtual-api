using System.Diagnostics;
using System.ComponentModel;
using System.Text;

namespace CriatorioVirtual.Infrastructure.Documents;

public interface IHtmlToPdfRenderer
{
    Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken = default);
}

public sealed class ChromiumHtmlToPdfRenderer : IHtmlToPdfRenderer
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(30);

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

    private readonly string? configuredExecutablePath;

    public ChromiumHtmlToPdfRenderer()
        : this(
            Environment.GetEnvironmentVariable("DocumentRendering__ChromiumPath") ??
            Environment.GetEnvironmentVariable("DOCUMENT_RENDERING_CHROMIUM_PATH"))
    {
    }

    public ChromiumHtmlToPdfRenderer(string? executablePath)
    {
        configuredExecutablePath = string.IsNullOrWhiteSpace(executablePath)
            ? null
            : executablePath.Trim();
    }

    public async Task<byte[]> RenderAsync(
        string html,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        cancellationToken.ThrowIfCancellationRequested();

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"criatorio-virtual-pdf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        var htmlPath = Path.Combine(temporaryDirectory, "document.html");
        var pdfPath = Path.Combine(temporaryDirectory, "document.pdf");
        using var renderCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        renderCancellation.CancelAfter(RenderTimeout);
        var renderCancellationToken = renderCancellation.Token;
        await File.WriteAllTextAsync(
            htmlPath,
            html,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            renderCancellationToken);

        Process? process = null;
        try
        {
            var startInfo = CreateStartInfo(htmlPath, pdfPath);
            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException("Chromium could not be started for HTML document rendering.");
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(renderCancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(renderCancellationToken);
            try
            {
                await process.WaitForExitAsync(renderCancellationToken);
            }
            catch (OperationCanceledException)
            {
                KillProcess(process);
                if (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Chromium exceeded the HTML document rendering timeout.");
                }

                throw;
            }

            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Chromium failed to render the HTML document (exit code {process.ExitCode}). " +
                    $"{TrimProcessOutput(standardError, standardOutput)}");
            }

            if (!File.Exists(pdfPath))
            {
                throw new InvalidOperationException("Chromium completed without producing a PDF document.");
            }

            var content = await File.ReadAllBytesAsync(pdfPath, renderCancellationToken);
            if (content.Length == 0)
            {
                throw new InvalidOperationException("Chromium produced an empty PDF document.");
            }

            return content;
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "A Chromium-compatible executable is required to render HTML document templates. " +
                "Configure DocumentRendering:ChromiumPath or install Chromium in the runtime image.",
                exception);
        }
        finally
        {
            process?.Dispose();
            TryDeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    private ProcessStartInfo CreateStartInfo(string htmlPath, string pdfPath)
    {
        var executablePath = ResolveExecutablePath();
        var userDataDirectory = Path.Combine(Path.GetDirectoryName(pdfPath)!, "browser-profile");
        var fileUri = new Uri(htmlPath).AbsoluteUri;
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
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        startInfo.ArgumentList.Add("--no-pdf-header-footer");
        startInfo.ArgumentList.Add("--no-sandbox");
        startInfo.ArgumentList.Add($"--user-data-dir={userDataDirectory}");
        startInfo.ArgumentList.Add($"--print-to-pdf={pdfPath}");
        startInfo.ArgumentList.Add(fileUri);
        return startInfo;
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

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string TrimProcessOutput(string standardError, string standardOutput)
    {
        var output = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
        return string.IsNullOrWhiteSpace(output)
            ? string.Empty
            : output.Trim().Length <= 500
                ? output.Trim()
                : output.Trim()[..500];
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
