namespace QuadExplorer.AiSearch;

public enum ItemKind { Any, File, Folder }
public enum SearchScope { Everywhere, ActiveFolder, VisibleFolders }
public enum ChatRole { User, Assistant }
public enum AiProviderKind { LocalEndpoint, ClaudeCli, CodexCli }
public enum AiSearchMode { FileNames, ReadDocuments }
public enum DocumentReadStatus { Success, Unsupported, Failed }

public sealed record DocumentReadLimits(long MaxFileBytes = 20 * 1024 * 1024,
    int MaxCharacters = 12000, int MaxExcerpts = 12);
public sealed record DocumentExcerpt(string Text, string Locator);
public sealed record DocumentReadResult(DocumentReadStatus Status, IReadOnlyList<DocumentExcerpt> Excerpts,
    bool Truncated = false, string? Detail = null);

public interface IDocumentReader
{
    Task<DocumentReadResult> ReadAsync(string fullPath, DocumentReadLimits limits, CancellationToken token);
}

public sealed record DocumentSourceReference(string Id, string FullPath, string Locator, string Quote);
public sealed record DocumentCoverage(int Searches, int FoldersListed, int Candidates, int FilesRead,
    int FailedReads, bool Limited, IReadOnlyList<string> Notes);
public sealed record DocumentSearchResult(string Answer, IReadOnlyList<DocumentSourceReference> Sources,
    DocumentCoverage Coverage, string? Question = null);
public sealed record DocumentSearchProgress(int Searches, int FoldersListed, int Candidates, int FilesRead);

public sealed record SearchPlan(
    IReadOnlyList<string> AllTerms,
    IReadOnlyList<IReadOnlyList<string>> AnyOf,
    IReadOnlyList<string> Excluded,
    IReadOnlyList<string> Extensions,
    ItemKind Kind,
    SearchScope Scope,
    DateTime? ModifiedAfterUtc,
    DateTime? ModifiedBeforeUtc,
    long? MinBytes,
    long? MaxBytes,
    bool Refine,
    string? Clarification);

public sealed record AiHit(string FullPath, bool IsDirectory, long? Bytes, DateTime? ModifiedUtc);
public sealed record AiSearchResult(SearchPlan Plan, IReadOnlyList<AiHit> Hits, int Examined, bool Truncated, string? Note)
{
    // Preserves each filename refinement and its scope for safe bounded requery.
    internal IReadOnlyList<(SearchPlan Plan, IReadOnlyList<string> Roots)>? RefinementHistory { get; init; }
}
public sealed record ChatTurn(ChatRole Role, string Text, AiSearchResult? Result = null,
    string? ProviderLabel = null, string? Error = null, TimeSpan? Elapsed = null,
    DocumentSearchResult? DocumentResult = null, AiContextUsage? ContextUsage = null);
public sealed record AiContextUsage(long InputTokens, long? ContextWindow, string ModelId)
{
    public AiProviderKind Provider { get; init; }
}
public sealed record AiModelDescriptor(AiProviderKind Provider, string Id, string DisplayName,
    IReadOnlyList<string> ReasoningEfforts, bool Available = false, string? UnavailableReason = null,
    long? ContextWindow = null)
{
    public string? Endpoint { get; init; }
}
public sealed record ProviderProbe(bool Ok, string Detail, IReadOnlyList<string> Models, TimeSpan Elapsed)
{
    public IReadOnlyList<AiModelDescriptor> ModelDescriptors { get; init; } = [];
}

public sealed record AiSearchSettings
{
    public AiProviderKind Provider { get; init; } = AiProviderKind.LocalEndpoint;
    public AiSearchMode Mode { get; init; } = AiSearchMode.FileNames;
    public string Endpoint { get; init; } = "http://127.0.0.1:11434/v1";
    public string Model { get; init; } = "";
    // Persisted name retained for existing settings; applies to both AI search modes.
    public string FilenamePrompt { get; init; } = "";
    public string ReasoningEffort { get; init; } = "default";
    public string? CliPath { get; init; }
    public string? ClaudeCliPath { get; init; }
    public string? CodexCliPath { get; init; }
    public bool Visible { get; init; }
    public int Width { get; init; } = 420;
    public bool Floating { get; init; }
}

public interface ISearchPlanner
{
    string Label { get; }
    Task<SearchPlan> PlanAsync(IReadOnlyList<ChatTurn> conversation, SearchPlan? previous, CancellationToken token);
}

public interface IModelTransport
{
    string Label { get; }
    AiContextUsage? LastContextUsage => null;
    Task<string> CompleteAsync(string system, string conversation, CancellationToken token);
    Task<string> CompleteJsonAsync(string system, string conversation, string schema, CancellationToken token) =>
        CompleteAsync(system, conversation, token);
    Task<ProviderProbe> ProbeAsync(CancellationToken token);
}
