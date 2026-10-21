using System.Text.Json.Serialization;

namespace QuadExplorer;

public enum SplitEdge
{
    Left,
    Right,
    Top,
    Bottom
}

public sealed record PaneLayout
{
    public int Pane { get; init; } = -1;
    public bool Vertical { get; init; } = true;
    public double Ratio { get; init; } = .5;
    public PaneLayout? First { get; init; }
    public PaneLayout? Second { get; init; }

    [JsonIgnore]
    public bool IsLeaf => Pane >= 0;

    public static PaneLayout Leaf(int pane)
    {
        if (pane is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(pane));
        return new PaneLayout { Pane = pane };
    }

    public static PaneLayout Preset(int count, bool splitFirst = true, bool vertical = true)
    {
        if (count is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 1)
            return Leaf(0);

        var first = Leaf(0);
        var second = Leaf(1);
        if (count == 4 || count == 3 && splitFirst)
            first = new PaneLayout { Vertical = !vertical, First = first, Second = Leaf(2) };
        if (count == 4 || count == 3 && !splitFirst)
            second = new PaneLayout { Vertical = !vertical, First = second, Second = Leaf(count == 4 ? 3 : 2) };
        return new PaneLayout { Vertical = vertical, First = first, Second = second };
    }

    public int[] GetPanes()
    {
        if (IsLeaf)
            return [Pane];
        return [.. First!.GetPanes(), .. Second!.GetPanes()];
    }

    public PaneLayout Split(int targetPane, int newPane, SplitEdge edge)
    {
        if (newPane is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(newPane));
        if (edge is not (SplitEdge.Left or SplitEdge.Right or SplitEdge.Top or SplitEdge.Bottom))
            throw new ArgumentOutOfRangeException(nameof(edge));

        var panes = GetPanes();
        if (!panes.Contains(targetPane))
            throw new ArgumentException("Target pane does not exist.", nameof(targetPane));
        if (panes.Length >= 4)
            throw new InvalidOperationException("A layout cannot contain more than four panes.");
        if (panes.Contains(newPane))
            throw new ArgumentException("New pane is already in the layout.", nameof(newPane));

        return Replace(this);

        PaneLayout Replace(PaneLayout node)
        {
            if (node.IsLeaf)
            {
                if (node.Pane != targetPane)
                    return node;

                var added = Leaf(newPane);
                var before = edge is SplitEdge.Left or SplitEdge.Top;
                return new PaneLayout
                {
                    Vertical = edge is SplitEdge.Left or SplitEdge.Right,
                    First = before ? added : node,
                    Second = before ? node : added
                };
            }

            var first = Replace(node.First!);
            var second = Replace(node.Second!);
            return ReferenceEquals(first, node.First) && ReferenceEquals(second, node.Second)
                ? node
                : node with { First = first, Second = second };
        }
    }

    public PaneLayout SetRatio(string path, double ratio)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Replace(this, 0);

        PaneLayout Replace(PaneLayout? node, int offset)
        {
            if (node is null || node.IsLeaf)
                throw new ArgumentException("Path must identify a split branch.", nameof(path));
            if (offset == path.Length)
                return node with { Ratio = ClampRatio(ratio) };
            return path[offset] switch
            {
                '0' => node with { First = Replace(node.First, offset + 1) },
                '1' => node with { Second = Replace(node.Second, offset + 1) },
                _ => throw new ArgumentException("Path can contain only 0 and 1.", nameof(path))
            };
        }
    }

    public PaneLayout Equalize() => IsLeaf
        ? this
        : this with { Ratio = .5, First = First!.Equalize(), Second = Second!.Equalize() };

    public static bool TryNormalize(PaneLayout? candidate, out PaneLayout normalized)
    {
        normalized = Leaf(0);
        var panes = new HashSet<int>();
        if (!Clone(candidate, 0, out var result))
            return false;

        normalized = result;
        return true;

        bool Clone(PaneLayout? node, int depth, out PaneLayout clone)
        {
            clone = Leaf(0);
            if (node is null || depth > 3)
                return false;
            if (node.IsLeaf)
            {
                if (node.Pane > 3 || node.First is not null || node.Second is not null ||
                    !panes.Add(node.Pane) || panes.Count > 4)
                    return false;
                clone = node with { Ratio = ClampRatio(node.Ratio) };
                return true;
            }

            if (node.Pane != -1 ||
                !Clone(node.First, depth + 1, out var first) ||
                !Clone(node.Second, depth + 1, out var second))
                return false;

            clone = node with { Ratio = ClampRatio(node.Ratio), First = first, Second = second };
            return true;
        }
    }

    private static double ClampRatio(double ratio) =>
        double.IsFinite(ratio) ? Math.Clamp(ratio, .15, .85) : .5;
}
