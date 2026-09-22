using System.Drawing;
using System.Windows;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TableSnip.Interop;

namespace TableSnip;

/// <summary>
/// One screen-capture session: freezes the whole desktop, opens a full-screen overlay per
/// monitor, and resolves with the region the user dragged (or null when cancelled).
/// </summary>
public sealed class SnipSession
{
    private readonly TaskCompletionSource<Rectangle?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<SnipWindow> _windows = new();

    public static async Task<Bitmap?> CaptureAsync()
    {
        var virt = System.Windows.Forms.SystemInformation.VirtualScreen;
        var shot = new Bitmap(virt.Width, virt.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(shot))
            g.CopyFromScreen(virt.Left, virt.Top, 0, 0, virt.Size, CopyPixelOperation.SourceCopy);

        var session = new SnipSession();
        try
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var b = screen.Bounds;
                var part = new Rectangle(b.X - virt.X, b.Y - virt.Y, b.Width, b.Height);
                part.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
                if (part.Width <= 0 || part.Height <= 0) continue;

                using var piece = shot.Clone(part, PixelFormat.Format32bppPArgb);
                var screenRect = new Rectangle(part.X + virt.X, part.Y + virt.Y, part.Width, part.Height);
                session._windows.Add(new SnipWindow(session, screenRect, ToBitmapSource(piece)));
            }
            if (session._windows.Count == 0) return null;

            foreach (var w in session._windows) w.Show();

            var cursor = System.Windows.Forms.Cursor.Position;
            var front = session._windows.FirstOrDefault(w => w.ScreenBounds.Contains(cursor)) ?? session._windows[0];
            front.Activate();
            front.Focus();

            var picked = await session._tcs.Task;
            if (picked is not Rectangle r) return null;

            r.Intersect(virt);
            if (r.Width < 2 || r.Height < 2) return null;
            return shot.Clone(new Rectangle(r.X - virt.X, r.Y - virt.Y, r.Width, r.Height), PixelFormat.Format32bppPArgb);
        }
        finally
        {
            foreach (var w in session._windows)
            {
                try { w.Close(); } catch { }
            }
            shot.Dispose();
        }
    }

    public void Complete(Rectangle r) => _tcs.TrySetResult(r);
    public void Cancel() => _tcs.TrySetResult(null);

    private static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        IntPtr h = bmp.GetHbitmap();
        try
        {
            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            NativeMethods.DeleteObject(h);
        }
    }
}

public partial class SnipWindow : Window
{
    private readonly SnipSession _session;
    private System.Windows.Point? _start;
    private Rect _sel;

    /// <summary>Physical-pixel bounds of the monitor this overlay covers.</summary>
    public Rectangle ScreenBounds { get; }

    public SnipWindow(SnipSession session, Rectangle screenBounds, BitmapSource shot)
    {
        InitializeComponent();
        _session = session;
        ScreenBounds = screenBounds;
        Shot.Source = shot;

        Loaded += (_, _) => { Place(); UpdateDim(); };
        ContentRendered += (_, _) => Place();
        SizeChanged += (_, _) => UpdateDim();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Place();
    }

    /// <summary>Position in physical pixels so the overlay maps 1:1 onto the frozen screenshot.</summary>
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.GetWindowRect(hwnd, out var current);
        if (current.Left == ScreenBounds.X && current.Top == ScreenBounds.Y &&
            current.Width == ScreenBounds.Width && current.Height == ScreenBounds.Height) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, ScreenBounds.X, ScreenBounds.Y,
            ScreenBounds.Width, ScreenBounds.Height, NativeMethods.SWP_NOACTIVATE);
    }

    private Matrix ToDevice =>
        PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;

    // ------------------------------------------------------------------ input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _start = e.GetPosition(Root);
        _sel = new Rect(_start.Value, new System.Windows.Size(0, 0));
        CaptureMouse();
        Hint.Visibility = Visibility.Collapsed;
        UpdateSelection();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_start == null) return;
        _sel = new Rect(_start.Value, e.GetPosition(Root));
        UpdateSelection();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_start == null) return;
        ReleaseMouseCapture();
        _sel = new Rect(_start.Value, e.GetPosition(Root));
        _start = null;

        if (_sel.Width < 4 || _sel.Height < 4)
        {
            _sel = new Rect();
            UpdateSelection();
            Hint.Visibility = Visibility.Visible;
            return;
        }

        var m = ToDevice;
        var tl = m.Transform(_sel.TopLeft);
        var br = m.Transform(_sel.BottomRight);
        var r = new Rectangle(
            ScreenBounds.X + (int)Math.Round(tl.X),
            ScreenBounds.Y + (int)Math.Round(tl.Y),
            (int)Math.Round(br.X - tl.X),
            (int)Math.Round(br.Y - tl.Y));
        _session.Complete(r);
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        _session.Cancel();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) _session.Cancel();
    }

    // ------------------------------------------------------------------ drawing

    private void UpdateSelection()
    {
        bool has = _sel.Width > 0 && _sel.Height > 0;
        var vis = has ? Visibility.Visible : Visibility.Collapsed;
        SelRect.Visibility = vis;
        SelShadow.Visibility = vis;
        SizeLabel.Visibility = vis;

        if (has)
        {
            Canvas.SetLeft(SelRect, _sel.X);
            Canvas.SetTop(SelRect, _sel.Y);
            SelRect.Width = _sel.Width;
            SelRect.Height = _sel.Height;

            Canvas.SetLeft(SelShadow, _sel.X - 1);
            Canvas.SetTop(SelShadow, _sel.Y - 1);
            SelShadow.Width = _sel.Width + 2;
            SelShadow.Height = _sel.Height + 2;

            var m = ToDevice;
            SizeText.Text = $"{Math.Round(_sel.Width * m.M11)} × {Math.Round(_sel.Height * m.M22)}";
            double ly = _sel.Bottom + 8;
            if (ly + 24 > ActualHeight) ly = Math.Max(0, _sel.Y - 28);
            Canvas.SetLeft(SizeLabel, Math.Max(0, _sel.X));
            Canvas.SetTop(SizeLabel, ly);
        }
        UpdateDim();
    }

    private void UpdateDim()
    {
        var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        Dim.Data = _sel.Width > 0 && _sel.Height > 0
            ? new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(_sel))
            : full;
    }
}
