using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace QuadExplorer.AiSearch;

/// <summary>Reads bounded excerpts using only the app's bundled, offline parser.</summary>
public sealed class DocumentReader(string? parserDirectory = null) : IDocumentReader
{
    private readonly string parserRoot = parserDirectory ?? Path.Combine(AppContext.BaseDirectory, "parsers");
    private static readonly HashSet<string> TextFormats = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".csv", ".log" };
    private static readonly HashSet<string> DocumentFormats = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".hwp", ".hwpx", ".hml", ".docx", ".pptx", ".xlsx", ".xls" };

    public async Task<DocumentReadResult> ReadAsync(string fullPath, DocumentReadLimits limits, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (limits.MaxCharacters is < 1 or > 100000 || limits.MaxExcerpts is < 1 or > 50
            || limits.MaxFileBytes is < 1 or > 100 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(limits));
        try
        {
            var file = new FileInfo(fullPath);
            if (!file.Exists) return Failed("The file no longer exists.");
            if (file.Length > limits.MaxFileBytes) return Failed("The file exceeds the document-reading size limit.");
            if (TextFormats.Contains(file.Extension))
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var buffer = new char[limits.MaxCharacters + 1];
                int count = await reader.ReadBlockAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                string text = new(buffer, 0, Math.Min(count, limits.MaxCharacters));
                if (string.IsNullOrWhiteSpace(text)) return Failed("The file contains no readable text.");
                return new(DocumentReadStatus.Success,
                    [new(text, $"characters 1-{text.Length}")], count > limits.MaxCharacters);
            }
            if (!DocumentFormats.Contains(file.Extension))
                return new(DocumentReadStatus.Unsupported, [], Detail: "This file format is not supported by the bundled reader.");
            return await ParseAsync(file.FullName, file.Extension, limits, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or Win32Exception or TimeoutException)
        {
            return Failed(error.Message);
        }
    }

    private async Task<DocumentReadResult> ParseAsync(string path, string extension,
        DocumentReadLimits limits, CancellationToken token)
    {
        string node = Path.Combine(parserRoot, "node.exe");
        string cli = Path.Combine(parserRoot, "node_modules", "kordoc", "dist", "cli.js");
        if (!File.Exists(node) || !File.Exists(cli))
            return Failed("The bundled document reader is missing. Reinstall the complete Folder Splitter package.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        using var process = new Process
        {
            StartInfo = new(node)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = parserRoot,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            }
        };
        foreach (string argument in new[] { "--max-old-space-size=256", cli, "parse-worker", "--protocol", "2",
            "--max-request-bytes", "65536", "--max-response-bytes", "2097152" })
            process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["KORDOC_OFFLINE"] = "1";
        process.StartInfo.Environment.Remove("NODE_OPTIONS");
        process.StartInfo.Environment.Remove("NODE_PATH");
        process.StartInfo.Environment.Remove("NODE_COMPILE_CACHE");
        process.Start();
        using var cancellation = deadline.Token.Register(() => Kill(process));
        Task stderr = DrainAsync(process.StandardError, deadline.Token);
        try
        {
            using var ready = await ReadFrameAsync(process.StandardOutput, deadline.Token).ConfigureAwait(false);
            if (!ready.RootElement.TryGetProperty("ready", out var isReady) || isReady.ValueKind != JsonValueKind.True
                || ready.RootElement.GetProperty("protocol").GetInt32() != 2
                || !ready.RootElement.GetProperty("capabilities").EnumerateArray().Any(value => value.GetString() == "parse"))
                return Failed("The bundled document reader returned an incompatible protocol.");
            string request = JsonSerializer.Serialize(new
            {
                id = 1, cmd = "parse", file = path,
                options = new { pages = $"1-{limits.MaxExcerpts}", images = false, ocr = false,
                    formulaOcr = false, plain = true }
            });
            await process.StandardInput.WriteLineAsync(request.AsMemory(), deadline.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(deadline.Token).ConfigureAwait(false);
            using var response = await ReadFrameAsync(process.StandardOutput, deadline.Token).ConfigureAwait(false);
            if (!response.RootElement.TryGetProperty("result", out var result))
                return Failed("The document reader rejected the request or its output exceeded the size limit.");
            if (!result.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
                return Failed(result.TryGetProperty("error", out var error)
                    ? error.GetString() ?? "Document parsing failed." : "Document parsing failed.");

            var excerpts = new List<DocumentExcerpt>();
            int remaining = limits.MaxCharacters;
            bool truncated = result.TryGetProperty("pageCount", out var pagesCount)
                && pagesCount.TryGetInt32(out int totalPages) && totalPages > limits.MaxExcerpts;
            bool section = result.TryGetProperty("metadata", out var metadata)
                && metadata.TryGetProperty("pageMode", out var mode) && mode.GetString() == "section";
            void Add(string value, string locator)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                if (remaining == 0 || excerpts.Count >= limits.MaxExcerpts) { truncated = true; return; }
                int length = Math.Min(value.Length, remaining);
                excerpts.Add(new(value[..length], locator));
                remaining -= length;
                truncated |= length < value.Length;
            }
            if (result.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
            {
                foreach (var page in pages.EnumerateArray())
                {
                    int number = page.GetProperty("pageNumber").GetInt32();
                    string label = extension.ToLowerInvariant() switch
                    {
                        ".pptx" => "slide", ".xlsx" or ".xls" => "sheet",
                        ".hwp" or ".hwpx" or ".hml" when section => "section (approximate)",
                        _ => "page"
                    };
                    Add(page.GetProperty("markdown").GetString() ?? "", $"{label} {number}");
                }
            }
            if (excerpts.Count == 0 && result.TryGetProperty("markdown", out var markdown))
                Add(markdown.GetString() ?? "", "document text (no page mapping)");
            if (excerpts.Count == 0)
                return Failed("No text layer was found. Scanned images require OCR, which is not enabled.");
            var warnings = result.TryGetProperty("warnings", out var warningList) && warningList.ValueKind == JsonValueKind.Array
                ? warningList.EnumerateArray().Take(8).Select(warning =>
                    warning.TryGetProperty("message", out var message) ? message.GetString() : null)
                    .Where(message => !string.IsNullOrWhiteSpace(message)).ToArray() : [];
            return new(DocumentReadStatus.Success, excerpts, truncated,
                warnings.Length > 0 ? string.Join("; ", warnings)[..Math.Min(1000, string.Join("; ", warnings).Length)] : null);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Document reading exceeded 45 seconds.");
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await stderr.ConfigureAwait(false); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
    }

    private static async Task<JsonDocument> ReadFrameAsync(StreamReader reader, CancellationToken token)
    {
        string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
        if (line is null || line.Length > 2097152)
            throw new InvalidDataException("The document reader returned an empty or oversized frame.");
        return JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 100 });
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false) != 0) { }
    }

    private static DocumentReadResult Failed(string detail) => new(DocumentReadStatus.Failed, [], Detail: detail);

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }
}
