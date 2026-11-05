namespace QuadExplorer;

internal enum TabGroupColor
{
    Grey, Blue, Red, Yellow, Green, Pink, Purple, Cyan, Orange
}

internal sealed class TabGroup
{
    internal const int MaxTabs = 12;

    internal string Name { get; set; } = "";
    internal TabGroupColor Color { get; set; }
    internal bool Collapsed { get; set; }
}
