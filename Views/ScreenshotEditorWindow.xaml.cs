using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

// 项目启用了 UseWindowsForms（全局引入 System.Windows.Forms / System.Drawing），
// 以下别名把同名 WPF 类型优先指向 PresentationFramework/PresentationCore，消解歧义。
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using MessageBox = System.Windows.MessageBox;
using Size = System.Windows.Size;
using Vector = System.Windows.Vector;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Clipboard = System.Windows.Clipboard;

namespace ImageTool.Views;

/// <summary>
/// 截图编辑器：全屏捕获 -> 唯一选区(可移动/缩放/双击裁剪) -> 标注(画笔/矩形/箭头/文字/高亮/马赛克) -> 保存/复制。
///
/// 缩放模型：Image / Overlay / SelectionCanvas 共用 CanvasGrid，缩放通过对 CanvasGrid 施加
/// LayoutTransform(ScaleTransform) 实现。所有交互坐标都在「图像像素空间」(1:1)，缩放只是视觉变换，
/// 因此无需换算；导出时临时把缩放复位到 1 再渲染，保证像素级清晰。
///
/// 选择层 SelectionCanvas 置 IsHitTestVisible=false，鼠标事件全部由 Overlay 接收，
/// 在代码里做命中测试：先判手柄(始终可缩放)，再判选区内部(选择工具时拖动=移动，其它工具时在其上画图)，
/// 最后才落到标注绘制。这样保证「唯一选区」且选区与标注互不打架。
/// </summary>
public partial class ScreenshotEditorWindow : Window
{
    private enum Tool { Select, Pen, Rect, Arrow, Text, Highlight, Mosaic }

    // 选区手柄顺序（与下方 _handleAnchors 对应）
    private static readonly string[] HandleNames = { "nw", "n", "ne", "e", "se", "s", "sw", "w" };
    // 每个手柄在选区上的锚点（相对比例）
    private static readonly (double fx, double fy)[] HandleAnchors =
    {
        (0, 0), (0.5, 0), (1, 0), (1, 0.5), (1, 1), (0.5, 1), (0, 1), (0, 0.5)
    };

    private const double HandleSize = 10;   // 手柄视觉边长（图像像素）
    private const double HitTol = 10;       // 手柄命中容差（图像像素）
    private const double MinSel = 5;        // 选区最小边长

    private Tool _tool = Tool.Pen;          // 默认激活：画笔
    private BitmapSource _source = null!;
    private readonly List<UIElement> _annotations = new();
    private readonly Stack<Action> _undoStack = new();   // 统一撤销栈（标注添加 + 裁剪均可撤销）
    private UIElement? _current;

    // 选区状态（图像空间，唯一）
    private Rect _sel;
    private bool _hasSel;          // 是否已有选区（打开时默认无，需框选才有）
    private Rectangle? _selRect;                         // 选区边框（仅视觉）
    private readonly Rectangle[] _handles = new Rectangle[8];
    private readonly Rectangle[] _dim = new Rectangle[4]; // 选区外的变暗四块
    private bool _selMoving, _selResizing, _drawingNewSel;
    private string? _activeHandle;
    private Point _selMoveStart;
    private Rect _selMoveOrig;

    // 缩放 / 颜色 / 粗细 / 文字
    private double _zoom = 1.0;
    private bool _inFit;          // 当前是否处于"适应屏幕"缩放模式
    private bool _zoomSyncing;    // 防止 UpdateZoomUI 与 ComboBox.SelectionChanged 互相递归
    private Color _color = Colors.Red;
    private int _size = 4;
    private FontFamily _fontFamily = new FontFamily("Microsoft YaHei");
    private double _fontSize = 20;

    // 箭头临时引用（整条箭头是一个实心多边形）
    private Polygon? _arrowShape;
    private Canvas? _arrowContainer;

    // 工具按钮高亮
    private Button? _activeToolBtn;
    private Button? _activeSwatch;
    private Button? _activeSizeBtn;

    public ScreenshotEditorWindow(BitmapImage source)
    {
        InitializeComponent();
        // 关键：强制图片以 1:1 像素填充画布（无论源位图 DPI 如何），
        // 否则高 DPI 屏下图片会被缩放显示，导致选区/裁剪坐标与实际像素错位。
        ScreenshotImage.Stretch = Stretch.Fill;
        SetImageSource(source);
        var w = source.PixelWidth;
        var h = source.PixelHeight;
        Overlay.Width = w; Overlay.Height = h;
        CanvasGrid.Width = w; CanvasGrid.Height = h;
        SelectionCanvas.Width = w; SelectionCanvas.Height = h;

        BuildSelectionVisuals();
        ClearSelection();          // 打开时默认无选区，需框选才有

        // 默认：画笔 + 红色 + 中号
        ActivateTool(BtnPen, Tool.Pen);
        Swatch_Click(SwRed, new RoutedEventArgs());
        BtnSize_Click(BtnSizeM, new RoutedEventArgs());

        // 字体 / 字号下拉
        foreach (var f in new[] { "Microsoft YaHei", "SimSun", "SimHei", "KaiTi", "Arial", "Consolas", "Verdana" })
            FontCombo.Items.Add(f);
        FontCombo.SelectedItem = "Microsoft YaHei";
        foreach (var s in new[] { 12, 14, 16, 18, 20, 24, 28, 32, 36, 48 })
            FontSizeCombo.Items.Add(s);
        FontSizeCombo.SelectedItem = 20;

        // 缩放交互
        Scroller.PreviewMouseWheel += Scroller_PreviewMouseWheel;
        Scroller.Loaded += (_, _) =>
            Dispatcher.BeginInvoke(new Action(FitToScreen), DispatcherPriority.Loaded);
    }

    // ===================== 导入现有图片 =====================

    private void BtnOpen_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*",
            Title = "打开图片"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(dlg.FileName);
            bmp.EndInit();
            LoadImage(bmp);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开图片：{ex.Message}", "ImageTool",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>用一张新图片替换当前内容，并完整重置编辑器状态（标注/撤销/选区/缩放）</summary>
    private void LoadImage(BitmapSource src)
    {
        // 清空现有标注与撤销历史
        foreach (var a in _annotations)
            Overlay.Children.Remove(a);
        _annotations.Clear();
        _undoStack.Clear();

        // 重置选区（整图清晰、无遮罩）
        ClearSelection();

        // 替换底层图片，并同步三个画布层尺寸（抵消源 DPI，1:1 像素）
        SetImageSource(src);
        int w = src.PixelWidth, h = src.PixelHeight;
        Overlay.Width = w; Overlay.Height = h;
        CanvasGrid.Width = w; CanvasGrid.Height = h;
        SelectionCanvas.Width = w; SelectionCanvas.Height = h;

        // 重新适应屏幕
        ZoomCombo.SelectedIndex = 0;
        FitToScreen();
    }

    // ===================== 选择层视觉 =====================

    private void BuildSelectionVisuals()
    {
        // 变暗四块（选区外）
        for (int i = 0; i < 4; i++)
        {
            _dim[i] = new Rectangle
            {
                Fill = Brushes.Black,
                Opacity = 0.4,
                IsHitTestVisible = false
            };
            SelectionCanvas.Children.Add(_dim[i]);
        }
        // 选区边框
        _selRect = new Rectangle
        {
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 5, 4 },
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        SelectionCanvas.Children.Add(_selRect);
        // 8 个调整手柄
        for (int i = 0; i < 8; i++)
        {
            _handles[i] = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = Brushes.White,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 2,
                RadiusX = 1.5,
                RadiusY = 1.5,
                IsHitTestVisible = false
            };
            SelectionCanvas.Children.Add(_handles[i]);
        }
    }

    private void ClearSelection()
    {
        // 打开时 / 选区过小 / 裁剪后：清除选区，整图清晰、无遮罩、无边框手柄
        _hasSel = false;
        _sel = Rect.Empty;
        SelectionCanvas.Visibility = Visibility.Collapsed;
    }

    private void UpdateSelectionVisuals()
    {
        if (!_hasSel)
        {
            SelectionCanvas.Visibility = Visibility.Collapsed;
            return;
        }
        SelectionCanvas.Visibility = Visibility.Visible;

        // 边框
        Canvas.SetLeft(_selRect!, _sel.X);
        Canvas.SetTop(_selRect!, _sel.Y);
        _selRect!.Width = _sel.Width;
        _selRect.Height = _sel.Height;

        // 手柄
        for (int i = 0; i < 8; i++)
        {
            var (fx, fy) = HandleAnchors[i];
            double cx = _sel.X + fx * _sel.Width;
            double cy = _sel.Y + fy * _sel.Height;
            Canvas.SetLeft(_handles[i], cx - HandleSize / 2);
            Canvas.SetTop(_handles[i], cy - HandleSize / 2);
        }

        // 变暗四块（围绕选区）
        double iw = _source.PixelWidth, ih = _source.PixelHeight;
        double l = _sel.X, t = _sel.Y, r = _sel.X + _sel.Width, b = _sel.Y + _sel.Height;
        SetRect(_dim[0], 0, 0, iw, t);                       // 上
        SetRect(_dim[1], 0, b, iw, ih - b);                  // 下
        SetRect(_dim[2], 0, t, l, b - t);                    // 左
        SetRect(_dim[3], r, t, iw - r, b - t);               // 右
    }

    private static void SetRect(Rectangle rect, double x, double y, double w, double h)
    {
        if (w < 0) { x += w; w = -w; }
        if (h < 0) { y += h; h = -h; }
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        rect.Width = w;
        rect.Height = h;
        rect.Visibility = (w <= 0.5 || h <= 0.5) ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool InsideSelection(Point p) =>
        p.X >= _sel.X && p.X <= _sel.X + _sel.Width && p.Y >= _sel.Y && p.Y <= _sel.Y + _sel.Height;

    // 返回命中手柄名；否则若命中选区内部返回 "body"；否则 null
    private string? HitTestSelection(Point p)
    {
        if (!_hasSel) return null;
        for (int i = 0; i < 8; i++)
        {
            var (fx, fy) = HandleAnchors[i];
            double cx = _sel.X + fx * _sel.Width;
            double cy = _sel.Y + fy * _sel.Height;
            double dx = p.X - cx, dy = p.Y - cy;
            if (dx * dx + dy * dy <= HitTol * HitTol) return HandleNames[i];
        }
        return InsideSelection(p) ? "body" : null;
    }

    // ===================== 工具切换 =====================

    private void ActivateTool(Button btn, Tool t)
    {
        _tool = t;
        if (_activeToolBtn != null)
            _activeToolBtn.Background = Brushes.Transparent;
        _activeToolBtn = btn;
        btn.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        // 文字选项始终可见（常驻便于随时调整字体/字号）
    }

    private void BtnSelect_Click(object s, RoutedEventArgs e) => ActivateTool(BtnSelect, Tool.Select);
    private void BtnPen_Click(object s, RoutedEventArgs e) => ActivateTool(BtnPen, Tool.Pen);
    private void BtnRect_Click(object s, RoutedEventArgs e) => ActivateTool(BtnRect, Tool.Rect);
    private void BtnArrow_Click(object s, RoutedEventArgs e) => ActivateTool(BtnArrow, Tool.Arrow);
    private void BtnText_Click(object s, RoutedEventArgs e) => ActivateTool(BtnText, Tool.Text);
    private void BtnHighlight_Click(object s, RoutedEventArgs e) => ActivateTool(BtnHighlight, Tool.Highlight);
    private void BtnMosaic_Click(object s, RoutedEventArgs e) => ActivateTool(BtnMosaic, Tool.Mosaic);
    private void BtnClose_Click(object s, RoutedEventArgs e) => Close();

    private void BtnUndo_Click(object s, RoutedEventArgs e)
    {
        // 统一撤销：按操作逆序回退（最近一次标注添加，或最近一次裁剪）
        if (_undoStack.Count == 0) return;
        _undoStack.Pop()();
    }

    private void PushUndo(Action reverse) => _undoStack.Push(reverse);

    private void RemoveAnnotation(UIElement elem)
    {
        Overlay.Children.Remove(elem);
        _annotations.Remove(elem);
    }

    private void BtnClear_Click(object s, RoutedEventArgs e)
    {
        foreach (var a in _annotations)
            Overlay.Children.Remove(a);
        _annotations.Clear();
    }

    // ---- 颜色色板 ----
    private void Swatch_Click(object s, RoutedEventArgs e)
    {
        var btn = (Button)s;
        if (_activeSwatch != null)
        {
            _activeSwatch.ClearValue(BorderBrushProperty);
            _activeSwatch.ClearValue(BorderThicknessProperty);
        }
        _activeSwatch = btn;
        btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
        btn.BorderThickness = new Thickness(2);
        _color = (Color)ColorConverter.ConvertFromString(btn.Tag?.ToString() ?? "Red")!;
    }

    // ---- 粗细预设 ----
    private void BtnSize_Click(object s, RoutedEventArgs e)
    {
        var btn = (Button)s;
        if (_activeSizeBtn != null)
            _activeSizeBtn.Background = Brushes.Transparent;
        _activeSizeBtn = btn;
        btn.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        _size = int.Parse(btn.Tag?.ToString() ?? "4");
    }

    // ===================== 缩放 =====================

    private void BtnZoomIn_Click(object s, RoutedEventArgs e) =>
        ZoomAt(1.2, new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2));
    private void BtnZoomOut_Click(object s, RoutedEventArgs e) =>
        ZoomAt(1 / 1.2, new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2));
    private void BtnFit_Click(object s, RoutedEventArgs e) => FitToScreen();

    private void Scroller_PreviewMouseWheel(object s, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            ZoomAt(e.Delta > 0 ? 1.1 : 1 / 1.1, e.GetPosition(Scroller));
        }
    }

    private void ZoomAt(double factor, Point anchor)
    {
        _inFit = false;
        var newZoom = Math.Clamp(_zoom * factor, 0.1, 8.0);
        if (Math.Abs(newZoom - _zoom) < 0.0001) return;

        double oldOffX = Scroller.HorizontalOffset;
        double oldOffY = Scroller.VerticalOffset;
        double oldZoom = _zoom;
        _zoom = newZoom;
        ImageScale.ScaleX = _zoom;
        ImageScale.ScaleY = _zoom;
        Scroller.UpdateLayout();

        double ratio = newZoom / oldZoom;
        Scroller.ScrollToHorizontalOffset((oldOffX + anchor.X) * ratio - anchor.X);
        Scroller.ScrollToVerticalOffset((oldOffY + anchor.Y) * ratio - anchor.Y);
        UpdateZoomUI();
    }

    private void FitToScreen()
    {
        double vw = Scroller.ViewportWidth, vh = Scroller.ViewportHeight;
        double iw = CanvasGrid.Width, ih = CanvasGrid.Height;
        if (vw <= 0 || vh <= 0 || iw <= 0 || ih <= 0) return;

        double fit = Math.Min(vw / iw, vh / ih);
        _zoom = Math.Min(fit, 1.0);
        ImageScale.ScaleX = _zoom;
        ImageScale.ScaleY = _zoom;
        Scroller.UpdateLayout();
        UpdateZoomUI();

        double extW = iw * _zoom, extH = ih * _zoom;
        if (extW > vw) Scroller.ScrollToHorizontalOffset((extW - vw) / 2);
        if (extH > vh) Scroller.ScrollToVerticalOffset((extH - vh) / 2);

        _inFit = true;
        UpdateZoomUI();
    }

    private void UpdateZoomUI()
    {
        ZoomLabel.Text = _inFit ? "适应" : $"{(int)Math.Round(_zoom * 100)}%";
        _zoomSyncing = true;
        if (_inFit)
        {
            ZoomCombo.SelectedIndex = 0;   // 适应屏幕
        }
        else
        {
            bool matched = false;
            foreach (ComboBoxItem item in ZoomCombo.Items)
            {
                var tag = item.Tag?.ToString();
                if (tag == "auto") continue;
                if (double.TryParse(tag, out var f) && Math.Abs(f - _zoom) < 0.005)
                {
                    ZoomCombo.SelectedItem = item;
                    matched = true;
                    break;
                }
            }
            if (!matched) ZoomCombo.Text = ZoomLabel.Text;   // 非预设值显示自定义百分比
        }
        _zoomSyncing = false;
    }

    private void ZoomCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (_zoomSyncing) return;
        if (ZoomCombo.SelectedItem is not ComboBoxItem item) return;
        var tag = item.Tag?.ToString();
        if (tag == "auto") FitToScreen();
        else if (double.TryParse(tag, out var f)) SetZoom(f);
    }

    // 以视口中心为锚点设置绝对缩放，确保能精确命中 50%/100%/150%
    private void SetZoom(double factor) =>
        ZoomAt(factor / _zoom, new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2));

    // ===================== 键盘 =====================

    private void Window_KeyDown(object s, KeyEventArgs e)
    {
        // 正在文本框输入时，让字母/快捷键直接打字，不要切工具
        if (Keyboard.FocusedElement is TextBox) return;

        if (e.Key == Key.Escape) { Close(); return; }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key is Key.OemPlus or Key.Add) { BtnZoomIn_Click(s, e); e.Handled = true; }
            else if (e.Key is Key.OemMinus or Key.Subtract) { BtnZoomOut_Click(s, e); e.Handled = true; }
            else if (e.Key is Key.D0 or Key.NumPad0) { FitToScreen(); e.Handled = true; }
            else if (e.Key == Key.Z) { BtnUndo_Click(s, e); e.Handled = true; }
        }
        else
        {
            if (e.Key == Key.V) BtnSelect_Click(s, e);
            else if (e.Key == Key.P) BtnPen_Click(s, e);
            else if (e.Key == Key.R) BtnRect_Click(s, e);
            else if (e.Key == Key.A) BtnArrow_Click(s, e);
            else if (e.Key == Key.T) BtnText_Click(s, e);
            else if (e.Key == Key.H) BtnHighlight_Click(s, e);
            else if (e.Key == Key.M) BtnMosaic_Click(s, e);
            else if (e.Key == Key.O) BtnOpen_Click(s, e);
        }
    }

    // ===================== 鼠标交互（Overlay 统一接收） =====================

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var pos = e.GetPosition(Overlay);
        _start = pos;

        // 双击选区内部 -> 裁剪（仅当有选区）
        if (e.ClickCount == 2)
        {
            if (_hasSel && InsideSelection(pos)) CropToSelection();
            return;
        }

        var hit = HitTestSelection(pos);

        // 1) 命中手柄 -> 缩放（任何工具下都可调）
        if (hit is { Length: > 0 } && hit != "body")
        {
            _selResizing = true;
            _activeHandle = hit;
            Overlay.CaptureMouse();
            return;
        }

        // 2) 命中选区内部
        if (hit == "body")
        {
            if (_tool == Tool.Select)
            {
                _selMoving = true;
                _selMoveStart = pos;
                _selMoveOrig = _sel;
                Overlay.CaptureMouse();
                return;
            }
            // 其它工具：在选区上直接绘制标注（落到下方）
        }
        else if (_tool == Tool.Select)
        {
            // 选区工具在画布上拖拽 -> 框选新选区（打开时默认无选区，需框选才有）
            _drawingNewSel = true;
            _hasSel = true;
            _sel = new Rect(pos.X, pos.Y, 0, 0);
            UpdateSelectionVisuals();
            Overlay.CaptureMouse();
            return;
        }

        // 3) 绘制标注
        _start = pos;
        if (_tool == Tool.Pen || _tool == Tool.Highlight)
        {
            var poly = new Polyline
            {
                Stroke = GetBrush(),
                StrokeThickness = _tool == Tool.Highlight ? _size + 10 : _size,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = _tool == Tool.Highlight ? 0.4 : 1
            };
            poly.Points.Add(pos);
            Overlay.Children.Add(poly);
            _current = poly;
        }
        else if (_tool == Tool.Rect)
        {
            var r = new Rectangle { Stroke = GetBrush(), StrokeThickness = _size, Fill = Brushes.Transparent };
            Canvas.SetLeft(r, pos.X);
            Canvas.SetTop(r, pos.Y);
            Overlay.Children.Add(r);
            _current = r;
        }
        else if (_tool == Tool.Arrow)
        {
            _arrowContainer = new Canvas();
            _arrowShape = new Polygon
            {
                Fill = GetBrush(),
                Stroke = GetBrush(),
                StrokeThickness = 1,
                StrokeLineJoin = PenLineJoin.Round
            };
            _arrowContainer.Children.Add(_arrowShape);
            Overlay.Children.Add(_arrowContainer);
            _current = _arrowContainer;
        }
        else if (_tool == Tool.Mosaic)
        {
            // 拖框预览：虚线矩形 + 淡填充；松开时再对矩形区域做像素化
            var r = new Rectangle
            {
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(40, 0x6B, 0x72, 0x80))
            };
            Canvas.SetLeft(r, pos.X);
            Canvas.SetTop(r, pos.Y);
            Overlay.Children.Add(r);
            _current = r;
        }
        else if (_tool == Tool.Text)
        {
            AddText(pos);   // 文字不捕获鼠标，直接落编辑框
            return;
        }

        Overlay.CaptureMouse();
    }

    private Point _start;

    private void Overlay_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(Overlay);

        if (_selResizing) { DoResize(pos); return; }
        if (_selMoving) { DoMove(pos); return; }
        if (_drawingNewSel)
        {
            double x = Math.Min(_start.X, pos.X), y = Math.Min(_start.Y, pos.Y);
            _sel = new Rect(x, y, Math.Abs(pos.X - _start.X), Math.Abs(pos.Y - _start.Y));
            UpdateSelectionVisuals();
            return;
        }

        if (_current == null) return;

        if (_tool == Tool.Pen || _tool == Tool.Highlight)
        {
            ((Polyline)_current).Points.Add(pos);
        }
        else if (_tool == Tool.Rect && _current is Rectangle r)
        {
            r.Width = Math.Abs(pos.X - _start.X);
            r.Height = Math.Abs(pos.Y - _start.Y);
            Canvas.SetLeft(r, Math.Min(pos.X, _start.X));
            Canvas.SetTop(r, Math.Min(pos.Y, _start.Y));
        }
        else if (_tool == Tool.Arrow && _arrowShape != null)
        {
            UpdateArrow(pos);
        }
        else if (_tool == Tool.Mosaic && _current is Rectangle mr)
        {
            mr.Width = Math.Abs(pos.X - _start.X);
            mr.Height = Math.Abs(pos.Y - _start.Y);
            Canvas.SetLeft(mr, Math.Min(pos.X, _start.X));
            Canvas.SetTop(mr, Math.Min(pos.Y, _start.Y));
        }
    }

    private void Overlay_MouseUp(object sender, MouseButtonEventArgs e)
    {
        Overlay.ReleaseMouseCapture();

        if (_selResizing) { _selResizing = false; _activeHandle = null; return; }
        if (_selMoving) { _selMoving = false; return; }
        if (_drawingNewSel)
        {
            _drawingNewSel = false;
            if (_sel.Width < MinSel || _sel.Height < MinSel)
                ClearSelection(); // 太小则视为未框选，清除选区
            return;
        }

        if (_current == null) return;

        if (_tool == Tool.Mosaic)
        {
            // 马赛克：移除预览矩形，对区域做像素化（直接改写底层位图，非矢量标注）
            if (_current is Rectangle mrect)
            {
                double rx = Canvas.GetLeft(mrect), ry = Canvas.GetTop(mrect);
                double rw = mrect.Width, rh = mrect.Height;
                Overlay.Children.Remove(mrect);
                if (rw >= MinSel && rh >= MinSel)
                    ApplyMosaic(new Rect(rx, ry, rw, rh));
            }
            _current = null;
            return;
        }

        if (_tool == Tool.Arrow)
        {
            var arrowC = _arrowContainer!;
            _annotations.Add(arrowC);
            PushUndo(() => RemoveAnnotation(arrowC));
            _arrowShape = null; _arrowContainer = null;
        }
        else if (_tool == Tool.Pen || _tool == Tool.Rect || _tool == Tool.Highlight)
        {
            _annotations.Add(_current);
            var added = _current;
            PushUndo(() => RemoveAnnotation(added!));
        }
        // Text / Select 不进标注列表
        _current = null;
    }

    // ---- 选区缩放 / 移动 ----

    private void DoResize(Point pos)
    {
        double iw = _source.PixelWidth, ih = _source.PixelHeight;
        double left = _sel.X, top = _sel.Y, right = _sel.X + _sel.Width, bottom = _sel.Y + _sel.Height;
        double x = Clamp(pos.X, 0, iw), y = Clamp(pos.Y, 0, ih);

        if (_activeHandle!.Contains('w')) left = x;
        if (_activeHandle.Contains('e')) right = x;
        if (_activeHandle.Contains('n')) top = y;
        if (_activeHandle.Contains('s')) bottom = y;

        double nw = right - left, nh = bottom - top;
        if (nw < MinSel) { if (_activeHandle.Contains('w')) left = right - MinSel; else right = left + MinSel; }
        if (nh < MinSel) { if (_activeHandle.Contains('n')) top = bottom - MinSel; else bottom = top + MinSel; }

        left = Clamp(left, 0, iw); right = Clamp(right, 0, iw);
        top = Clamp(top, 0, ih); bottom = Clamp(bottom, 0, ih);
        _sel = new Rect(left, top, right - left, bottom - top);
        UpdateSelectionVisuals();
    }

    private void DoMove(Point pos)
    {
        double iw = _source.PixelWidth, ih = _source.PixelHeight;
        double dx = pos.X - _selMoveStart.X;
        double dy = pos.Y - _selMoveStart.Y;
        double nl = Clamp(_selMoveOrig.X + dx, 0, iw - _selMoveOrig.Width);
        double nt = Clamp(_selMoveOrig.Y + dy, 0, ih - _selMoveOrig.Height);
        _sel = new Rect(nl, nt, _selMoveOrig.Width, _selMoveOrig.Height);
        UpdateSelectionVisuals();
    }

    private static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;

    // ---- 锥形实心箭头：尾部尖(宽≈1) → 杆逐渐变粗 → 大三角头 ----

    private void UpdateArrow(Point tip)
    {
        if (_arrowShape == null) return;
        var tail = _start;                 // 起点：尾部尖点（杆宽≈0）
        var dir = tip - tail;
        double len = dir.Length;
        if (len < 0.001) { _arrowShape.Points.Clear(); return; }

        var u = dir / len;
        var perp = new Vector(-u.Y, u.X);

        // 大三角头
        double headLen = Math.Max(22, _size * 5.5);
        double headWidth = Math.Max(20, _size * 5.0);
        // 杆在头部根部的半宽（最粗处）；明显细于三角头底边，向尾部线性收细到 ~0
        double shaftHalf = Math.Max(3, _size * 1.2);

        // 短线时按比例缩小头部，避免头比杆还长
        if (headLen > len * 0.7)
        {
            double ratio = len * 0.7 / headLen;
            headLen *= ratio;
            headWidth *= ratio;
            shaftHalf *= ratio;
        }

        var baseC = tip - u * headLen;          // 三角头根部（杆最粗处）
        var baseTop = baseC + perp * shaftHalf;
        var baseBot = baseC - perp * shaftHalf;
        var headTop = baseC + perp * (headWidth / 2);
        var headBot = baseC - perp * (headWidth / 2);

        // 整条箭头是一个实心多边形：尾部尖点 → 杆顶 → 三角头顶 → 尖 → 三角头底 → 杆底 → 回尾部
        _arrowShape.Points = new PointCollection
        {
            tail,       // 尾部尖点（杆宽≈1）
            baseTop,    // 杆顶（头部根部，最粗）
            headTop,    // 三角头顶边
            tip,        // 箭头尖
            headBot,    // 三角头底边
            baseBot     // 杆底（头部根部，最粗）
        };
    }

    // ===================== 马赛克（像素化，直接改写底层位图） =====================

    private void ApplyMosaic(Rect region)
    {
        int bSize = Math.Max(8, _size * 5);   // 块大小随粗细档位变化
        int iw = _source.PixelWidth, ih = _source.PixelHeight;
        int x = (int)Math.Round(Clamp(region.X, 0, iw - 1));
        int y = (int)Math.Round(Clamp(region.Y, 0, ih - 1));
        int w = (int)Math.Round(Clamp(region.Width, 1, iw - x));
        int h = (int)Math.Round(Clamp(region.Height, 1, ih - y));
        if (w <= 0 || h <= 0) return;

        var wb = (WriteableBitmap)_source;
        const int bpp = 4;                     // Bgra32
        int stride = (w * bpp + 3) & ~3;       // 行对齐到 4 字节
        var orig = new byte[stride * h];
        wb.CopyPixels(new Int32Rect(x, y, w, h), orig, stride, 0);

        var buf = (byte[])orig.Clone();
        Pixelate(buf, stride, w, h, bSize, bpp);
        wb.WritePixels(new Int32Rect(x, y, w, h), buf, stride, 0);

        // 撤销：把该区域原始像素写回（撤销栈 LIFO，此时 _source 仍是同一实例，安全）
        byte[] restore = orig;
        int rx = x, ry = y, rw = w, rh = h;
        PushUndo(() =>
        {
            var wbb = (WriteableBitmap)_source;
            wbb.WritePixels(new Int32Rect(rx, ry, rw, rh), restore, stride, 0);
        });
    }

    // 对 BGRA 像素缓冲按 bSize×bSize 方块做「块内平均色」像素化
    private static void Pixelate(byte[] buf, int stride, int w, int h, int bSize, int bpp)
    {
        for (int by = 0; by < h; by += bSize)
        {
            int bh = Math.Min(bSize, h - by);
            for (int bx = 0; bx < w; bx += bSize)
            {
                int bw = Math.Min(bSize, w - bx);
                int sr = 0, sg = 0, sb = 0, sa = 0, cnt = 0;
                for (int yy = 0; yy < bh; yy++)
                {
                    int row = (by + yy) * stride;
                    for (int xx = 0; xx < bw; xx++)
                    {
                        int o = row + (bx + xx) * bpp;
                        sb += buf[o]; sg += buf[o + 1]; sr += buf[o + 2]; sa += buf[o + 3]; cnt++;
                    }
                }
                byte ab = (byte)(sb / cnt), ag = (byte)(sg / cnt), ar = (byte)(sr / cnt), aa = (byte)(sa / cnt);
                for (int yy = 0; yy < bh; yy++)
                {
                    int row = (by + yy) * stride;
                    for (int xx = 0; xx < bw; xx++)
                    {
                        int o = row + (bx + xx) * bpp;
                        buf[o] = ab; buf[o + 1] = ag; buf[o + 2] = ar; buf[o + 3] = aa;
                    }
                }
            }
        }
    }

    // ---- 文字 ----

    private void AddText(Point p)
    {
        _fontFamily = new FontFamily(FontCombo.SelectedItem as string ?? "Microsoft YaHei");
        _fontSize = FontSizeCombo.SelectedItem is int fs ? fs : 20;

        var tb = new TextBox
        {
            Width = 240,
            MinWidth = 80,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            Foreground = GetBrush(),
            FontFamily = _fontFamily,
            FontSize = _fontSize,
            FontWeight = FontWeights.Bold,
            AcceptsReturn = false,
            Padding = new Thickness(2),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 0, ShadowDepth = 1, Opacity = 0.65 }
        };
        Canvas.SetLeft(tb, p.X);
        Canvas.SetTop(tb, p.Y);
        Overlay.Children.Add(tb);
        tb.Focus();
        tb.LostKeyboardFocus += (_, _) => CommitText(tb);
        tb.KeyDown += (_, ev) =>
        {
            if (ev.Key == Key.Enter)
                tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            else if (ev.Key == Key.Escape)
            {
                Overlay.Children.Remove(tb);
                ev.Handled = true;
            }
        };
    }

    private void CommitText(TextBox tb)
    {
        var text = tb.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            Overlay.Children.Remove(tb);
            return;
        }
        var blk = new TextBlock
        {
            Text = text,
            Foreground = tb.Foreground,
            FontFamily = tb.FontFamily,
            FontSize = tb.FontSize,
            FontWeight = FontWeights.Bold,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 0, ShadowDepth = 1, Opacity = 0.65 }
        };
        Canvas.SetLeft(blk, Canvas.GetLeft(tb));
        Canvas.SetTop(blk, Canvas.GetTop(tb));
        Overlay.Children.Remove(tb);
        Overlay.Children.Add(blk);
        _annotations.Add(blk);
        PushUndo(() => RemoveAnnotation(blk));
    }

    // ===================== 裁剪 =====================

    // 替换当前源位图，并同步图片元素尺寸（关键：裁剪/撤销后必须刷新 Width/Height，
    // 否则 Stretch=Fill 会把小图拉伸填满旧尺寸，导致"被放大/与选区对不上"）。
    private void SetImageSource(BitmapSource src)
    {
        var wb = ToWritableBitmap(src);
        _source = wb;
        ScreenshotImage.Source = wb;
        ScreenshotImage.Width = wb.PixelWidth;
        ScreenshotImage.Height = wb.PixelHeight;
    }

    // 统一把任意源位图转成「可写 + Bgra32(非预乘)」的 WriteableBitmap，
    // 这样马赛克/像素化可以直接改写底层像素；已是未冻结 WriteableBitmap 则原样复用。
    private static WriteableBitmap ToWritableBitmap(BitmapSource src)
    {
        if (src is WriteableBitmap w && !w.IsFrozen) return w;
        BitmapSource norm = src.Format == PixelFormats.Bgra32
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        return new WriteableBitmap(norm);
    }

    private void BtnCrop_Click(object s, RoutedEventArgs e) => CropToSelection();

    private void CropToSelection()
    {
        if (!_hasSel || _sel.Width < 1 || _sel.Height < 1) return;  // 无选区则不裁剪
        int iw = _source.PixelWidth, ih = _source.PixelHeight;
        int x = (int)Math.Round(Clamp(_sel.X, 0, iw - 1));
        int y = (int)Math.Round(Clamp(_sel.Y, 0, ih - 1));
        int w = (int)Math.Round(Clamp(_sel.Width, 1, iw - x));
        int h = (int)Math.Round(Clamp(_sel.Height, 1, ih - y));
        if (w <= 0 || h <= 0) return;

        // 记录裁剪前完整状态（图像 + 尺寸 + 各标注位置），供「撤销」精确还原
        var snapSource = _source;
        double snapW = Overlay.Width, snapH = Overlay.Height;
        var snapAnns = _annotations.Select(a => (a, Canvas.GetLeft(a), Canvas.GetTop(a))).ToList();

        var cropped = new CroppedBitmap(_source, new Int32Rect(x, y, w, h));
        cropped.Freeze();
        SetImageSource(cropped);

        // 标注整体平移到新原点，并丢弃完全落在新画布外的
        var kept = new List<UIElement>();
        foreach (var a in _annotations)
        {
            double l = Canvas.GetLeft(a) - x;
            double t = Canvas.GetTop(a) - y;
            Canvas.SetLeft(a, l);
            Canvas.SetTop(a, t);
            if (l < -2000 || t < -2000 || l > w + 2000 || t > h + 2000) Overlay.Children.Remove(a);
            else kept.Add(a);
        }
        _annotations.Clear();
        _annotations.AddRange(kept);

        Overlay.Width = w; Overlay.Height = h;
        CanvasGrid.Width = w; CanvasGrid.Height = h;
        SelectionCanvas.Width = w; SelectionCanvas.Height = h;

        // 撤销裁剪：还原图像、尺寸与全部标注位置
        PushUndo(() =>
        {
            SetImageSource(snapSource);
            Overlay.Width = snapW; Overlay.Height = snapH;
            CanvasGrid.Width = snapW; CanvasGrid.Height = snapH;
            SelectionCanvas.Width = snapW; SelectionCanvas.Height = snapH;
            Overlay.Children.Clear();
            _annotations.Clear();
            foreach (var (elem, l, t) in snapAnns)
            {
                Canvas.SetLeft(elem, l);
                Canvas.SetTop(elem, t);
                Overlay.Children.Add(elem);
                _annotations.Add(elem);
            }
            ClearSelection();
            FitToScreen();
        });

        ClearSelection();  // 裁剪后重置为新整图（无选区，需重新框选）
        FitToScreen();
    }

    // ===================== 导出（始终按当前选区裁剪） =====================

    private Brush GetBrush() => new SolidColorBrush(_color);

    private BitmapSource RenderComposite()
    {
        // 内容坐标始终是「图像像素空间」(1:1)，缩放只是屏幕视觉变换。
        // 离屏导出要在 1:1 渲染，需抵消两点：
        //  1) RenderTargetBitmap 必须用 96 DPI(WPF 逻辑 DPI) 渲染，使「逻辑尺寸 w == 像素尺寸 w」。
        //     若用 _source.DpiX（高 DPI 屏下可能≠96），WPF 会把内容按 DpiX/96 放大渲染，
        //     位图缓冲区却只有 w 像素宽，于是只截到左上角一部分、内容被放大（典型故障）。
        //  2) 标注层 Overlay 继承 CanvasGrid 的缩放 LayoutTransform(ImageScale)，VisualBrush 会把它
        //     一起渲染，需用逆变换抵消，保证标注 1:1 落在正确位置（缩放≠1 时尤其明显）。
        // 整个过程完全不动屏幕上的 CanvasGrid / ImageScale / 滚动条。
        int w = (int)Math.Round(CanvasGrid.Width);
        int h = (int)Math.Round(CanvasGrid.Height);

        var root = new Grid { Width = w, Height = h, SnapsToDevicePixels = true };

        // 1) 图片：强制以像素尺寸填充，保证 1:1
        var img = new System.Windows.Controls.Image
        {
            Source = _source,
            Width = w,
            Height = h,
            Stretch = Stretch.Fill
        };
        root.Children.Add(img);

        // 2) 标注层：用 VisualBrush 引用屏幕上的 Overlay（不改动它，屏幕标注不受影响）
        var overlayBrush = new VisualBrush(Overlay)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, w, h),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, w, h)
        };
        if (Math.Abs(_zoom - 1.0) > 0.0001)
            overlayBrush.Transform = new ScaleTransform(1 / _zoom, 1 / _zoom);
        root.Children.Add(new Rectangle { Width = w, Height = h, Fill = overlayBrush });

        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        // 固定 96 DPI：逻辑尺寸 == 像素尺寸，1:1 像素级清晰
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);

        // 按当前选区裁剪；无选区则导出整图
        if (!_hasSel)
            return rtb;
        int iw = _source.PixelWidth, ih = _source.PixelHeight;
        int x = (int)Math.Round(Clamp(_sel.X, 0, iw - 1));
        int y = (int)Math.Round(Clamp(_sel.Y, 0, ih - 1));
        int cw = (int)Math.Round(Clamp(_sel.Width, 1, iw - x));
        int ch = (int)Math.Round(Clamp(_sel.Height, 1, ih - y));
        if (cw >= w && ch >= h) return rtb;

        var finalCrop = new CroppedBitmap(rtb, new Int32Rect(x, y, cw, ch));
        finalCrop.Freeze();
        return finalCrop;
    }

    private static void SaveBitmapSource(BitmapSource src, string path)
    {
        BitmapEncoder enc = System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
        enc.Frames.Add(BitmapFrame.Create(src));
        using var fs = new System.IO.FileStream(path, System.IO.FileMode.Create);
        enc.Save(fs);
    }

    private void BtnSave_Click(object s, RoutedEventArgs e)
    {
        var src = RenderComposite();
        var dlg = new SaveFileDialog
        {
            Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp",
            FileName = $"截图_{DateTime.Now:yyyyMMdd_HHmmss}"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                SaveBitmapSource(src, dlg.FileName);
                MessageBox.Show("已保存：" + dlg.FileName, "截图编辑");
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败：" + ex.Message, "截图编辑");
            }
        }
    }

    private void BtnCopy_Click(object s, RoutedEventArgs e)
    {
        var src = RenderComposite();
        Clipboard.SetImage(src);
        MessageBox.Show("已复制到剪贴板。", "截图编辑");
    }
}
