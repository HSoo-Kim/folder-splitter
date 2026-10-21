namespace QuadExplorer.Search;

public sealed record SearchHit(string FullPath, bool IsDirectory);

public interface IPathSearchBackend : IDisposable
{
    bool IsAvailable { get; }
    bool IsReady { get; }
    event EventHandler? IndexChanged;
    Task<IReadOnlyList<SearchHit>> QueryAsync(string text, IReadOnlyList<string> priorityRoots,
        int limit, CancellationToken cancellationToken);
}
