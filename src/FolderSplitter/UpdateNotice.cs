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
    private readonly Button details;
    private readonly FlowLayoutPanel buttons;
    private readonly Label summary = new() { TextAlign = ContentAlignment.TopLeft };
    private IReadOnlyList<string> bullets = Array.Empty<string>();
    private bool showDetails;
    private bool layingOut;
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly Stopwatch transition = new();
    private Form? anchor;
    private Rectangle targetBounds;
    private int slideDistance;
    private bool animate;
    private double visibility;
    private double transitionStart;
    private double transitionEnd;
    internal bool Dismissed { get; private set; }

    internal event EventHandler? UpdateRequested;
    internal event EventHandler? DetailsRequested;

    internal UpdateNotice(UiText text, Font font, ReleaseInfo release, bool canInstall, bool executableAvailable = true)
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
        buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10, 4, 10, 10)
        };
        dismiss = Theme.Button("", "", () =>
        {
            Dismissed = true;
            StartTransition(0);
        }, 100);
        action = Theme.Button("", "", () => UpdateRequested?.Invoke(this, EventArgs.Empty), 140);
        details = Theme.Button("", "", () => DetailsRequested?.Invoke(this, EventArgs.Empty), 140);
        details.Dock = DockStyle.None;
        dismiss.Dock = action.Dock = DockStyle.None;
        buttons.Controls.Add(dismiss);
        buttons.Controls.Add(action);
        Controls.Add(message);
        Controls.Add(buttons);
        CancelButton = dismiss;
        timer.Tick += OnTimerTick;
        RefreshNotice(release, canInstall, executableAvailable);
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

    internal void RefreshNotice(ReleaseInfo release, bool canInstall, bool executableAvailable = true)
    {
        Text = text.T("Update available");
        AccessibleName = Text;
        message.Text = text.T("A new version {0} is available.", release.Tag);
        action.Text = text.T(canInstall ? "Update" : "Download");
        action.AccessibleName = action.Text;
        dismiss.Text = text.T("Close");
        dismiss.AccessibleName = dismiss.Text;
        details.Text = text.T("View details");
        details.AccessibleName = details.Text;
        bullets = UpdateSummary.ForLanguage(release.Summaries, text.Language).Take(3).ToArray();
        showDetails = canInstall && executableAvailable;
        if (anchor is not null) UpdateTarget();
    }

    private void OwnerChanged(object? sender, EventArgs e)
    {
        if (anchor is null) return;
        if (!anchor.Visible || anchor.WindowState == FormWindowState.Minimized) Close();
        else UpdateTarget();
    }

    private void UpdateTarget()
    {
        if (anchor is null || layingOut) return;
        LayoutForOwner();
        targetBounds = TargetBounds(anchor, Size);
        slideDistance = (12 * anchor.DeviceDpi + 48) / 96;
        PositionAtOwner();
    }

    private void LayoutForOwner()
    {
        if (anchor is null) return;
        layingOut = true;
        try
        {
            int Scale(int value) => (value * anchor.DeviceDpi + 48) / 96;
            int pad = Scale(16), gap = Scale(6);
            int compactWidth = Scale(NoticeSize.Width), compactHeight = Scale(NoticeSize.Height);
            Rectangle ownerClient = anchor.RectangleToScreen(anchor.ClientRectangle);
            var available = Rectangle.Intersect(ownerClient, Screen.FromRectangle(ownerClient).WorkingArea);
            int margin = Scale(12);
            int maxWidth = available.Width - 2 * margin;
            int maxHeight = available.Height - 2 * margin;
            int ButtonWidth(Button button, int minimum) =>
                Math.Max(Scale(minimum), TextRenderer.MeasureText(button.Text, Font).Width + Scale(24));
            int buttonHeight = Math.Max(Scale(30), Font.Height + Scale(12));
            int actionWidth = ButtonWidth(action, 140), dismissWidth = ButtonWidth(dismiss, 100);
            int detailsWidth = showDetails ? ButtonWidth(details, 140) : 0;
            int width = Math.Max(compactWidth, actionWidth + dismissWidth + detailsWidth
                + Scale(showDetails ? 56 : 48));
            width = Math.Min(width, maxWidth);
            string summaryText = string.Join(Environment.NewLine, bullets.Select(item => "• " + item));
            int textWidth = width - 2 * pad;
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;
            int messageHeight = textWidth > 0 ? TextRenderer.MeasureText(message.Text, Font,
                new Size(textWidth, int.MaxValue), flags).Height + Scale(4) : 0;
            int summaryHeight = textWidth > 0 && bullets.Count > 0 ? TextRenderer.MeasureText(summaryText, Font,
                new Size(textWidth, int.MaxValue), flags).Height + Scale(4) : 0;
            int height = pad + messageHeight + gap + summaryHeight + gap + buttonHeight + pad;
            int neededButtonWidth = actionWidth + dismissWidth + detailsWidth + Scale(showDetails ? 44 : 36);
            bool expanded = bullets.Count > 0 && width >= compactWidth && width >= neededButtonWidth
                && height <= maxHeight;

            SuspendLayout();
            if (expanded)
            {
                message.Dock = DockStyle.None;
                message.Padding = Padding.Empty;
                message.TextAlign = ContentAlignment.TopLeft;
                buttons.Dock = DockStyle.None;
                buttons.AutoSize = false;
                buttons.Padding = Padding.Empty;
                action.Size = new Size(actionWidth, buttonHeight);
                dismiss.Size = new Size(dismissWidth, buttonHeight);
                details.Size = new Size(detailsWidth, buttonHeight);
                if (showDetails && !buttons.Controls.Contains(details)) buttons.Controls.Add(details);
                if (!showDetails) buttons.Controls.Remove(details);
                if (!Controls.Contains(summary)) Controls.Add(summary);
                summary.ForeColor = Theme.Text;
                summary.BackColor = Theme.Canvas;
                summary.Text = summaryText;
                ClientSize = new Size(width, height);
                message.SetBounds(pad, pad, textWidth, messageHeight);
                summary.SetBounds(pad, pad + messageHeight + gap, textWidth, summaryHeight);
                buttons.SetBounds(pad, height - pad - buttonHeight - Scale(4), width - 2 * pad, buttonHeight + Scale(4));
            }
            else
            {
                buttons.Controls.Remove(details);
                Controls.Remove(summary);
                message.Dock = DockStyle.Fill;
                message.Padding = new Padding(Scale(16), Scale(12), Scale(16), Scale(4));
                message.TextAlign = ContentAlignment.MiddleLeft;
                buttons.Dock = DockStyle.Bottom;
                buttons.AutoSize = true;
                buttons.Padding = new Padding(Scale(10), Scale(4), Scale(10), Scale(10));
                action.Size = new Size(Scale(140), Scale(30));
                dismiss.Size = new Size(Scale(100), Scale(30));
                ClientSize = new Size(compactWidth, compactHeight);
            }
            ResumeLayout(true);
        }
        finally { layingOut = false; }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (anchor is not null) UpdateTarget();
    }

    private void PositionAtOwner()
    {
        if (anchor is null) return;
        if (targetBounds.IsEmpty)
        {
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
            details.Dispose();
            summary.Dispose();
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
