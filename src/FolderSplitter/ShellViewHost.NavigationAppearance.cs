namespace QuadExplorer;

public sealed partial class ShellViewHost
{
    private readonly List<NavigationBackground> navigationBackgrounds = [];

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        // The Shell creates a fresh DefView for each navigation. Theme its empty background
        // before CreateWindow returns, not after the new folder has finished loading.
        if (message.Msg != 0x0210 || ((long)message.WParam & 0xffff) != 1) return;
        WatchNavigationBackground(message.LParam);
    }

    private void WatchNavigationBackground(nint window)
    {
        var name = new System.Text.StringBuilder(32);
        GetClassName(window, name, name.Capacity);
        if (name.ToString() is not ("ExplorerBrowserControl" or "SHELLDLL_DefView")) return;
        if (navigationBackgrounds.Any(background => background.Handle == window)) return;
        var background = new NavigationBackground(this);
        background.AssignHandle(window);
        navigationBackgrounds.Add(background);
    }

    private sealed class NavigationBackground(ShellViewHost owner) : NativeWindow
    {
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0014 && message.WParam != 0) // WM_ERASEBKGND
            {
                DetailsNative.GetClientRect(Handle, out var bounds);
                DetailsNative.FillColor(message.WParam, bounds, Theme.ListBack);
                message.Result = 1;
                return;
            }
            base.WndProc(ref message);
            if (message.Msg == 0x0210 && ((long)message.WParam & 0xffff) == 1)
                owner.WatchNavigationBackground(message.LParam);
            if (message.Msg == 0x0082) owner.navigationBackgrounds.Remove(this); // WM_NCDESTROY
        }
    }
}
