using System.Security.Cryptography;
using System.Text;

namespace QuadExplorer.Search;

internal static class IndexStore
{
    private const int Magic = 0x58495146;
    private const int Version = 2; // 2: entries stored as one packed string per folder
    private const int DigestLength = 32;

    internal static void Save(string path, string[] configuredRoots, FileIndex index,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 65536, FileOptions.SequentialScan))
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    writer.Write(Magic);
                    writer.Write(Version);
                    writer.Write(index.BuiltUtc.Ticks);
                    WriteStrings(writer, configuredRoots);
                    WriteStrings(writer, index.Roots);
                    WriteStrings(writer, index.DeniedPaths);
                    writer.Write(index.Directories.Length);
                    foreach (var directory in index.Directories)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        writer.Write(directory.Path);
                        writer.Write(directory.Count);
                        writer.Write(directory.Packed);
                    }
                }
                var length = stream.Position;
                stream.Position = 0;
                var digest = Digest(stream, length, cancellationToken);
                stream.Write(digest);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    internal static FileIndex? Load(string path, string[] configuredRoots, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 65536, FileOptions.SequentialScan);
            if (stream.Length < 16 + DigestLength)
                return null;
            var payloadLength = stream.Length - DigestLength;
            var computed = Digest(stream, payloadLength, cancellationToken);
            Span<byte> expected = stackalloc byte[DigestLength];
            stream.ReadExactly(expected);
            if (!CryptographicOperations.FixedTimeEquals(computed, expected))
                return null;
            stream.Position = 0;
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version)
                return null;
            var built = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            if (!ReadStrings(reader).SequenceEqual(configuredRoots, StringComparer.OrdinalIgnoreCase))
                return null;
            var roots = ReadStrings(reader);
            var denied = ReadStrings(reader);
            var count = ReadCount(reader);
            var directories = new IndexDirectory[count];
            for (var i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = reader.ReadString();
                var entryCount = ReadCount(reader);
                var packed = reader.ReadString();
                if (!IndexDirectory.IsWellFormed(packed, entryCount))
                    return null;
                directories[i] = new IndexDirectory(directory, packed, entryCount);
            }
            return stream.Position == payloadLength ? new FileIndex(roots, directories, denied, built) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
        catch (FormatException) { return null; }
    }

    private static byte[] Digest(Stream stream, long length, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        while (length > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read == 0)
                throw new EndOfStreamException();
            hash.AppendData(buffer, 0, read);
            length -= read;
        }
        return hash.GetHashAndReset();
    }

    private static int ReadCount(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid snapshot count.");
        return count;
    }

    private static string[] ReadStrings(BinaryReader reader)
    {
        var result = new string[ReadCount(reader)];
        for (var i = 0; i < result.Length; i++)
            result[i] = reader.ReadString();
        return result;
    }

    private static void WriteStrings(BinaryWriter writer, string[] values)
    {
        writer.Write(values.Length);
        foreach (var value in values)
            writer.Write(value);
    }
}
