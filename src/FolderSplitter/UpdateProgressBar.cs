using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace QuadExplorer;

internal sealed class UpdateProgressBar : Control
{
    private System.Windows.Forms.Timer? timer;
    private readonly Stopwatch clock = new();
    private double phase;
    internal int? Percentage { get; private set; }
    internal bool AnimationRunning => timer is { Enabled: true };
    internal double BandPosition => phase;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<bool> AnimationPreference { get; set; } = WindowsAnimationEnabled;

    internal UpdateProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleName = "Update progress";
        AccessibleRole = AccessibleRole.ProgressBar;
        Size = new Size(100, 12);
        Margin = new Padding(8, 0, 8, 0);
        Anchor = AnchorStyles.Left;
        Visible = false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ScaleMetrics();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ScaleMetrics();
    }

    private void ScaleMetrics()
    {
        Size = new Size((100 * DeviceDpi + 48) / 96, (12 * DeviceDpi + 48) / 96);
        int gap = (8 * DeviceDpi + 48) / 96;
        Margin = new Padding(gap, 0, gap, 0);
    }

    internal void SetProgress(UpdateProgress progress)
    {
        Percentage = progress.Stage == UpdateStage.Downloading ? progress.Percentage : null;
        AccessibleDescription = Percentage is { } value ? value + "%" : null;
        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        RefreshAnimation();
    }

    internal void RefreshAnimation()
    {
        if (!Visible || FindForm() is not { Visible: true, WindowState: not FormWindowState.Minimized } || !AnimationPreference())
        {
            timer?.Stop();
            timer?.Dispose();
            timer = null;
            clock.Reset();
            phase = 0;
            Invalidate();
            return;
        }
        if (timer is not null) return;
        timer = new System.Windows.Forms.Timer { Interval = 33 };
        timer.Tick += (_, _) => AdvanceAnimation(clock.Elapsed);
        clock.Restart();
        timer.Start();
    }

    // A time-based phase keeps the sweep smooth even if a busy message loop skips a frame.
    internal void AdvanceAnimation(TimeSpan elapsed)
    {
        if (!AnimationRunning) return;
        phase = elapsed.TotalMilliseconds % 1400 / 1400;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int height = Math.Max(3, (4 * DeviceDpi + 48) / 96);
        var track = new Rectangle(0, (Height - height) / 2, Width, height);
        using var background = new SolidBrush(Theme.Border);
        e.Graphics.FillRectangle(background, track);
        int filled = Percentage is { } value ? (int)Math.Round(Width * value / 100d) : Width;
        if (filled <= 0) return;
        var fill = new Rectangle(track.X, track.Y, filled, track.Height);
        using var accent = new SolidBrush(Theme.Accent);
        e.Graphics.FillRectangle(accent, fill);
        if (!AnimationRunning) return;
        double eased = phase * phase * (3 - 2 * phase);
        int bandWidth = Math.Max(12, Width / 3);
        int left = (int)Math.Round((filled + 2 * bandWidth) * eased) - bandWidth;
        Color soft = Color.FromArgb((Theme.Accent.R + Theme.Canvas.R) / 2,
            (Theme.Accent.G + Theme.Canvas.G) / 2, (Theme.Accent.B + Theme.Canvas.B) / 2);
        var band = new Rectangle(left, track.Y, bandWidth, track.Height);
        var state = e.Graphics.Save();
        e.Graphics.SetClip(fill);
        using var wave = new LinearGradientBrush(band, Theme.Accent, Theme.Accent, LinearGradientMode.Horizontal)
        {
            InterpolationColors = new ColorBlend { Colors = [Theme.Accent, soft, Theme.Accent], Positions = [0, .5f, 1] }
        };
        e.Graphics.FillRectangle(wave, band);
        e.Graphics.Restore(state);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer?.Dispose(); timer = null; clock.Stop(); }
        base.Dispose(disposing);
    }

    private static bool WindowsAnimationEnabled() =>
        !SystemParametersInfo(0x1042, 0, out bool enabled, 0) || enabled;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
