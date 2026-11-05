namespace QuadExplorer;

internal enum TabGroupColor
{
    Grey, Blue, Red, Yellow, Green, Pink, Purple, Cyan, Orange
}

internal sealed class TabGroup
{
    internal const int MaxTabs = 12;

    internal string Name { get; set; } = "";
    internal string? SavedId { get; set; }
    internal TabGroupColor Color { get; set; }
    internal bool Collapsed { get; set; }
    // Collapsing selects an outside tab; a later window transfer still selects the previous member.
    internal int LastSelectedMember { get; set; }
}
