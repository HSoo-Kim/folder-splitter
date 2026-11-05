namespace QuadExplorer;

internal enum UpdateStage { Downloading, Verifying, Installing, Restarting }

internal readonly record struct UpdateProgress(UpdateStage Stage, long BytesReceived = 0, long? TotalBytes = null)
{
    internal int? Percentage => TotalBytes is > 0
        ? (int)Math.Clamp((decimal)BytesReceived * 100 / TotalBytes.Value, 0, 100) : null;
}
