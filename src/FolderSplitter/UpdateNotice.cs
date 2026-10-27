using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuadExplorer;

internal sealed class UpdateNotice : Form
{
    private const int TransitionMilliseconds = 240;
    private static readonly Size NoticeSize = new(420, 120);
    private readonly UiText text;
    private readonly Label message = new()
    {
        Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 4),
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Button action;
    private readonly Button dismiss;
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly Stopwatch transition = new();
    private Form? anchor;
    private Rectangle targetBounds;
    private int slideDistance;
    private bool animate;
    private double visibility;
    private double transitionStart;
    private double transitionEnd;
    internal bool Deferred { get; private set; }

    internal event EventHandler? UpdateRequested;

    internal UpdateNotice(UiText text, Font font, ReleaseInfo release, bool canInstall)
    {
        this.text = text;
        Font = font;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = NoticeSize;
        Padding = new Padding(2);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10, 4, 10, 10)
        };
        dismiss = Theme.Button("", "", () => StartTransition(0), 100);
        action = Theme.Button("", "", () => UpdateRequested?.Invoke(this, EventArgs.Empty), 140);
        dismiss.Dock = action.Dock = DockStyle.None;
        buttons.Controls.Add(dismiss);
        buttons.Controls.Add(action);
        Controls.Add(message);
        Controls.Add(buttons);
        CancelButton = dismiss;
        timer.Tick += OnTimerTick;
        RefreshNotice(release, canInstall);
        Theme.Apply(this);
    }

    protected override bool ShowWithoutActivation => true;

    internal void Present(Form owner)
    {
        anchor = owner;
        owner.Move += OwnerChanged;
        owner.Resize += OwnerChanged;
        owner.VisibleChanged += OwnerChanged;
        animate = SystemParametersInfo(0x1042, 0, out int enabled, 0) && enabled != 0;
        visibility = animate ? 0 : 1;
        Opacity = visibility;
        UpdateTarget();
        if (IsDisposed) return;
        Show(owner);
        UpdateTarget();
        if (IsDisposed) return;
        if (animate) StartTransition(1);
        else CompleteEntry();
    }

    internal static bool OwnerVisibleAtNotice(Form owner)
    {
        int dpi = owner.DeviceDpi;
        var size = new Size((NoticeSize.Width * dpi + 48) / 96, (NoticeSize.Height * dpi + 48) / 96);
        Rectangle bounds = TargetBounds(owner, size);
        return !bounds.IsEmpty && BelongsToOwner(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2), owner);
    }

    private static Rectangle TargetBounds(Form owner, Size size)
    {
        Rectangle client = owner.RectangleToScreen(owner.ClientRectangle);
        Rectangle bounds = Rectangle.Intersect(client, Screen.FromRectangle(client).WorkingArea);
        int margin = (12 * owner.DeviceDpi + 48) / 96;
        if (bounds.Width < size.Width + 2 * margin || bounds.Height < size.Height + 2 * margin) return Rectangle.Empty;
        return new Rectangle(bounds.Right - size.Width - margin, bounds.Bottom - size.Height - margin, size.Width, size.Height);
    }

    private static bool BelongsToOwner(Point point, Form owner)
    {
        nint window = WindowFromPoint(point);
        if (window == 0) return false;
        nint root = GetAncestor(window, 3); // GA_ROOTOWNER
        return root == owner.Handle ||
            Control.FromHandle(root) is UpdateNotice notice && ReferenceEquals(notice.Owner, owner);
    }

    internal void RefreshNotice(ReleaseInfo release, bool canInstall)
    {
        Text = text.T("Update available");
        AccessibleName = Text;
        message.Text = text.T("A new version {0} is available.", release.Tag);
        action.Text = text.T(canInstall ? "Update" : "Download");
        action.AccessibleName = action.Text;
        dismiss.Text = text.T("Close");
        dismiss.AccessibleName = dismiss.Text;
    }

    private void OwnerChanged(object? sender, EventArgs e)
    {
        if (anchor is null) return;
        if (!anchor.Visible || anchor.WindowState == FormWindowState.Minimized) Close();
        else UpdateTarget();
    }

    private void UpdateTarget()
    {
        if (anchor is null) return;
        targetBounds = TargetBounds(anchor, Size);
        slideDistance = (12 * anchor.DeviceDpi + 48) / 96;
        PositionAtOwner();
    }

    private void PositionAtOwner()
    {
        if (anchor is null) return;
        if (targetBounds.IsEmpty)
        {
            Deferred = true;
            Close();
            return;
        }
        int offset = (int)Math.Round(slideDistance * (1 - visibility));
        Location = new Point(targetBounds.Left, targetBounds.Top + offset);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width <= 0 || Height <= 0) return;
        Region? previous = Region;
        Region = Theme.CardRegion(Size, DeviceDpi);
        previous?.Dispose();
        if (anchor is not null) UpdateTarget();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Theme.PaintCard(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), false, DeviceDpi, 0);
    }

    private void StartTransition(double target)
    {
        if (!animate)
        {
            if (target == 0) Close();
            else CompleteEntry();
            return;
        }
        transitionStart = visibility;
        transitionEnd = target;
        transition.Restart();
        timer.Interval = 16;
        timer.Start();
    }

    private void CompleteEntry()
    {
        visibility = 1;
        Opacity = 1;
        PositionAtOwner();
        if (IsDisposed) return;
        timer.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        double duration = TransitionMilliseconds * Math.Abs(transitionEnd - transitionStart);
        double progress = duration == 0 ? 1 : Math.Clamp(transition.Elapsed.TotalMilliseconds / duration, 0, 1);
        double remaining = 1 - progress;
        double eased = 1 - remaining * remaining * remaining;
        visibility = transitionStart + (transitionEnd - transitionStart) * eased;
        Opacity = visibility;
        PositionAtOwner();
        if (IsDisposed) return;
        if (progress < 1) return;
        if (transitionEnd == 0) Close();
        else CompleteEntry();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            if (anchor is not null)
            {
                anchor.Move -= OwnerChanged;
                anchor.Resize -= OwnerChanged;
                anchor.VisibleChanged -= OwnerChanged;
                anchor = null;
            }
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

}
