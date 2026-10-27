using System.Diagnostics;
using QuadExplorer.Search;

namespace QuadExplorer.AiSearch;

public sealed class AiSearchSession : IDisposable
{
    private readonly object _gate = new();
    private readonly ConversationalSearch _search;
    private readonly IPathSearchBackend _backend;
    private readonly ISearchPlanner? _injectedPlanner;
    private readonly IDocumentReader? _injectedReader;
    private readonly IModelTransport? _injectedTransport;
    private readonly List<ChatTurn> _turns = [];
    private AiSearchSettings _settings;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _disposed;
    private AiSearchResult? _previous;

    public AiSearchSession(IPathSearchBackend backend, AiSearchSettings settings, ISearchPlanner? planner = null,
        IDocumentReader? documentReader = null, IModelTransport? transport = null)
    {
        _backend = backend;
        _search = new ConversationalSearch(backend);
        _settings = AiSearchSettingsStore.Normalize(settings);
        _injectedPlanner = planner;
        _injectedReader = documentReader;
        _injectedTransport = transport;
    }

    public event EventHandler<DocumentSearchProgress>? DocumentProgress;

    public AiSearchSettings Settings
    {
        get { lock (_gate) return _settings; }
        set
        {
            var normalized = AiSearchSettingsStore.Normalize(value);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_settings.Provider != normalized.Provider || _settings.Endpoint != normalized.Endpoint
                    || _settings.Model != normalized.Model || _settings.CliPath != normalized.CliPath
                    || _settings.ClaudeCliPath != normalized.ClaudeCliPath || _settings.CodexCliPath != normalized.CodexCliPath
                    || _settings.ReasoningEffort != normalized.ReasoningEffort || _settings.Mode != normalized.Mode
                    || _settings.FilenamePrompt != normalized.FilenamePrompt)
                    CancelCore();
                if (_settings.Mode != normalized.Mode)
                {
                    // A mode switch starts a separate conversation. Never pass document evidence or
                    // document questions into filename refinement (or filename plans into reading).
                    _turns.Clear();
                    _previous = null;
                }
                _settings = normalized;
            }
        }
    }

    public IReadOnlyList<ChatTurn> Turns
    {
        get { lock (_gate) return _turns.ToArray(); }
    }

    public async Task<ChatTurn?> SubmitAsync(string text, IReadOnlyList<string> roots)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (text.Length > 4000)
            throw new ArgumentException("Search messages are limited to 4000 characters.", nameof(text));
        CancellationTokenSource pending;
        long generation;
        AiSearchSettings settings;
        ChatTurn[] conversation;
        AiSearchResult? previous;
        var capturedRoots = roots.Take(64).ToArray();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancelCore();
            pending = new CancellationTokenSource();
            _pending = pending;
            generation = ++_generation;
            settings = _settings;
            previous = _previous;
            _turns.Add(new ChatTurn(ChatRole.User, text.Trim()));
            if (_turns.Count > 40)
                _turns.RemoveRange(0, _turns.Count - 39);
            conversation = _turns.ToArray();
        }
        var clock = Stopwatch.StartNew();
        IModelTransport? transport = null;
        var label = _injectedPlanner?.Label ?? settings.Provider.ToString();
        try
        {
            ChatTurn turn;
            if (settings.Mode == AiSearchMode.ReadDocuments)
            {
                transport = _injectedTransport ?? CreateTransport(settings);
                label = transport.Label;
                var documents = new DocumentSearch(_backend, transport, _injectedReader ?? new DocumentReader(),
                    settings.FilenamePrompt);
                var result = await documents.RunAsync(conversation, capturedRoots, pending.Token, progress =>
                {
                    lock (_gate)
                        if (!_disposed && _generation == generation && !pending.IsCancellationRequested)
                            DocumentProgress?.Invoke(this, progress);
                }).ConfigureAwait(false);
                turn = new ChatTurn(ChatRole.Assistant, result.Question ?? result.Answer,
                    ProviderLabel: label, Elapsed: clock.Elapsed, DocumentResult: result);
            }
            else
            {
                var planner = _injectedPlanner;
                if (planner is null)
                {
                    transport = _injectedTransport ?? CreateTransport(settings);
                    planner = new ModelSearchPlanner(transport, settings.FilenamePrompt);
                }
                label = planner.Label;
                var plan = await planner.PlanAsync(conversation, previous?.Plan, pending.Token).ConfigureAwait(false);
                pending.Token.ThrowIfCancellationRequested();
                var result = await _search.RunAsync(plan, previous, capturedRoots, pending.Token).ConfigureAwait(false);
                var message = result.Plan.Clarification ?? (result.Hits.Count == 0 ? "No matching files found."
                    : $"Found {result.Hits.Count} matching items.");
                turn = new ChatTurn(ChatRole.Assistant, message, result, label, Elapsed: clock.Elapsed);
            }
            turn = turn with { ContextUsage = transport?.LastContextUsage };
            pending.Token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_disposed || _generation != generation || pending.IsCancellationRequested)
                    return null;
                _turns.Add(turn);
                if (turn.Result is { Plan.Clarification: null } result)
                    _previous = result;
                _pending = null;
                return turn;
            }
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (_disposed || _generation != generation || pending.IsCancellationRequested)
                    return null;
                var turn = new ChatTurn(ChatRole.Assistant, "Search could not complete.", ProviderLabel: label,
                    Error: error.Message, Elapsed: clock.Elapsed, ContextUsage: transport?.LastContextUsage);
                _turns.Add(turn);
                _pending = null;
                return turn;
            }
        }
        finally
        {
            if (!ReferenceEquals(transport, _injectedTransport))
                (transport as IDisposable)?.Dispose();
            pending.Dispose();
        }
    }

    public void Cancel()
    {
        lock (_gate)
            CancelCore();
    }

    private void CancelCore()
    {
        _generation++;
        if (_pending is null)
            return;
        var pending = _pending;
        _pending = null;
        // A cancelled request has no assistant answer. Remove its unmatched user turn so
        // subsequent context cannot mistake it for a completed search/refinement.
        if (_turns.Count > 0 && _turns[^1].Role == ChatRole.User)
            _turns.RemoveAt(_turns.Count - 1);
        pending.Cancel();
    }

    public void Reset()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancelCore();
            _turns.Clear();
            _previous = null;
        }
    }

    public static async Task<ProviderProbe> ProbeAsync(AiSearchSettings settings, CancellationToken token)
        => await ProbeCoreAsync(settings, false, token).ConfigureAwait(false);

    /// <summary>Lists provider metadata only; never submits an inference request.</summary>
    public static async Task<ProviderProbe> ListModelsAsync(AiSearchSettings settings, CancellationToken token)
        => await ProbeCoreAsync(settings, true, token).ConfigureAwait(false);

    private static async Task<ProviderProbe> ProbeCoreAsync(AiSearchSettings settings, bool modelsOnly, CancellationToken token)
    {
        IModelTransport? transport = null;
        var clock = Stopwatch.StartNew();
        try
        {
            token.ThrowIfCancellationRequested();
            transport = CreateTransport(AiSearchSettingsStore.Normalize(settings));
            return modelsOnly && transport is CliTransport cli
                ? await cli.ListModelsAsync(token).ConfigureAwait(false)
                : await transport.ProbeAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or InvalidOperationException
            or NotSupportedException or UnauthorizedAccessException)
        {
            return new ProviderProbe(false, error.Message, [], clock.Elapsed);
        }
        finally { (transport as IDisposable)?.Dispose(); }
    }

    private static IModelTransport CreateTransport(AiSearchSettings settings) =>
        settings.Provider == AiProviderKind.LocalEndpoint
            ? new LocalEndpointTransport(settings) : new CliTransport(settings);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            CancelCore();
            _turns.Clear();
            _previous = null;
        }
    }
}
