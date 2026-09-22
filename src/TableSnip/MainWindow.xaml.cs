using System.ComponentModel;
using System.Data;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using TableSnip.Core;
using TableSnip.Interop;

namespace TableSnip;

public partial class MainWindow : Window
{
    private readonly Recognizer _recognizer;
    private readonly bool _startWithSnip;
    private TrayIcon? _tray;
    private bool _quitting, _trayHintShown;
    private List<OcrWord>? _words;
    private System.Drawing.Bitmap? _image;
    private DataTable? _table;
    private HotKey? _hotKey;
    private bool _busy, _snipping, _previewVisible = true;
    private GridLength _previewWidth = new(300);
    private DateTime _lastDragOver;

    private readonly DispatcherTimer _rebuildTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _copiedTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };

    public MainWindow(bool startWithSnip = false, bool startInTray = false)
    {
        InitializeComponent();
        _startWithSnip = startWithSnip;
        _recognizer = new Recognizer();

        // When starting hidden in the tray the window is never shown, but the global hotkey and
        // the tray icon are set up in OnSourceInitialized, which needs a window handle.
        if (startInTray) new WindowInteropHelper(this).EnsureHandle();

        _rebuildTimer.Tick += (_, _) => { _rebuildTimer.Stop(); RebuildTable(copyToClipboard: true); };
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            CopyLabel.Text = "Copy table";
            CopyIcon.Data = (Geometry)FindResource("IconCopy");
        };
        Loaded += OnLoaded;
    }

    // ------------------------------------------------------------------ lifecycle

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Theme.ApplyTitleBar(this);

        _hotKey = HotKey.TryRegister(this, ModifierKeys.Control | ModifierKeys.Alt, Key.T, () => _ = SnipAsync());
        if (_hotKey != null)
        {
            HotkeyHint.Text = $"or press {_hotKey.DisplayText} from any app";
            SnipButton.ToolTip = $"Select a table anywhere on your screen ({_hotKey.DisplayText} works from any app)";
        }
        else
        {
            HotkeyHint.Visibility = Visibility.Collapsed;
        }

        _tray = new TrayIcon(LoadAppIcon(), _hotKey?.DisplayText);
        _tray.OpenRequested += ShowFromTray;
        _tray.SnipRequested += SnipFromTray;
        _tray.QuitRequested += Quit;

        ShowIdleStatus();
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))
                  ?? throw new InvalidOperationException("app.ico missing");
        return new System.Drawing.Icon(res.Stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    // ------------------------------------------------------------------ tray

    public void ShowFromTray() => ShowAndActivate();

    public void SnipFromTray() => _ = SnipAsync();

    public void Quit()
    {
        _quitting = true;
        Close();
        Application.Current.Shutdown();
    }

    /// <summary>Closing the window keeps TableSnip in the tray so the global hotkey stays available.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_quitting && _tray != null)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                var trigger = _hotKey != null ? $"Press {_hotKey.DisplayText}" : "Click the tray icon";
                _tray.ShowBalloon("TableSnip is still running", $"{trigger} any time to snip a table. Right-click the tray icon to quit.");
            }
            return;
        }
        base.OnClosing(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_startWithSnip) await SnipAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray?.Dispose();
        _hotKey?.Dispose();
        _image?.Dispose();
        _recognizer.Dispose();
        base.OnClosed(e);
    }

    private void Window_Activated(object sender, EventArgs e)
    {
        if (EmptyState.Visibility == Visibility.Visible && !_busy && ClipboardImage.HasImage())
            SetStatus("There is an image on your clipboard. Press Ctrl+V to read it.", ok: false);
    }

    // ------------------------------------------------------------------ input paths

    private async void Snip_Click(object sender, RoutedEventArgs e) => await SnipAsync();
    private async void Paste_Click(object sender, RoutedEventArgs e) => await PasteAsync();
    private async void Open_Click(object sender, RoutedEventArgs e) => await OpenFileAsync();

    private async Task SnipAsync()
    {
        if (_snipping || _busy) return;
        _snipping = true;
        try
        {
            bool wasVisible = IsVisible;
            if (wasVisible)
            {
                Hide();
                await Task.Delay(250); // let the window actually disappear before we grab the screen
            }

            System.Drawing.Bitmap? bmp = null;
            string? error = null;
            try { bmp = await SnipSession.CaptureAsync(); }
            catch (Exception ex) { error = ex.Message; }

            // A cancelled snip started from the tray leaves the window hidden, as it was.
            if (wasVisible || bmp != null || error != null) ShowAndActivate();
            if (error != null) SetStatus("Couldn't capture the screen: " + error, ok: false);
            else if (bmp != null) await ProcessImageAsync(bmp);
        }
        finally
        {
            _snipping = false;
        }
    }

    private async Task PasteAsync()
    {
        var bmp = ClipboardImage.Get();
        if (bmp == null)
        {
            SetStatus("No image on the clipboard. Take a screenshot first (Win+Shift+S), then paste it here.", ok: false);
            return;
        }
        await ProcessImageAsync(bmp);
    }

    private async Task OpenFileAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open an image of a table",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        await LoadFileAsync(dlg.FileName);
    }

    private async Task LoadFileAsync(string path)
    {
        System.Drawing.Bitmap bmp;
        try { bmp = ImageUtil.LoadFile(path); }
        catch (Exception ex)
        {
            SetStatus("Couldn't open that file: " + ex.Message, ok: false);
            return;
        }
        await ProcessImageAsync(bmp);
    }

    private void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    // ------------------------------------------------------------------ OCR + table

    private async Task ProcessImageAsync(System.Drawing.Bitmap bmp)
    {
        if (!_recognizer.IsAvailable)
        {
            bmp.Dispose();
            ShowIdleStatus();
            return;
        }
        if (_busy)
        {
            bmp.Dispose();
            return;
        }

        SetBusy(true);
        try
        {
            var words = await Task.Run(() => _recognizer.RecognizeAsync(bmp));
            var preview = ImageUtil.ToBitmapSource(bmp);

            _image?.Dispose();
            _image = bmp;
            _words = words;
            PreviewImage.Source = preview;
            RebuildTable(copyToClipboard: true);
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_image, bmp)) bmp.Dispose();
            SetStatus("Couldn't read that image: " + ex.Message, ok: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private double GapFactor => Math.Pow(2, (50 - GapSlider.Value) / 50 * 1.5);

    private void RebuildTable(bool copyToClipboard)
    {
        if (_words == null) return;

        var rows = TableBuilder.Build(_words, GapFactor);

        if (_table != null) _table.DefaultView.ListChanged -= Table_ListChanged;
        var dt = new DataTable();
        int cols = rows.Length == 0 ? 0 : rows[0].Length;
        for (int c = 0; c < cols; c++) dt.Columns.Add(ColumnName(c), typeof(string));
        foreach (var r in rows) dt.Rows.Add(r.Cast<object>().ToArray());
        dt.AcceptChanges();
        _table = dt;
        Grid.ItemsSource = dt.DefaultView;
        dt.DefaultView.ListChanged += Table_ListChanged;

        ShowResultView();

        if (rows.Length == 0)
        {
            SetStatus("No text found. Try a tighter snip, or zoom the source in before snipping.", ok: false);
            return;
        }
        if (copyToClipboard) SyncClipboard();
    }

    private void SyncClipboard()
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        var rows = CurrentRows();
        if (rows.Count == 0)
        {
            SetStatus("Nothing to copy yet.", ok: false);
            return;
        }

        try { ClipboardTable.Copy(rows); }
        catch (Exception)
        {
            SetStatus("The clipboard is busy in another app. Try Copy again.", ok: false);
            return;
        }

        int cols = rows[0].Count;
        SetStatus($"Copied {rows.Count} {(rows.Count == 1 ? "row" : "rows")} × {cols} {(cols == 1 ? "column" : "columns")}. Paste into Excel or Google Sheets with Ctrl+V.", ok: true);
        CopyLabel.Text = "Copied";
        CopyIcon.Data = (Geometry)FindResource("IconCheck");
        _copiedTimer.Stop();
        _copiedTimer.Start();
    }

    private List<IReadOnlyList<string>> CurrentRows()
    {
        var list = new List<IReadOnlyList<string>>();
        if (_table == null) return list;
        int n = _table.Columns.Count;
        foreach (DataRowView rv in _table.DefaultView)
        {
            var cells = new string[n];
            for (int i = 0; i < n; i++) cells[i] = rv[i]?.ToString() ?? string.Empty;
            list.Add(cells);
        }
        return list;
    }

    private void Table_ListChanged(object? sender, ListChangedEventArgs e)
    {
        if (e.ListChangedType is ListChangedType.ItemChanged or ListChangedType.ItemDeleted)
        {
            SetStatus("Table edited. Click Copy table (or Ctrl+Shift+C) to update the clipboard.", ok: false);
            if (e.ListChangedType == ListChangedType.ItemDeleted)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Grid.Items.Refresh());
        }
    }

    private static string ColumnName(int index)
    {
        var sb = new StringBuilder();
        index++;
        while (index > 0)
        {
            index--;
            sb.Insert(0, (char)('A' + index % 26));
            index /= 26;
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ toolbar & footer

    private void Copy_Click(object sender, RoutedEventArgs e) => SyncClipboard();

    private void GapSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_words == null) return;
        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    private void SaveCsv_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        var rows = CurrentRows();
        if (rows.Count == 0) return;

        var dlg = new SaveFileDialog { Title = "Save table as CSV", Filter = "CSV file (*.csv)|*.csv", FileName = "table.csv" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, ClipboardTable.ToCsv(rows), new UTF8Encoding(true));
            SetStatus($"Saved {Path.GetFileName(dlg.FileName)}.", ok: true);
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't save: " + ex.Message, ok: false);
        }
    }

    private void TogglePreview_Click(object sender, RoutedEventArgs e)
    {
        _previewVisible = !_previewVisible;
        if (_previewVisible)
        {
            PreviewColumn.Width = _previewWidth;
            SplitterColumn.Width = new GridLength(10);
        }
        else
        {
            _previewWidth = PreviewColumn.Width;
            PreviewColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
        }
        PreviewBorder.Visibility = _previewVisible ? Visibility.Visible : Visibility.Collapsed;
        PreviewToggle.Opacity = _previewVisible ? 1 : 0.5;
    }

    // ------------------------------------------------------------------ grid plumbing

    private void Grid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.Column is DataGridTextColumn tc)
        {
            tc.ElementStyle = (Style)FindResource("CellText");
            tc.EditingElementStyle = (Style)FindResource("CellEditor");
            tc.MinWidth = 56;
            tc.MaxWidth = 520;
        }
    }

    private void Grid_LoadingRow(object sender, DataGridRowEventArgs e) =>
        e.Row.Header = (e.Row.GetIndex() + 1).ToString();

    private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
            SetStatus("Table edited. Click Copy table (or Ctrl+Shift+C) to update the clipboard.", ok: false);
    }

    private void GridBorder_SizeChanged(object sender, SizeChangedEventArgs e) =>
        GridClip.Rect = new Rect(0, 0, GridBorder.ActualWidth, GridBorder.ActualHeight);

    // ------------------------------------------------------------------ keyboard

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool editingText = Keyboard.FocusedElement is TextBox;
        if (!ctrl || editingText) return;

        if (e.Key == Key.V && !shift)
        {
            if (ClipboardImage.HasImage())
            {
                e.Handled = true;
                await PasteAsync();
            }
            else if (!Grid.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                SetStatus("No image on the clipboard. Take a screenshot first (Win+Shift+S), then paste it here.", ok: false);
            }
        }
        else if (e.Key == Key.C && (shift || !Grid.IsKeyboardFocusWithin))
        {
            if (_table == null) return;
            e.Handled = true;
            SyncClipboard();
        }
        else if (e.Key == Key.N)
        {
            e.Handled = true;
            await SnipAsync();
        }
        else if (e.Key == Key.O)
        {
            e.Handled = true;
            await OpenFileAsync();
        }
    }

    // ------------------------------------------------------------------ drag & drop

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        bool ok = !_busy && ClipboardImage.HasImage(e.Data);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropHint.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        _lastDragOver = DateTime.UtcNow;
        e.Handled = true;
    }

    private void Window_PreviewDragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between child elements; only hide if no DragOver follows.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if ((DateTime.UtcNow - _lastDragOver).TotalMilliseconds > 80)
                DropHint.Visibility = Visibility.Collapsed;
        });
    }

    private async void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (_busy) return;

        var file = ClipboardImage.FirstImageFile(e.Data);
        if (file != null)
        {
            await LoadFileAsync(file);
            return;
        }
        var bmp = ClipboardImage.Get(e.Data, fromClipboard: false);
        if (bmp != null) await ProcessImageAsync(bmp);
        else SetStatus("That doesn't look like an image.", ok: false);
    }

    // ------------------------------------------------------------------ UI state

    private void ShowResultView()
    {
        EmptyState.Visibility = Visibility.Collapsed;
        ResultView.Visibility = Visibility.Visible;
        ResultTools.Visibility = Visibility.Visible;
        FooterTools.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Toolbar.IsEnabled = !busy;
        ResultTools.IsEnabled = !busy;
        if (busy)
        {
            SetStatus("Reading the table…", ok: false);
            SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }

    private async void ShowIdleStatus()
    {
        await _recognizer.Ready;   // engines are created in the background so the window opens instantly
        if (!_recognizer.IsAvailable)
            SetStatus("No OCR engine found. Reinstall the app, or add a language with OCR in Settings › Time & language › Language & region.", ok: false);
        else
            SetStatus($"Reads text with {_recognizer.Description}, entirely on this PC. Nothing is uploaded.", ok: false);
    }

    private void SetStatus(string text, bool ok)
    {
        StatusText.Text = text;
        StatusIcon.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
    }
}
