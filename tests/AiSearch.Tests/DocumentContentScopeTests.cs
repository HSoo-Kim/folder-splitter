using QuadExplorer.AiSearch;

internal static partial class Program
{
    private static async Task DocumentContentScope()
    {
        string root = Folder("content-scope");
        string child = Directory.CreateDirectory(Path.Combine(root, "scope-child")).FullName;
        string grandchild = Directory.CreateDirectory(Path.Combine(child, "nested")).FullName;
        string inside = TextFile(grandchild, "record.txt", "The budget is 120 dollars.");
        string outside = Directory.CreateDirectory(root + "-sibling").FullName;
        string outsideFile = TextFile(outside, "private.txt", "The budget is 250 dollars.");
        using var backend = new Backend([child, outside]);

        var nestedSearch = new ActionTransport((context, step) => step switch
        {
            0 => ListAction(context, root),
            1 => ListAction(context, child),
            2 => ActionJson(new { action = "search_content", targetId = CandidateId(context, grandchild), terms = new[] { "budget" } }),
            3 => ReadAction(context, inside),
            _ => AnswerAction(context, "The budget is 120 dollars.")
        });
        var nested = await new DocumentSearch(backend, nestedSearch, new DocumentReader())
            .RunAsync([new(ChatRole.User, "Find the budget")], [root], default);
        Check(nested.Question is null && nested.Sources.Single().FullPath == inside && nested.Coverage.Searches == 1);

        var indexedSearch = new ActionTransport((context, step) => step switch
        {
            0 => ActionJson(new { action = "search", query = "scope-child" }),
            1 => ActionJson(new { action = "search_content", targetId = CandidateId(context, child), terms = new[] { "budget" } }),
            2 => ReadAction(context, inside),
            _ => AnswerAction(context, "The budget is 120 dollars.")
        });
        var indexed = await new DocumentSearch(backend, indexedSearch, new DocumentReader())
            .RunAsync([new(ChatRole.User, "Find the budget")], [root], default);
        Check(indexed.Question is null && indexed.Sources.Single().FullPath == inside);

        var outsideReader = new FixtureReader();
        var outsideSearch = new ActionTransport((context, step) => step == 0
            ? ActionJson(new { action = "search", query = Path.GetFileName(outside) })
            : ActionJson(new { action = "search_content", targetId = CandidateId(context, outside), terms = new[] { "budget" } }));
        var denied = await new DocumentSearch(backend, outsideSearch, outsideReader)
            .RunAsync([new(ChatRole.User, "Find the budget")], [root], default);
        Check(denied.Question == DocumentSearch.OutsideContentScopeMessage + Environment.NewLine + outside && denied.Sources.Count == 0
            && outsideReader.Paths.Count == 0);

        var batchReader = new FixtureReader();
        var outsideBatch = new ActionTransport((context, step) => step == 0
            ? ActionJson(new { action = "search", query = Path.GetFileName(outside) })
            : ActionJson(new { action = "batch", actions = new[] {
                new { action = "search_content", targetId = CandidateId(context, root), terms = new[] { "budget" } },
                new { action = "search_content", targetId = CandidateId(context, outside), terms = new[] { "budget" } } } }));
        var rejectedBatch = await new DocumentSearch(backend, outsideBatch, batchReader)
            .RunAsync([new(ChatRole.User, "Find the budget")], [root], default);
        Check(rejectedBatch.Question == DocumentSearch.OutsideContentScopeMessage + Environment.NewLine + outside && batchReader.Paths.Count == 0
            && rejectedBatch.Coverage.Searches == 1);

        var explicitSearch = new ActionTransport((context, step) => step switch
        {
            0 => ActionJson(new { action = "search_content", targetId = CandidateId(context, outside), terms = new[] { "budget" } }),
            1 => ReadAction(context, outsideFile),
            _ => AnswerAction(context, "The budget is 250 dollars.")
        });
        var explicitResult = await new DocumentSearch(backend, explicitSearch, new DocumentReader())
            .RunAsync([new(ChatRole.User, $"Search this folder: \"{outside}\"")], [root], default);
        Check(explicitResult.Question is null && explicitResult.Sources.Single().FullPath == outsideFile);
    }
}
