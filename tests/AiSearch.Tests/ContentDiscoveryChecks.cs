using System.IO.Compression;
using System.Text;
using QuadExplorer.AiSearch;

// RunAsync is suitable for the normal test table; BundledAsync requires the staged parser directory.
internal static class ContentDiscoveryChecks
{
    internal static async Task RunAsync()
    {
        string root = Directory.CreateTempSubdirectory("content-discovery-").FullName;
        try
        {
            string selected = Directory.CreateDirectory(Path.Combine(root, "selected folder")).FullName;
            string nested = Directory.CreateDirectory(Path.Combine(selected, "nested")).FullName;
            string neighbor = Directory.CreateDirectory(Path.Combine(root, "neighbor")).FullName;
            string path = Path.Combine(nested, "misleading.txt");
            string body = new string('x', 14000) + "\n정확한 한국어 증거 42\n" + new string('y', 500);
            await File.WriteAllTextAsync(path, body, new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(neighbor, "outside.txt"), "정확한 한국어 증거 42");
            var reader = new DocumentReader(Path.Combine(root, "missing-parser"));
            var relevant = await reader.ReadRelevantAsync(path, new(), ["no synonym", "한국어 증거"], default);
            Require(relevant.Status == DocumentReadStatus.Success && relevant.Excerpts.Count > 0, "late text");
            var excerpt = relevant.Excerpts.First();
            Require(excerpt.Text.Contains("정확한 한국어 증거 42"), "exact quote");
            string[] location = excerpt.Locator["characters ".Length..].Split('-');
            int start = int.Parse(location[0]) - 1, end = int.Parse(location[1]);
            Require(body[start..end] == excerpt.Text && start > 12000, "exact character locator");
            var scan = await LocalContentDiscovery.ScanAsync(selected, ["한국어 증거"], reader, default);
            Require(scan.Matches.Count == 1 && scan.Matches[0].FullPath == path, "nested content only");
            Require(scan.Matches[0].Excerpts.Any(e => e.Text.Contains("한국어 증거")), "matched evidence");
            var empty = await reader.ReadRelevantAsync(path, new(), ["never present"], default);
            Require(empty.Status == DocumentReadStatus.Success && empty.Excerpts.Count == 0, "no match is not a parser error");
            var fallback = await LocalContentDiscovery.ScanAsync(selected, ["한국어 증거"], new LegacyReader(), default);
            Require(fallback.Limited && fallback.Matches.Count == 0, "legacy prefix must be marked incomplete");
            var stale = await LocalContentDiscovery.ScanAsync(Path.Combine(root, "gone"), ["x"], reader, default);
            Require(stale.Failed > 0 && stale.Limited, "stale root");
            Require((await reader.ReadRelevantAsync(Path.Combine(root, "gone.txt"), new(), ["x"], default)).Status
                == DocumentReadStatus.Failed, "stale file");
            string large = Path.Combine(selected, "too-large.txt");
            using (var file = File.Create(large)) file.SetLength(20L * 1024 * 1024 + 1);
            var oversize = await LocalContentDiscovery.ScanAsync(selected, ["x"], new LegacyReader(), default);
            Require(oversize.Failed > 0 && oversize.Limited, "file size limit applies to injected readers");
            File.Delete(large);
            var volume = await LocalContentDiscovery.ScanAsync(Path.GetPathRoot(root)!, ["x"], reader, default);
            Require(volume.Failed > 0 && volume.Limited, "volume root rejected");
            var network = await LocalContentDiscovery.ScanAsync(@"\\server\share\folder", ["x"], reader, default);
            Require(network.Failed > 0 && network.Limited, "network root rejected");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try { await LocalContentDiscovery.ScanAsync(selected, ["x"], reader, canceled.Token); throw new Exception("cancellation ignored"); }
            catch (OperationCanceledException) { }
            using var inFlight = new CancellationTokenSource();
            var waitingReader = new WaitingReader();
            Task<LocalContentScan> pending = LocalContentDiscovery.ScanAsync(selected, ["x"], waitingReader, inFlight.Token);
            await waitingReader.Started.Task;
            inFlight.Cancel();
            try { await pending; throw new Exception("in-flight cancellation ignored"); }
            catch (OperationCanceledException) { }
            string many = Directory.CreateDirectory(Path.Combine(root, "many")).FullName;
            for (int i = 0; i < 201; i++) await File.WriteAllTextAsync(Path.Combine(many, $"{i:D3}.txt"), "x");
            var countBound = await LocalContentDiscovery.ScanAsync(many, ["absent"], new LegacyReader(), default);
            Require(countBound.Limited && countBound.FilesScanned == 200 && countBound.Matches.Count == 0, "file count bound");
            var matchBound = await LocalContentDiscovery.ScanAsync(many, ["prefix"], new LegacyReader(), default);
            Require(matchBound.Limited && matchBound.FilesScanned == 64 && matchBound.Matches.Count == 64, "match count bound");
            string deep = selected;
            for (int i = 0; i < 10; i++) deep = Directory.CreateDirectory(Path.Combine(deep, "d")).FullName;
            await File.WriteAllTextAsync(Path.Combine(deep, "deep.txt"), "depth marker");
            var bounded = await LocalContentDiscovery.ScanAsync(selected, ["depth marker"], reader, default);
            Require(bounded.Limited && bounded.Matches.Count == 0, "depth bound");
            // Creating symlinks on Windows may require developer mode or administrative privileges.
            try
            {
                string link = Path.Combine(selected, "link");
                Directory.CreateSymbolicLink(link, neighbor);
                var linked = await LocalContentDiscovery.ScanAsync(selected, ["한국어 증거"], reader, default);
                Require(linked.Limited && linked.Matches.All(m => !m.FullPath.StartsWith(link, StringComparison.OrdinalIgnoreCase)), "link traversal");
                var linkRoot = await LocalContentDiscovery.ScanAsync(link, ["한국어 증거"], reader, default);
                Require(linkRoot.Failed > 0 && linkRoot.Limited, "linked root");
                string fileLink = Path.Combine(selected, "outside.txt");
                File.CreateSymbolicLink(fileLink, Path.Combine(neighbor, "outside.txt"));
                var linkedFile = await LocalContentDiscovery.ScanAsync(selected, ["한국어 증거"], reader, default);
                Require(linkedFile.Matches.All(m => m.FullPath != fileLink), "file link traversal");
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task BundledAsync(string parserDirectory)
    {
        string root = Directory.CreateTempSubdirectory("content-pdf-").FullName;
        try
        {
            string pdf = Path.Combine(root, "late.pdf");
            const int pages = 18;
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                $"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, pages).Select(i => $"{4 + i * 2} 0 R"))}] /Count {pages} >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
            };
            for (int i = 0; i < pages; i++)
            {
                int stream = 5 + i * 2;
                objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {stream} 0 R >>");
                string content = $"BT /F1 12 Tf 72 720 Td ({(i == 16 ? "LATE PDF EVIDENCE 2468" : "ordinary page")}) Tj ET";
                objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
            }
            var body = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int> { 0 };
            foreach (string value in objects)
            {
                offsets.Add(Encoding.ASCII.GetByteCount(body.ToString()));
                body.Append($"{offsets.Count - 1} 0 obj\n{value}\nendobj\n");
            }
            int xref = Encoding.ASCII.GetByteCount(body.ToString());
            body.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
            foreach (int offset in offsets.Skip(1)) body.Append($"{offset:D10} 00000 n \n");
            body.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            await File.WriteAllTextAsync(pdf, body.ToString(), Encoding.ASCII);
            var reader = new DocumentReader(parserDirectory);
            var result = await reader.ReadRelevantAsync(pdf, new(), ["EVIDENCE 2468"], default);
            Require(result.Status == DocumentReadStatus.Success, result.Detail ?? "PDF reader failed");
            Require(result.Excerpts.Any(e => e.Locator.StartsWith("page 17,") && e.Text.Contains("LATE PDF EVIDENCE 2468")), "late PDF page and quote");
            var scan = await LocalContentDiscovery.ScanAsync(root, ["EVIDENCE 2468"], reader, default);
            Require(scan.Matches.Count == 1 && scan.Matches[0].Excerpts.Any(e => e.Locator.StartsWith("page 17,")), "late PDF discovery");
            string docx = Path.Combine(root, "late.docx");
            using (var zip = ZipFile.Open(docx, ZipArchiveMode.Create))
            {
                AddEntry(zip, "[Content_Types].xml", """
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
                    """);
                AddEntry(zip, "_rels/.rels", """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                    """);
                AddEntry(zip, "word/document.xml",
                    $"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>{new string('a', 14000)}</w:t></w:r></w:p><w:p><w:r><w:t>LATE DOCX EVIDENCE 1357</w:t></w:r></w:p></w:body></w:document>");
            }
            var document = await reader.ReadRelevantAsync(docx, new(), ["DOCX EVIDENCE 1357"], default);
            Require(document.Status == DocumentReadStatus.Success, document.Detail ?? "DOCX reader failed");
            Require(document.Excerpts.Any(e => e.Text.Contains("LATE DOCX EVIDENCE 1357") && e.Locator.Contains("characters")), "late DOCX evidence");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void AddEntry(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Content discovery: " + message);
    }

    private sealed class LegacyReader : IDocumentReader
    {
        public Task<DocumentReadResult> ReadAsync(string path, DocumentReadLimits limits, CancellationToken token) =>
            Task.FromResult(new DocumentReadResult(DocumentReadStatus.Success, [new("prefix only", "characters 1-11")], true));
    }

    private sealed class WaitingReader : IDocumentReader
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<DocumentReadResult> ReadAsync(string path, DocumentReadLimits limits, CancellationToken token)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable");
        }
    }
}
