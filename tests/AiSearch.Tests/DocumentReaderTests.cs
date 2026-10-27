using System.IO.Compression;
using System.Text;
using QuadExplorer.AiSearch;

internal static partial class Program
{
    private static async Task DocumentReaderAdapter()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_root, "reader")).FullName;
        string path = Path.Combine(folder, "report & notes.txt");
        await File.WriteAllTextAsync(path, "FIRST EVIDENCE\nSECOND EVIDENCE", Encoding.UTF8);
        var reader = new DocumentReader(Path.Combine(folder, "absent-parser"));
        var read = await reader.ReadAsync(path, new(MaxCharacters: 14), default);
        Check(read.Status == DocumentReadStatus.Success && read.Truncated);
        Check(read.Excerpts.Single().Text == "FIRST EVIDENCE");
        Check(read.Excerpts.Single().Locator == "characters 1-14");
        Check((await File.ReadAllTextAsync(path)).Contains("SECOND EVIDENCE"));
        Check((await reader.ReadAsync(path, new(MaxFileBytes: 2), default)).Status == DocumentReadStatus.Failed);
        string unsupported = Path.Combine(folder, "image.png");
        await File.WriteAllTextAsync(unsupported, "not a document");
        Check((await reader.ReadAsync(unsupported, new(), default)).Status == DocumentReadStatus.Unsupported);
        string pdf = Path.Combine(folder, "missing-engine.pdf");
        await File.WriteAllTextAsync(pdf, "%PDF-1.4");
        Check((await reader.ReadAsync(pdf, new(), default)).Status == DocumentReadStatus.Failed);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Throws<OperationCanceledException>(() => reader.ReadAsync(path, new(), cancellation.Token));
    }

    private static async Task<int> BundledDocumentReader(string parserDirectory)
    {
        string root = Directory.CreateTempSubdirectory("folder-reader-package-").FullName;
        try
        {
            var reader = new DocumentReader(parserDirectory);
            string docx = Path.Combine(root, "sample & document.docx");
            using (var zip = ZipFile.Open(docx, ZipArchiveMode.Create))
            {
                Entry(zip, "[Content_Types].xml", """
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
                    """);
                Entry(zip, "_rels/.rels", """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                    """);
                Entry(zip, "word/document.xml", """
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>RAIL STUDY: peak waiting time fell from 12 to 7 minutes.</w:t></w:r></w:p><w:p><w:r><w:t>읽기 전용 한글 문서 검증입니다.</w:t></w:r></w:p></w:body></w:document>
                    """);
            }
            byte[] original = await File.ReadAllBytesAsync(docx);
            var read = await reader.ReadAsync(docx, new(), default);
            if (read.Status != DocumentReadStatus.Success) throw new InvalidOperationException(read.Detail);
            Check(read.Status == DocumentReadStatus.Success);
            Check(read.Excerpts.Any(excerpt => excerpt.Text.Contains("12 to 7 minutes")));
            Check(read.Excerpts.Any(excerpt => excerpt.Text.Contains("한글 문서")));
            byte[] after = await File.ReadAllBytesAsync(docx);
            Check(original.SequenceEqual(after));
            string pdf = Path.Combine(root, "sample.pdf");
            var objects = new[]
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
            };
            const string content = "BT /F1 12 Tf 72 720 Td (PARSER PDF EVIDENCE 42) Tj ET";
            var body = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int> { 0 };
            foreach (string value in objects.Append($"<< /Length {content.Length} >>\nstream\n{content}\nendstream"))
            {
                offsets.Add(Encoding.ASCII.GetByteCount(body.ToString()));
                body.Append($"{offsets.Count - 1} 0 obj\n{value}\nendobj\n");
            }
            int xref = Encoding.ASCII.GetByteCount(body.ToString());
            body.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
            foreach (int offset in offsets.Skip(1)) body.Append($"{offset:D10} 00000 n \n");
            body.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            await File.WriteAllTextAsync(pdf, body.ToString(), Encoding.ASCII);
            var pdfRead = await reader.ReadAsync(pdf, new(), default);
            if (pdfRead.Status != DocumentReadStatus.Success) throw new InvalidOperationException(pdfRead.Detail);
            Check(pdfRead.Status == DocumentReadStatus.Success);
            Check(pdfRead.Excerpts.Any(excerpt => excerpt.Locator == "page 1" && excerpt.Text.Contains("EVIDENCE 42")));
            Console.WriteLine($"BUNDLED_DOCUMENT_READER_PASS assertions={_assertions}");
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Entry(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
