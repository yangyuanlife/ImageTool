using ImageTool.Models;

namespace ImageTool.Services;

/// <summary>
/// 图片处理服务接口。所有方法为同步 CPU 操作，调用方应在后台线程执行并传递进度回调。
/// </summary>
public interface IImageService
{
    /// <summary>调整大小</summary>
    void Resize(string inputPath, string outputPath, ResizeOptions options, IProgress<double>? progress = null);

    /// <summary>格式转换（由 outputPath 的扩展名决定目标格式）</summary>
    void Convert(string inputPath, string outputPath, int quality = 90, IProgress<double>? progress = null);

    /// <summary>格式转换（含圆角、ICO 多尺寸等高级选项）</summary>
    void Convert(string inputPath, string outputPath, ConvertOptions options, IProgress<double>? progress = null);

    /// <summary>智能压缩：PNG 量化降色 + 去元数据；JPG/WebP 按质量有损压缩（由 outputPath 扩展名决定目标格式）</summary>
    void Compress(string inputPath, string outputPath, CompressOptions options, IProgress<double>? progress = null);
}
