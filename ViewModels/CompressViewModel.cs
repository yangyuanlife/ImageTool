using System;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using ImageTool.Models;
using ImageTool.Services;

namespace ImageTool.ViewModels;

public class CompressViewModel : ViewModelBase
{
    private readonly IImageService _service;
    private string _inputPath = "";
    private string _outputPath = "";
    private string _targetFormat = "原格式";
    private int _quality = 80;
    private double _progress;
    private bool _isBusy;
    private string _status = "请选择一张图片";
    private BitmapImage? _preview;

    // 选图后的基本属性（用于「图片信息」面板）
    private int _srcW, _srcH;
    private double _srcDpiX, _srcDpiY;
    private long _fileBytes;
    private string _format = "";

    public CompressViewModel(IImageService service) => _service = service;

    /// <summary>目标格式：原格式 / png / jpg / webp / bmp</summary>
    public string[] Formats => new[] { "原格式", "png", "jpg", "webp", "bmp" };

    public string InputPath { get => _inputPath; set { if (Set(ref _inputPath, value)) { UpdateDefaultOutput(); CommandManager.InvalidateRequerySuggested(); } } }
    public string OutputPath { get => _outputPath; set => Set(ref _outputPath, value); }
    public string TargetFormat { get => _targetFormat; set { if (Set(ref _targetFormat, value)) UpdateDefaultOutput(); } }
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
        var filter = TargetFormat == "原格式"
            ? "PNG|*.png|JPEG|*.jpg|WebP|*.webp|BMP|*.bmp"
            : TargetFormat.ToUpperInvariant() + "|*." + TargetFormat;
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = OutputPath };
        if (dlg.ShowDialog() == true)
            OutputPath = dlg.FileName;
    }

    private void UpdateDefaultOutput()
    {
        if (string.IsNullOrWhiteSpace(InputPath))
            return;
        var dir = Path.GetDirectoryName(InputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(InputPath);
        string ext = TargetFormat == "原格式" ? Path.GetExtension(InputPath) : "." + TargetFormat;
        OutputPath = Path.Combine(dir, name + "_compressed" + ext);
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
        }
        catch
        {
            Preview = null;
            _srcW = _srcH = 0;
            _format = "";
            _fileBytes = 0;
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
        Status = "正在压缩...";
        try
        {
            if (string.IsNullOrWhiteSpace(OutputPath))
                UpdateDefaultOutput();

            var inputSize = new FileInfo(InputPath).Length;
            var prog = new Progress<double>(p => Progress = p);
            await Task.Run(() => _service.Compress(InputPath, OutputPath, new CompressOptions { Quality = Quality }, prog));

            var outputSize = new FileInfo(OutputPath).Length;
            var saved = inputSize > 0 ? (1 - (double)outputSize / inputSize) * 100 : 0;
            Status = $"完成 → {OutputPath}\n原始 {inputSize / 1024} KB → {outputSize / 1024} KB（节省 {saved:0.0}%）";
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
