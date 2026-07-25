using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using ImageTool.Models;
using ImageTool.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Rgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;
using RgbaImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

namespace ImageTool.ViewModels;

public class ConvertViewModel : ViewModelBase
{
    private readonly IImageService _service;
    private string _inputPath = "";
    private string _outputPath = "";
    private string _targetFormat = "png";
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

    private bool _roundedCorners;
    private int _cornerRadius = 24;
    private int _cornerRadiusMax = 200;
    private readonly ObservableCollection<IcoSizeItem> _icoSizes = new();

    public ConvertViewModel(IImageService service)
    {
        _service = service;
        foreach (var s in new[] { 16, 32, 48, 64, 128, 256 })
            _icoSizes.Add(new IcoSizeItem(s, true));
    }

    public string[] Formats => new[] { "png", "jpg", "webp", "bmp", "ico" };

    public string InputPath { get => _inputPath; set { if (Set(ref _inputPath, value)) { UpdateDefaultOutput(); CommandManager.InvalidateRequerySuggested(); } } }
    public string OutputPath { get => _outputPath; set => Set(ref _outputPath, value); }
    public string TargetFormat
    {
        get => _targetFormat;
        set
        {
            if (Set(ref _targetFormat, value))
            {
                UpdateDefaultOutput();
                OnPropertyChanged(nameof(ShowIcoSizes));
                UpdatePreview();
            }
        }
    }
    public int Quality { get => _quality; set => Set(ref _quality, value); }

    // ---- 圆角 ----
    public bool RoundedCorners
    {
        get => _roundedCorners;
        set { if (Set(ref _roundedCorners, value)) UpdatePreview(); }
    }
    public int CornerRadius
    {
        get => _cornerRadius;
        set { if (Set(ref _cornerRadius, value)) UpdatePreview(); }
    }
    /// <summary>圆角可调上限：随图片短边动态放大（最大为短边一半，即「全圆/胶囊」极限），小图自动收窄。无图时回落到默认 200。</summary>
    public int CornerRadiusMax
    {
        get => _cornerRadiusMax;
        set => Set(ref _cornerRadiusMax, value);
    }

    // ---- ICO 多尺寸 ----
    public ObservableCollection<IcoSizeItem> IcoSizes => _icoSizes;
    public bool ShowIcoSizes => _targetFormat == "ico";

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
            UpdatePreview();
            LoadImageInfo();
        }
    }

    private void BrowseOutput()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG|*.png|JPEG|*.jpg|WebP|*.webp|BMP|*.bmp|ICO|*.ico",
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
        var ext = _targetFormat == "ico" ? "ico" : _targetFormat;
        OutputPath = Path.Combine(dir, name + "_converted." + ext);
    }

    /// <summary>
    /// 用与正式导出同一套圆角算法生成预览（缩放≤600 以保证实时流畅；圆角按比例换算，
    /// 使预览圆角视觉比例与最终导出一致）。圆角外按目标格式决定透明或白底。
    /// </summary>
    private void UpdatePreview()
    {
        if (string.IsNullOrEmpty(InputPath) || !File.Exists(InputPath)) { Preview = null; return; }
        try
        {
            using var src = SixLabors.ImageSharp.Image.Load<Rgba32>(InputPath);
            const int maxPrev = 600;
            float scale = 1f;
            if (src.Width > maxPrev || src.Height > maxPrev)
            {
                scale = maxPrev / (float)Math.Max(src.Width, src.Height);
                int pw = Math.Max(1, (int)(src.Width * scale));
                int ph = Math.Max(1, (int)(src.Height * scale));
                src.Mutate(x => x.Resize(pw, ph));
            }

            if (RoundedCorners && CornerRadius > 0)
            {
                float previewRadius = Math.Max(0, CornerRadius * scale);
                bool transparentOutside = IsTransparentTarget(_targetFormat);
                ImageSharpRoundedCorners.Apply(src, previewRadius, transparentOutside);
            }

            using var ms = new MemoryStream();
            src.Save(ms, new PngEncoder());
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            Preview = bmp;
        }
        catch
        {
            Preview = null;
        }
    }

    private static bool IsTransparentTarget(string format) =>
        format is "png" or "webp" or "ico";

    /// <summary>解析所选图片的基本属性（尺寸/DPI/格式/文件大小），供信息面板显示</summary>
    private void LoadImageInfo()
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(InputPath);
            bmp.EndInit();

            _srcW = bmp.PixelWidth;
            _srcH = bmp.PixelHeight;

            // 圆角可调上限随图片尺寸放大：最大可达短边的一半（半圆/胶囊效果），
            // 小图则自动收窄；若当前半径超过新上限则夹紧，避免滑块越界。
            int maxR = Math.Max(1, Math.Min(_srcW, _srcH) / 2);
            if (_cornerRadius > maxR) CornerRadius = maxR;
            CornerRadiusMax = maxR;

            _srcDpiX = bmp.DpiX;
            _srcDpiY = bmp.DpiY;
            _format = Path.GetExtension(InputPath)?.TrimStart('.').ToUpperInvariant() ?? "";
            try { _fileBytes = new FileInfo(InputPath).Length; }
            catch { _fileBytes = 0; }
        }
        catch
        {
            _srcW = _srcH = 0;
            _format = "";
            _fileBytes = 0;
            CornerRadiusMax = 200;
        }
        RaiseImageInfo();
    }

    private void RaiseImageInfo()
    {
        OnPropertyChanged(nameof(DimensionsText));
        OnPropertyChanged(nameof(DpiText));
        OnPropertyChanged(nameof(FormatText));
        OnPropertyChanged(nameof(FileSizeText));
        OnPropertyChanged(nameof(HasImageInfo));
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
        Status = "正在转换...";
        try
        {
            if (string.IsNullOrWhiteSpace(OutputPath))
                UpdateDefaultOutput();

            var options = new ConvertOptions
            {
                Quality = Quality,
                RoundedCorners = RoundedCorners,
                CornerRadius = CornerRadius,
                ToIco = _targetFormat == "ico",
                IcoSizes = IcoSizes.Where(s => s.IsSelected).Select(s => s.Size).ToArray()
            };

            var prog = new Progress<double>(p => Progress = p);
            await Task.Run(() => _service.Convert(InputPath, OutputPath, options, prog));
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

/// <summary>ICO 可多选的尺寸项（与 UI 的 CheckBox 双向绑定）。</summary>
public class IcoSizeItem : ViewModelBase
{
    public int Size { get; }
    private bool _selected;
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
    public IcoSizeItem(int size, bool selected) { Size = size; _selected = selected; }
}
