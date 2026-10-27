using QuadExplorer.AiSearch;

internal static partial class Program
{
    private static async Task CitationFormatting()
    {
        // Document text as extracted: CRLF, aligned spaces, a typographic dash and curly quotes.
        const string minutes = "2026년 3분기 운영 회의록\r\n\r\n1. 예산:  총 사업비는 1,200,000원이며,\r\n   이 중 \"장비 구입비\"는 450,000원 (37.5%) 입니다.\r\n2. 일정 ― 참여 기관은 ‘한국대학교’, 도시안전센터이다.\r\n";
        string exactBudget = "총 사업비는 1,200,000원이며,\r\n   이 중 \"장비 구입비\"는 450,000원 (37.5%) 입니다.";

        var reflowed = await CitedAnswer("reflow", minutes, "총 사업비는 1,200,000원이며, 이 중 \"장비 구입비\"는 450,000원 (37.5%) 입니다.");
        Check(reflowed.Question is null && reflowed.Sources.Single().Quote == exactBudget);
        Check(minutes.Contains(reflowed.Sources.Single().Quote, StringComparison.Ordinal));

        var typography = await CitedAnswer("typography", minutes, "일정 — 참여 기관은 '한국대학교', 도시안전센터이다.");
        Check(typography.Sources.Single().Quote == "일정 ― 참여 기관은 ‘한국대학교’, 도시안전센터이다.");

        const string report = "첫 문장은 시작입니다. 중간에는 다른 내용이 많이 있습니다. 마지막 문장은 끝입니다.";
        var gapped = await CitedAnswer("ellipsis", report, "첫 문장은 시작입니다. ... 마지막 문장은 끝입니다.");
        Check(gapped.Sources.Select(source => source.Quote).SequenceEqual(["첫 문장은 시작입니다.", "마지막 문장은 끝입니다."]));
        Check(gapped.Sources.All(source => report.Contains(source.Quote, StringComparison.Ordinal)));

        // Exact quotations pass through unchanged, including text that itself contains an ellipsis.
        var literal = await CitedAnswer("literal", "잠깐... 이것은 원문입니다.", "잠깐... 이것은 원문입니다.");
        Check(literal.Sources.Single().Quote == "잠깐... 이것은 원문입니다.");

        // Formatting is tolerated; changed words, numbers and invented text are not. The invented quotation is
        // never shown: the model gets two retries, then the excerpt the app read is shown without an answer.
        foreach (string forged in new[] { "총 사업비는 1,300,000원이며", "총 사업비는 120만원이며", "총 사업비는 전혀 다른 문장입니다", "총... 사업비", "..." })
        {
            var withheld = await CitedAnswer("forged", minutes, forged);
            Check(withheld.Answer.Length == 0 && withheld.Question == DocumentSearch.UnverifiedEvidenceMessage
                && withheld.Sources.All(source => minutes.Contains(source.Quote, StringComparison.Ordinal)
                    && !source.Quote.Contains("1,300,000", StringComparison.Ordinal)));
        }
        Check(CitationLocator.Locate(new string('가', 1300), new string('가', 601)) is null);
    }

    private static async Task CitationRetry()
    {
        var folder = Folder("citation-retry");
        var file = TextFile(folder, "source.txt", "The cost cap is 120 dollars.\nDelivery is Friday.");
        using var backend = new Backend([]);
        var transport = new ActionTransport((context, step) => step switch
        {
            0 => ListAction(context, folder),
            1 => ReadAction(context, file),
            2 => ActionJson(new { action = "answer", answer = "The cost cap is 130 dollars.",
                citations = new[] { new { sourceId = "s1", quote = "The cost cap is 130 dollars." } } }),
            _ => ActionJson(new { action = "answer", answer = "The cost cap is 120 dollars.",
                citations = new[] { new { sourceId = "s1", quote = "The cost cap is 120 dollars." } } })
        });
        var result = await new DocumentSearch(backend, transport, new FixtureReader())
            .RunAsync([new(ChatRole.User, "What is the cost cap?")], [folder], default);
        Check(result.Question is null && result.Answer == "The cost cap is 120 dollars."
            && result.Sources.Single().Quote == "The cost cap is 120 dollars.");
        Check(transport.Contexts.Count == 4 && transport.Contexts[3].Contains("Rejected answer", StringComparison.Ordinal)
            && !transport.Contexts[2].Contains("Rejected answer", StringComparison.Ordinal));
        Check(result.Coverage.Notes.All(note => !note.Contains("Rejected", StringComparison.Ordinal)));

        // A model that keeps inventing quotations gets two retries; nothing it invented is published.
        var stubborn = new ActionTransport((context, step) => step switch
        {
            0 => ListAction(context, folder),
            1 => ReadAction(context, file),
            _ => ActionJson(new { action = "answer", answer = "Invented.",
                citations = new[] { new { sourceId = "s1", quote = "Invented quotation" } } })
        });
        var withheld = await new DocumentSearch(backend, stubborn, new FixtureReader())
            .RunAsync([new(ChatRole.User, "What is the cost cap?")], [folder], default);
        Check(withheld.Answer.Length == 0 && withheld.Question == DocumentSearch.UnverifiedEvidenceMessage
            && withheld.Sources.Single().Quote == "The cost cap is 120 dollars.\nDelivery is Friday."
            && stubborn.Contexts.Count == 5);
    }

    private static async Task CitationExtractedText()
    {
        // Text extracted from real documents: words split by spacing, table markup and bullet symbols.
        var split = await CitedAnswer("split", "총 사 업 비는 1,200,000 원이며, 기간은 12개월이다.", "총 사업비는 1,200,000원이며");
        Check(split.Question is null && split.Sources.Single().Quote == "총 사 업 비는 1,200,000 원이며");

        const string table = "| 구분 | 예산(원) | 비율 |\n| --- | --- | --- |\n| 장비 구입비 | 450,000 | 37.5% |\n| 인건비 | 600,000 | 50% |";
        var cells = await CitedAnswer("table", table, "인건비 600,000 50%");
        Check(cells.Question is null && cells.Sources.Single().Quote == "인건비 | 600,000 | 50");

        var bullets = await CitedAnswer("bullets", "○ 연구 기간 ㆍ 2026년 4월\n○ 참여 기관 ㆍ 한국대학교", "연구 기간 · 2026년 4월");
        Check(bullets.Sources.Single().Quote == "연구 기간 ㆍ 2026년 4월");

        // Separators inside a number carry meaning; very short passages are too ambiguous without exact text.
        foreach (var (text, forged) in new[] { ("장비 구입비 450,000 37.5%", "장비 구입비 450,000 375%"),
            ("인건비 600,000원", "인건비 6,000,00원"), ("예 산", "예산"), ("기간은 2026-10-30까지", "기간은 2026-10-03까지") })
        {
            var withheld = await CitedAnswer("extracted-forged", text, forged);
            Check(withheld.Answer.Length == 0 && withheld.Question == DocumentSearch.UnverifiedEvidenceMessage);
        }
    }

    private static async Task CitationSourceMislabeled()
    {
        var folder = Folder("citation-mislabeled");
        var first = TextFile(folder, "first.txt", "The first file talks about schedules.");
        var second = TextFile(folder, "second.txt", "The budget is 120 dollars.");
        using var backend = new Backend([]);
        foreach (string label in new[] { "s1", "s9" })
        {
            var transport = new ActionTransport((context, step) => step switch
            {
                0 => ListAction(context, folder),
                1 => ReadAction(context, first),
                2 => ReadAction(context, second),
                _ => ActionJson(new { action = "answer", answer = "The budget is 120 dollars.",
                    citations = new[] { new { sourceId = label, quote = "The budget is 120 dollars." } } })
            });
            var result = await new DocumentSearch(backend, transport, new FixtureReader())
                .RunAsync([new(ChatRole.User, "What is the budget?")], [folder], default);
            Check(result.Question is null && result.Sources.Single() is { Id: "s2", Quote: "The budget is 120 dollars." } source
                && source.FullPath == second && transport.Contexts.Count == 4);
        }

        // Answering before anything was read is told to read first instead of failing.
        var early = new ActionTransport((context, step) => step switch
        {
            0 => ListAction(context, folder),
            1 => ActionJson(new { action = "answer", answer = "From memory.",
                citations = new[] { new { sourceId = "s1", quote = "From a previous answer." } } }),
            2 => ReadAction(context, second),
            _ => AnswerAction(context, "The budget is 120 dollars.")
        });
        var recovered = await new DocumentSearch(backend, early, new FixtureReader())
            .RunAsync([new(ChatRole.User, "What is the budget?")], [folder], default);
        Check(recovered.Question is null && recovered.Sources.Single().FullPath == second
            && early.Contexts[2].Contains("no document text has been read", StringComparison.Ordinal));
    }

    private static async Task VerificationRetry()
    {
        var folder = Folder("verification-retry");
        var file = TextFile(folder, "source.txt", "The review is on October 30. The budget is 120 dollars.");
        using var backend = new Backend([]);
        string UnsupportedClaim(string answer) => ActionJson(new
        {
            claims = new[] { new { text = answer, supported = false, sourceIds = Array.Empty<string>() } }
        });
        string SupportedClaim(string answer) => ActionJson(new
        {
            claims = new[] { new { text = answer, supported = true, sourceIds = new[] { "e1" } } }
        });
        int verifications = 0;
        var repaired = new ActionTransport((context, step) => step switch
        {
            0 => ListAction(context, folder),
            1 => ReadAction(context, file),
            2 => ActionJson(new { action = "answer", answer = "The review is on October 30, 2026.",
                citations = new[] { new { sourceId = "s1", quote = "The review is on October 30." } } }),
            _ => ActionJson(new { action = "answer", answer = "The review is on October 30.",
                citations = new[] { new { sourceId = "s1", quote = "The review is on October 30." } } })
        })
        {
            Verification = verification =>
            {
                verifications++;
                string answer = verification.GetProperty("answer").GetString()!;
                return verifications == 1 ? UnsupportedClaim(answer) : SupportedClaim(answer);
            }
        };
        var result = await new DocumentSearch(backend, repaired, new FixtureReader())
            .RunAsync([new(ChatRole.User, "When is the review?")], [folder], default);
        Check(result.Question is null && result.Answer == "The review is on October 30." && verifications == 2);
        Check(repaired.Contexts.Count == 4 && repaired.Contexts[3].Contains("not supported by the cited quotes", StringComparison.Ordinal)
            && repaired.Contexts[3].Contains("October 30, 2026", StringComparison.Ordinal));

        // Still unsupported after the single repair attempt: the answer stays withheld and the exact excerpt is shown.
        verifications = 0;
        var stubborn = new ActionTransport((context, step) => step switch
        {
            0 => ListAction(context, folder),
            1 => ReadAction(context, file),
            _ => ActionJson(new { action = "answer", answer = "The review is on October 30, 2026.",
                citations = new[] { new { sourceId = "s1", quote = "The review is on October 30." } } })
        })
        {
            Verification = verification =>
            {
                verifications++;
                return UnsupportedClaim(verification.GetProperty("answer").GetString()!);
            }
        };
        var withheld = await new DocumentSearch(backend, stubborn, new FixtureReader())
            .RunAsync([new(ChatRole.User, "When is the review?")], [folder], default);
        Check(withheld.Answer.Length == 0 && withheld.Question == DocumentSearch.UnverifiedEvidenceMessage
            && withheld.Sources.Single().Quote == "The review is on October 30." && verifications == 2);
    }

    private static Task<DocumentSearchResult> CitedAnswer(string name, string text, params string[] quotes)
    {
        var folder = Folder("citation-" + name);
        var file = TextFile(folder, "source.txt", text);
        var backend = new Backend([]);
        var transport = new ActionTransport((context, step) => step switch
        {
            0 => ListAction(context, folder),
            1 => ReadAction(context, file),
            _ => ActionJson(new { action = "answer", answer = "Cited.",
                citations = quotes.Select(quote => new { sourceId = "s1", quote }).ToArray() })
        });
        return new DocumentSearch(backend, transport, new FixtureReader())
            .RunAsync([new(ChatRole.User, "question")], [folder], default);
    }
}
