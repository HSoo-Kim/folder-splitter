using System.Text.Json;
using QuadExplorer;

internal static class PaneLayoutTests
{
    public static (string Name, Action Run)[] Cases =
    [
        ("pane defaults and small presets", SmallPresets),
        ("both three-pane presets and orientations", ThreePanePresets),
        ("four-pane presets and orientations", FourPanePresets),
        ("split insertion on every edge", SplitEdges),
        ("split preserves other branches and pane IDs", SplitPreservesNodes),
        ("split and preset limits reject invalid arguments", InvalidRequests),
        ("ratio paths update only the named branch", RatioPaths),
        ("ratio paths reject leaves and invalid choices", InvalidRatioPaths),
        ("ratio bounds and nonfinite values", RatioBounds),
        ("equalize preserves tree structure", Equalize),
        ("normalization clones and clamps valid trees", NormalizeValid),
        ("normalization rejects malformed trees", NormalizeInvalid),
        ("pane layout JSON roundtrip excludes derived state", JsonRoundtrip)
    ];

    private static void SmallPresets()
    {
        var defaults = new PaneLayout();
        Equal(-1, defaults.Pane);
        Equal(true, defaults.Vertical);
        Equal(.5, defaults.Ratio);
        Equal(false, defaults.IsLeaf);
        Equal<PaneLayout?>(null, defaults.First);
        Equal<PaneLayout?>(null, defaults.Second);
        Equal(PaneLayout.Leaf(0), PaneLayout.Preset(1));
        for (var pane = 0; pane < 4; pane++)
        {
            var leaf = PaneLayout.Leaf(pane);
            Equal(true, leaf.IsLeaf);
            Sequence([pane], leaf.GetPanes());
        }

        foreach (var vertical in new[] { false, true })
        {
            var pair = PaneLayout.Preset(2, vertical: vertical);
            Branch(pair, vertical, 0, 1);
            Sequence([0, 1], pair.GetPanes());
        }
    }

    private static void ThreePanePresets()
    {
        foreach (var vertical in new[] { false, true })
        {
            foreach (var splitFirst in new[] { false, true })
            {
                var layout = PaneLayout.Preset(3, splitFirst, vertical);
                Equal(vertical, layout.Vertical);
                Equal(.5, layout.Ratio);
                Equal(false, layout.IsLeaf);
                if (splitFirst)
                {
                    Branch(layout.First!, !vertical, 0, 2);
                    Equal(PaneLayout.Leaf(1), layout.Second);
                    Sequence([0, 2, 1], layout.GetPanes());
                }
                else
                {
                    Equal(PaneLayout.Leaf(0), layout.First);
                    Branch(layout.Second!, !vertical, 1, 2);
                    Sequence([0, 1, 2], layout.GetPanes());
                }
            }
        }
    }

    private static void FourPanePresets()
    {
        foreach (var vertical in new[] { false, true })
        {
            var layout = PaneLayout.Preset(4, vertical: vertical);
            Equal(vertical, layout.Vertical);
            Equal(.5, layout.Ratio);
            Branch(layout.First!, !vertical, 0, 2);
            Branch(layout.Second!, !vertical, 1, 3);
            Sequence([0, 2, 1, 3], layout.GetPanes());
            Equal(layout, PaneLayout.Preset(4, splitFirst: false, vertical: vertical));
        }
    }

    private static void SplitEdges()
    {
        foreach (var edge in new[] { SplitEdge.Left, SplitEdge.Right, SplitEdge.Top, SplitEdge.Bottom })
        {
            var original = PaneLayout.Leaf(2);
            var split = original.Split(2, 3, edge);
            var before = edge is SplitEdge.Left or SplitEdge.Top;
            Branch(split, edge is SplitEdge.Left or SplitEdge.Right, before ? 3 : 2, before ? 2 : 3);
            Equal(true, ReferenceEquals(original, before ? split.Second : split.First));
            Equal(PaneLayout.Leaf(2), original);
        }
    }

    private static void SplitPreservesNodes()
    {
        var original = PaneLayout.Preset(3, splitFirst: false, vertical: false)
            .SetRatio("", .6).SetRatio("1", .7);
        var updated = original.Split(0, 3, SplitEdge.Bottom);
        Equal(original.Vertical, updated.Vertical);
        Equal(.6, updated.Ratio);
        Equal(true, ReferenceEquals(original.Second, updated.Second));
        Equal(true, ReferenceEquals(original.First, updated.First!.First));
        Branch(updated.First!, false, 0, 3);
        Equal(.7, updated.Second!.Ratio);
        Sequence([0, 3, 1, 2], updated.GetPanes());
        Sequence([0, 1, 2], original.GetPanes());

        var sparse = PaneLayout.Leaf(3).Split(3, 1, SplitEdge.Left).Split(1, 0, SplitEdge.Top);
        Sequence([0, 1, 3], sparse.GetPanes());
    }

    private static void InvalidRequests()
    {
        foreach (var invalid in new[] { int.MinValue, -1, 4, int.MaxValue })
            Throws<ArgumentOutOfRangeException>(() => PaneLayout.Leaf(invalid));
        foreach (var invalid in new[] { int.MinValue, 0, 5, int.MaxValue })
            Throws<ArgumentOutOfRangeException>(() => PaneLayout.Preset(invalid));

        var layout = PaneLayout.Preset(2);
        Throws<ArgumentException>(() => layout.Split(0, 1, SplitEdge.Right));
        Throws<ArgumentException>(() => layout.Split(3, 2, SplitEdge.Right));
        Throws<ArgumentOutOfRangeException>(() => layout.Split(0, -1, SplitEdge.Right));
        Throws<ArgumentOutOfRangeException>(() => layout.Split(0, 4, SplitEdge.Right));
        Throws<ArgumentOutOfRangeException>(() => layout.Split(0, 2, (SplitEdge)(-1)));
        Throws<ArgumentOutOfRangeException>(() => layout.Split(0, 2, (SplitEdge)4));
        Throws<InvalidOperationException>(() => PaneLayout.Preset(4).Split(0, 3, SplitEdge.Right));
        Equal(PaneLayout.Preset(2), layout);
    }

    private static void RatioPaths()
    {
        var original = PaneLayout.Preset(4);
        var rootChanged = original.SetRatio("", .6);
        Equal(.6, rootChanged.Ratio);
        Equal(true, ReferenceEquals(original.First, rootChanged.First));
        Equal(true, ReferenceEquals(original.Second, rootChanged.Second));

        var firstChanged = rootChanged.SetRatio("0", .3);
        Equal(.6, firstChanged.Ratio);
        Equal(.3, firstChanged.First!.Ratio);
        Equal(true, ReferenceEquals(rootChanged.Second, firstChanged.Second));
        var secondChanged = firstChanged.SetRatio("1", .7);
        Equal(.7, secondChanged.Second!.Ratio);
        Equal(true, ReferenceEquals(firstChanged.First, secondChanged.First));
        Equal(PaneLayout.Preset(4), original);

        var deep = PaneLayout.Preset(2).Split(0, 2, SplitEdge.Bottom).Split(0, 3, SplitEdge.Right);
        var deepChanged = deep.SetRatio("00", .25);
        Equal(.25, deepChanged.First!.First!.Ratio);
        Equal(.5, deep.First!.First!.Ratio);
        Equal(true, ReferenceEquals(deep.First!.Second, deepChanged.First!.Second));
        Equal(true, ReferenceEquals(deep.Second, deepChanged.Second));
        Equal(true, PaneLayout.TryNormalize(deepChanged, out _));
    }

    private static void InvalidRatioPaths()
    {
        var layout = PaneLayout.Preset(3);
        foreach (var path in new[] { "1", "00", "01", "000", "2", "0x", " 0" })
            Throws<ArgumentException>(() => layout.SetRatio(path, .6));
        Throws<ArgumentNullException>(() => layout.SetRatio(null!, .6));
        Throws<ArgumentException>(() => PaneLayout.Leaf(0).SetRatio("", .6));
        Throws<ArgumentException>(() => new PaneLayout().SetRatio("0", .6));
        Equal(PaneLayout.Preset(3), layout);
    }

    private static void RatioBounds()
    {
        var layout = PaneLayout.Preset(2);
        foreach (var (input, expected) in new[]
        {
            (double.MinValue, .15), (-1d, .15), (.15, .15), (.6, .6), (.85, .85),
            (2d, .85), (double.MaxValue, .85), (double.NaN, .5),
            (double.NegativeInfinity, .5), (double.PositiveInfinity, .5)
        })
            Equal(expected, layout.SetRatio("", input).Ratio);
        Equal(.5, layout.Ratio);
    }

    private static void Equalize()
    {
        var original = PaneLayout.Preset(4, vertical: false)
            .SetRatio("", .2).SetRatio("0", .3).SetRatio("1", .8);
        Equal(PaneLayout.Preset(4, vertical: false), original.Equalize());
        Equal(.2, original.Ratio);
        Equal(.3, original.First!.Ratio);
        Equal(.8, original.Second!.Ratio);
        var sparse = PaneLayout.Leaf(3).Split(3, 1, SplitEdge.Top).SetRatio("", .7);
        Equal(PaneLayout.Leaf(3).Split(3, 1, SplitEdge.Top), sparse.Equalize());
        var leaf = PaneLayout.Leaf(2);
        Equal(leaf, leaf.Equalize());
    }

    private static void NormalizeValid()
    {
        for (var count = 1; count <= 4; count++)
        {
            foreach (var vertical in new[] { false, true })
            {
                var original = PaneLayout.Preset(count, vertical: vertical);
                Equal(true, PaneLayout.TryNormalize(original, out var clone));
                Equal(original, clone);
                DistinctNodes(original, clone);
            }
        }

        var candidate = PaneLayout.Preset(4) with
        {
            Ratio = double.NaN,
            First = PaneLayout.Preset(4).First! with { Ratio = -100 },
            Second = PaneLayout.Preset(4).Second! with { Ratio = 100 }
        };
        Equal(true, PaneLayout.TryNormalize(candidate, out var normalized));
        Equal(.5, normalized.Ratio);
        Equal(.15, normalized.First!.Ratio);
        Equal(.85, normalized.Second!.Ratio);
        Equal(true, double.IsNaN(candidate.Ratio));
        Equal(-100d, candidate.First!.Ratio);
        Equal(100d, candidate.Second!.Ratio);
        DistinctNodes(candidate, normalized);

        foreach (var ratio in new[] { double.NegativeInfinity, double.PositiveInfinity })
        {
            Equal(true, PaneLayout.TryNormalize(PaneLayout.Preset(2) with { Ratio = ratio }, out normalized));
            Equal(.5, normalized.Ratio);
        }
        var sparse = PaneLayout.Leaf(3).Split(3, 1, SplitEdge.Right);
        Equal(true, PaneLayout.TryNormalize(sparse, out normalized));
        Equal(sparse, normalized);
    }

    private static void NormalizeInvalid()
    {
        var valid = PaneLayout.Preset(2);
        PaneLayout?[] invalid =
        [
            null,
            new PaneLayout(),
            valid with { First = null },
            valid with { Second = null },
            valid with { Pane = -2 },
            new PaneLayout { Pane = -2 },
            new PaneLayout { Pane = 4 },
            new PaneLayout { Pane = int.MaxValue },
            PaneLayout.Leaf(0) with { First = PaneLayout.Leaf(1) },
            PaneLayout.Leaf(0) with { Second = PaneLayout.Leaf(1) },
            valid with { Second = PaneLayout.Leaf(0) },
            PaneLayout.Preset(4) with { Second = PaneLayout.Preset(4).First },
            new PaneLayout { First = PaneLayout.Preset(4), Second = PaneLayout.Leaf(3) }
        ];
        foreach (var candidate in invalid)
        {
            Equal(false, PaneLayout.TryNormalize(candidate, out var normalized));
            Equal(PaneLayout.Leaf(0), normalized);
        }

        var deep = PaneLayout.Leaf(0);
        for (var depth = 0; depth < 128; depth++)
            deep = new PaneLayout { First = deep, Second = PaneLayout.Leaf(1) };
        Equal(false, PaneLayout.TryNormalize(deep, out var fallback));
        Equal(PaneLayout.Leaf(0), fallback);
    }

    private static void JsonRoundtrip()
    {
        var original = PaneLayout.Preset(3, splitFirst: false, vertical: false).SetRatio("1", .3);
        var json = JsonSerializer.Serialize(original);
        using var document = JsonDocument.Parse(json);
        Equal(false, document.RootElement.TryGetProperty("IsLeaf", out _));
        Equal(false, document.RootElement.GetProperty("First").TryGetProperty("IsLeaf", out _));
        Equal(true, PaneLayout.TryNormalize(JsonSerializer.Deserialize<PaneLayout>(json), out var restored));
        Equal(original, restored);
    }

    private static void Branch(PaneLayout node, bool vertical, int first, int second)
    {
        Equal(false, node.IsLeaf);
        Equal(-1, node.Pane);
        Equal(vertical, node.Vertical);
        Equal(.5, node.Ratio);
        Equal(PaneLayout.Leaf(first), node.First);
        Equal(PaneLayout.Leaf(second), node.Second);
    }

    private static void DistinctNodes(PaneLayout original, PaneLayout clone)
    {
        Equal(false, ReferenceEquals(original, clone));
        if (original.IsLeaf)
            return;
        DistinctNodes(original.First!, clone.First!);
        DistinctNodes(original.Second!, clone.Second!);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }

    private static void Sequence(IEnumerable<int> expected, IEnumerable<int> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Sequences differ.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
