using QuadExplorer;

internal static class UpdateProgressTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases =>
    [
        ("update percentages use received bytes and known Content-Length", KnownLength),
        ("unknown and empty update Content-Length remain indeterminate", UnknownLength),
        ("update percentages clamp and handle large byte counts", Bounds)
    ];

    private static void KnownLength()
    {
        for (int percent = 0; percent <= 100; percent++)
            Expect(new(UpdateStage.Downloading, percent, 100), percent);
        Expect(new(UpdateStage.Downloading, 1, 3), 33);
        Expect(new(UpdateStage.Downloading, 2, 3), 66);
    }

    private static void UnknownLength()
    {
        Expect(new(UpdateStage.Downloading), null);
        Expect(new(UpdateStage.Downloading, 12345), null);
        Expect(new(UpdateStage.Downloading, 0, 0), null);
        Expect(new(UpdateStage.Downloading, 123, -1), null);
    }

    private static void Bounds()
    {
        Expect(new(UpdateStage.Downloading, -1, 100), 0);
        Expect(new(UpdateStage.Downloading, 101, 100), 100);
        Expect(new(UpdateStage.Downloading, long.MaxValue, long.MaxValue), 100);
        Expect(new(UpdateStage.Downloading, long.MaxValue / 2, long.MaxValue), 49);
    }

    private static void Expect(UpdateProgress progress, int? expected)
    {
        if (progress.Percentage != expected)
            throw new InvalidOperationException($"{progress.BytesReceived}/{progress.TotalBytes}: expected {expected}, got {progress.Percentage}");
    }
}
