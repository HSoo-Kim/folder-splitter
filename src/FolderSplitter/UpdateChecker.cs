using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace QuadExplorer;

public sealed record ReleaseInfo(Version Version, string Tag, string Url, Uri? PackageUrl = null, string? PackageSha256 = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Summaries = null);


internal static class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/HSoo-Kim/folder-splitter/releases/latest";
    internal const string PackageName = "folder-splitter-win-x64.zip";

    public static Version Current => Normalize(typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0));

    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    // Reads only the latest release's tag, page, body and package digest; nothing about the user or their files is sent.
    public static async Task<ReleaseInfo?> FetchLatestAsync(CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FolderSplitter/" + Current);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        await using var stream = await http.GetStreamAsync(LatestReleaseApi, token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        return Parse(json.RootElement);
    }

    internal static ReleaseInfo? Parse(JsonElement release)
    {
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        string url = release.GetProperty("html_url").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || !url.StartsWith("https://github.com/", StringComparison.Ordinal))
            return null;
        Uri? package = null;
        string? sha256 = null;
        if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != PackageName) continue;
                string download = asset.TryGetProperty("browser_download_url", out var link) ? link.GetString() ?? "" : "";
                string digest = asset.TryGetProperty("digest", out var hash) && hash.ValueKind == JsonValueKind.String ? hash.GetString() ?? "" : "";
                if (download.StartsWith("https://github.com/", StringComparison.Ordinal)
                    && digest.StartsWith("sha256:", StringComparison.Ordinal) && digest.Length == 7 + 64)
                {
                    package = new Uri(download);
                    sha256 = digest[7..];
                }
                break;
            }
        IReadOnlyDictionary<string, IReadOnlyList<string>>? summaries = null;
        if (release.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String)
        {
            try { summaries = UpdateSummary.Parse(body.GetString()); }
            catch (Exception error) when (error is InvalidOperationException or JsonException or ArgumentException) { }
        }
        return new ReleaseInfo(Normalize(version), tag, url, package, sha256, summaries);
    }
}

// Replaces the running executable with the one in a verified release package. Windows lets a running
// executable be renamed, so the current file moves aside to "<exe>.old" and is deleted on the next start.
internal static class UpdateInstaller
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FolderSplitter/" + UpdateChecker.Current);
        return http;
    }

    public static Task<Stream> DownloadAsync(Uri url, CancellationToken token) => DownloadAsync(url, token, null);

    public static async Task<Stream> DownloadAsync(Uri url, CancellationToken token, IProgress<UpdateProgress>? progress)
    {
        var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            return new DownloadStream(stream, response, progress);
        }
        catch { response.Dispose(); throw; }
    }

    // The response owns the HTTP connection; its content length remains available even for a non-seekable stream.
    private sealed class DownloadStream(Stream source, HttpResponseMessage response, IProgress<UpdateProgress>? progress) : Stream
    {
        internal long? TotalBytes => response.Content.Headers.ContentLength;
        private long received;
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => TotalBytes ?? throw new NotSupportedException();
        public override long Position { get => received; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            received += count;
            progress?.Report(new(UpdateStage.Downloading, received, TotalBytes));
            return count;
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            received += read;
            progress?.Report(new(UpdateStage.Downloading, received, TotalBytes));
            return read;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { source.Dispose(); response.Dispose(); }
            base.Dispose(disposing);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public static string? InstallablePath()
    {
        string? exe = Environment.ProcessPath;
        if (exe is null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return null;
        string probe = Path.Combine(Path.GetDirectoryName(exe)!, ".folder-splitter-write-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return exe;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    public static void RemovePrevious(string? exe)
    {
        if (exe is null) return;
        try { File.Delete(exe + ".old"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public static async Task InstallAsync(ReleaseInfo release, string exe,
        Func<Uri, CancellationToken, Task<Stream>> download, CancellationToken token, IProgress<UpdateProgress>? progress = null)
    {
        if (release.PackageUrl is null || release.PackageSha256 is null)
            throw new InvalidDataException("The release has no verifiable Windows package.");
        string package = exe + ".download", staged = exe + ".new", previous = exe + ".old";
        try
        {
            progress?.Report(new(UpdateStage.Downloading));
            await using (var source = await download(release.PackageUrl, token).ConfigureAwait(false))
            await using (var file = File.Create(package))
            {
                long? total = source is DownloadStream http ? http.TotalBytes : source.CanSeek ? source.Length : null;
                long received = 0;
                progress?.Report(new(UpdateStage.Downloading, received, total));
                byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
                try
                {
                    int count;
                    while ((count = await source.ReadAsync(buffer.AsMemory(0, 81920), token).ConfigureAwait(false)) != 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        received += count;
                        progress?.Report(new(UpdateStage.Downloading, received, total));
                    }
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }
            progress?.Report(new(UpdateStage.Verifying));
            string actual;
            await using (var file = File.OpenRead(package))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
            if (!actual.Equals(release.PackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded package failed its SHA-256 check.");
            using (var zip = ZipFile.OpenRead(package))
            {
                ZipArchiveEntry? executable = null;
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.Equals("FolderSplitter.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (executable is not null)
                        throw new InvalidDataException("The package contains more than one FolderSplitter.exe.");
                    executable = entry;
                }
                if (executable is null)
                    throw new InvalidDataException("The package does not contain FolderSplitter.exe.");
                executable.ExtractToFile(staged, overwrite: true);
            }
            ValidateExecutable(staged, release.Version);
            progress?.Report(new(UpdateStage.Installing));
            File.Move(exe, previous, overwrite: true);
            try { File.Move(staged, exe); }
            catch
            {
                File.Move(previous, exe);
                throw;
            }
        }
        finally
        {
            TryDelete(package);
            TryDelete(staged);
        }
    }

    private static void ValidateExecutable(string path, Version expected)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            var headers = reader.PEHeaders;
            if (headers.CoffHeader.Machine != Machine.Amd64
                || (headers.CoffHeader.Characteristics & Characteristics.Dll) != 0
                || headers.PEHeader is not { AddressOfEntryPoint: > 0 })
                throw new InvalidDataException("The package does not contain a valid Windows x64 application.");
        }
        catch (BadImageFormatException error)
        {
            throw new InvalidDataException("The package does not contain a valid Windows x64 application.", error);
        }

        var info = FileVersionInfo.GetVersionInfo(path);
        if (!string.Equals(info.ProductName, "Folder Splitter", StringComparison.Ordinal)
            || info.FileMajorPart != expected.Major || info.FileMinorPart != expected.Minor
            || info.FileBuildPart != expected.Build)
            throw new InvalidDataException("The application's identity or version does not match the release.");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
