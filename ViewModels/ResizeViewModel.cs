using System;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using ImageTool.Models;
using ImageTool.Services;

namespace ImageTool.ViewModels;

public class ResizeViewModel : ViewModelBase
{
    private readonly IImageService _service;
    private string _inputPath = "";
    private string _outputPath = "";
    private ResizeMode _mode = ResizeMode.Width;
    private int _width = 800;
    private int _height = 600;
    private float _percentage = 50;
    private int _quality = 90;
    private double _progress;
    private bool _isBusy;
    private string _status = "请选择一张图片";
    private BitmapImage? _preview;

    // 选图后的基本属性（用于「图片信息」面板）
    private int _srcW, _srcH;
    private double _srcDpiX, _srcDpiY;
    private long _fileBytes;
    private string _format = "";

    // Exact 模式：锁定比例 + 实时最终效果预览
    private bool _lockAspectRatio = true;
    private bool _suppressRatio;
    private double _finalPreviewW, _finalPreviewH;

    public ResizeViewModel(IImageService service) => _service = service;

    public ResizeMode[] Modes => Enum.GetValues<ResizeMode>();

    public string InputPath { get => _inputPath; set { if (Set(ref _inputPath, value)) { UpdateDefaultOutput(); CommandManager.InvalidateRequerySuggested(); } } }
    public string OutputPath { get => _outputPath; set => Set(ref _outputPath, value); }
    public ResizeMode Mode
    {
        get => _mode;
        set
        {
            if (Set(ref _mode, value))
            {
                // 切换模式后，各字段的「是否生效」随之变化，需通知界面刷新可用态
                OnPropertyChanged(nameof(WidthEnabled));
                OnPropertyChanged(nameof(HeightEnabled));
                OnPropertyChanged(nameof(PercentageEnabled));
                OnPropertyChanged(nameof(LockRatioVisible));
                OnPropertyChanged(nameof(FinalPreviewVisible));
                // 进入 Exact 且锁定比例时，以当前宽度为基准重建高度，避免初始框就畸变
                if (value == ResizeMode.Exact && _lockAspectRatio && _srcW > 0)
                {
                    _suppressRatio = true;
                    _height = Math.Max(1, (int)Math.Round(_width * (_srcH / (double)_srcW)));
                    OnPropertyChanged(nameof(Height));
                    _suppressRatio = false;
                }
                UpdateFinalPreview();
            }
        }
    }

    /// <summary>「宽度」在当前模式下是否生效（Width/Exact 模式生效）</summary>
    public bool WidthEnabled => _mode is ResizeMode.Width or ResizeMode.Exact;
    /// <summary>「高度」在当前模式下是否生效（Height/Exact 模式生效）</summary>
    public bool HeightEnabled => _mode is ResizeMode.Height or ResizeMode.Exact;
    /// <summary>「百分比」在当前模式下是否生效（Percentage 模式生效）</summary>
    public bool PercentageEnabled => _mode == ResizeMode.Percentage;

    /// <summary>「锁定比例」勾选框仅在 Exact 模式可见</summary>
    public bool LockRatioVisible => _mode == ResizeMode.Exact;
    /// <summary>最终效果预览仅在 Exact 模式且已选图时可见</summary>
    public bool FinalPreviewVisible => _mode == ResizeMode.Exact && _srcW > 0;
    /// <summary>最终效果预览框显示宽度（px，按目标比例缩放到 ≤200）</summary>
    public double FinalPreviewWidth => _finalPreviewW;
    /// <summary>最终效果预览框显示高度（px）</summary>
    public double FinalPreviewHeight => _finalPreviewH;
    /// <summary>最终效果预览说明文字，如 "800 × 600 px"</summary>
    public string FinalPreviewText => _mode == ResizeMode.Exact && _srcW > 0 ? $"{_width} × {_height} px" : "";

    /// <summary>是否锁定原图宽高比（仅 Exact 模式有意义，默认开启）</summary>
    public bool LockAspectRatio
    {
        get => _lockAspectRatio;
        set
        {
            if (Set(ref _lockAspectRatio, value))
            {
                // 开启锁定时，以当前宽度为基准重建高度，保证不畸变
                if (value && _mode == ResizeMode.Exact && _srcW > 0)
                {
                    _suppressRatio = true;
                    _height = Math.Max(1, (int)Math.Round(_width * (_srcH / (double)_srcW)));
                    OnPropertyChanged(nameof(Height));
                    _suppressRatio = false;
                }
                UpdateFinalPreview();
            }
        }
    }

    public int Width
    {
        get => _width;
        set
        {
            if (Set(ref _width, value))
            {
                // Exact + 锁定：改宽自动算高，保持原图比例
                if (!_suppressRatio && _mode == ResizeMode.Exact && _lockAspectRatio && _srcW > 0 && _width > 0)
                {
                    _suppressRatio = true;
                    _height = Math.Max(1, (int)Math.Round(_width * (_srcH / (double)_srcW)));
                    OnPropertyChanged(nameof(Height));
                    _suppressRatio = false;
                }
                UpdateFinalPreview();
            }
        }
    }

    public int Height
    {
        get => _height;
        set
        {
            if (Set(ref _height, value))
            {
                // Exact + 锁定：改高自动算宽，保持原图比例
                if (!_suppressRatio && _mode == ResizeMode.Exact && _lockAspectRatio && _srcW > 0 && _height > 0)
                {
                    _suppressRatio = true;
                    _width = Math.Max(1, (int)Math.Round(_height * (_srcW / (double)_srcH)));
                    OnPropertyChanged(nameof(Width));
                    _suppressRatio = false;
                }
                UpdateFinalPreview();
            }
        }
    }
    public float Percentage { get => _percentage; set => Set(ref _percentage, value); }
    public int Quality { get => _quality; set => Set(ref _quality, value); }
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public BitmapImage? Preview { get => _preview; set => Set(ref _preview, value); }

    // 图片基本属性（选图后填充，用于信息面板）
    public string DimensionsText => _srcW > 0 ? $"{_srcW} × {_srcH} px" : "";
    public string DpiText => _srcW > 0 ? $"{Math.Round(_srcDpiX)} × {Math.Round(_srcDpiY)} DPI" : "";
    public string FormatText => _format;
    public string FileSizeText => FormatBytes(_fileBytes);
    public bool HasImageInfo => _srcW > 0;

    public ICommand BrowseInputCommand => new RelayCommand(BrowseInput);
    public ICommand BrowseOutputCommand => new RelayCommand(BrowseOutput);
    public ICommand RunCommand => new AsyncRelayCommand(RunAsync, () => !IsBusy && File.Exists(InputPath));

    private void BrowseInput()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
        };
        if (dlg.ShowDialog() == true)
        {
            InputPath = dlg.FileName;
            LoadPreview();
        }
    }

    private void BrowseOutput()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG|*.png|JPEG|*.jpg|WebP|*.webp|BMP|*.bmp",
            FileName = OutputPath
        };
        if (dlg.ShowDialog() == true)
            OutputPath = dlg.FileName;
    }

    private void UpdateDefaultOutput()
    {
        if (string.IsNullOrWhiteSpace(InputPath))
            return;
        var dir = Path.GetDirectoryName(InputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(InputPath);
        OutputPath = Path.Combine(dir, name + "_resized" + Path.GetExtension(InputPath));
    }

    private void LoadPreview()
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(InputPath);
            bmp.EndInit();
            Preview = bmp;

            // 解析基本属性：尺寸 / 分辨率 / 格式 / 文件大小
            _srcW = bmp.PixelWidth;
            _srcH = bmp.PixelHeight;
            _srcDpiX = bmp.DpiX;
            _srcDpiY = bmp.DpiY;
            _format = Path.GetExtension(InputPath)?.TrimStart('.').ToUpperInvariant() ?? "";
            try { _fileBytes = new FileInfo(InputPath).Length; }
            catch { _fileBytes = 0; }
            // 选图后若处于 Exact + 锁定，以原图比例初始化高度
            if (_mode == ResizeMode.Exact && _lockAspectRatio && _srcW > 0)
            {
                _suppressRatio = true;
                _height = Math.Max(1, (int)Math.Round(_width * (_srcH / (double)_srcW)));
                OnPropertyChanged(nameof(Height));
                _suppressRatio = false;
            }
            RaiseImageInfo();
            UpdateFinalPreview();
        }
        catch
        {
            Preview = null;
            _srcW = _srcH = 0;
            _format = "";
            _fileBytes = 0;
            RaiseImageInfo();
            UpdateFinalPreview();
        }
    }

    private void RaiseImageInfo()
    {
        OnPropertyChanged(nameof(DimensionsText));
        OnPropertyChanged(nameof(DpiText));
        OnPropertyChanged(nameof(FormatText));
        OnPropertyChanged(nameof(FileSizeText));
        OnPropertyChanged(nameof(HasImageInfo));
    }

    /// <summary>
    /// 计算「最终效果预览」框的显示尺寸：以目标宽高为基准，按最长边 ≤200px 缩放，
    /// 但保持目标纵横比 —— 这样框本身的比例就反映了最终成像（比例不对会直观看到拉伸/挤压）。
    /// </summary>
    private void UpdateFinalPreview()
    {
        if (_mode == ResizeMode.Exact && _srcW > 0)
        {
            const double maxBox = 200;
            double tw = Math.Max(1, _width);
            double th = Math.Max(1, _height);
            double scale = Math.Min(maxBox / tw, maxBox / th);
            _finalPreviewW = Math.Max(1, tw * scale);
            _finalPreviewH = Math.Max(1, th * scale);
        }
        else
        {
            _finalPreviewW = 0;
            _finalPreviewH = 0;
        }
        OnPropertyChanged(nameof(FinalPreviewWidth));
        OnPropertyChanged(nameof(FinalPreviewHeight));
        OnPropertyChanged(nameof(FinalPreviewText));
        OnPropertyChanged(nameof(FinalPreviewVisible));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "";
        string[] units = { "B", "KB", "MB", "GB" };
        int i = 0;
        double v = bytes;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : $"{v:F1} {units[i]}";
    }

    private async Task RunAsync()
    {
        IsBusy = true;
        Progress = 0;
        Status = "正在缩放...";
        try
        {
            var opts = new ResizeOptions
            {
                Mode = Mode,
                Width = Width,
                Height = Height,
                Percentage = Percentage,
                Quality = Quality
            };
            if (string.IsNullOrWhiteSpace(OutputPath))
                UpdateDefaultOutput();

            var prog = new Progress<double>(p => Progress = p);
            await Task.Run(() => _service.Resize(InputPath, OutputPath, opts, prog));
            Status = $"完成 → {OutputPath}";
        }
        catch (Exception ex)
        {
            Status = "错误：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
