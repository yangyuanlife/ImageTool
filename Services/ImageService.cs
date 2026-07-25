using System.IO;
using System.Linq;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using ImageTool.Models;

using SImage = SixLabors.ImageSharp.Image;
using SRectangle = SixLabors.ImageSharp.Rectangle;
using RgbaImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;
using Rgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace ImageTool.Services;

/// <summary>
/// 基于 SixLabors.ImageSharp 的图片处理实现。免费 MIT，跨平台，纯 C#。
/// </summary>
public class ImageService : IImageService
{
    public void Resize(string inputPath, string outputPath, Models.ResizeOptions options, IProgress<double>? progress = null)
    {
        progress?.Report(0);
        using var image = Load(inputPath);
        int w = image.Width, h = image.Height;

        (int nw, int nh) = options.Mode switch
        {
            Models.ResizeMode.Width => (options.Width, (int)Math.Round(h * (options.Width / (double)w))),
            Models.ResizeMode.Height => ((int)Math.Round(w * (options.Height / (double)h)), options.Height),
            Models.ResizeMode.Percentage => ((int)Math.Round(w * options.Percentage / 100), (int)Math.Round(h * options.Percentage / 100)),
            _ => (options.Width, options.Height)
        };
        nw = Math.Max(1, nw);
        nh = Math.Max(1, nh);

        image.Mutate(x => x.Resize(nw, nh));
        SaveTo(image, outputPath, GetEncoder(outputPath, options.Quality));
        progress?.Report(100);
    }

    public void Convert(string inputPath, string outputPath, int quality = 90, IProgress<double>? progress = null)
        => Convert(inputPath, outputPath, new ConvertOptions { Quality = quality }, progress);

    public void Convert(string inputPath, string outputPath, ConvertOptions options, IProgress<double>? progress = null)
    {
        progress?.Report(0);

        bool toIco = options.ToIco
                     || Path.GetExtension(outputPath).Equals(".ico", StringComparison.OrdinalIgnoreCase);

        // ---- ICO 多尺寸输出 ----
        if (toIco)
        {
            using var src = SImage.Load<Rgba32>(inputPath);
            if (options.RoundedCorners && options.CornerRadius > 0)
                ImageSharpRoundedCorners.Apply(src, options.CornerRadius, transparentOutside: true);

            var sizes = (options.IcoSizes is { Length: > 0 } ? options.IcoSizes : new[] { 16, 32, 48, 64, 128, 256 })
                .Distinct().OrderBy(s => s).ToArray();

            var frames = new List<RgbaImage>(sizes.Length);
            try
            {
                foreach (var s in sizes)
                    frames.Add(src.Clone(ctx => ctx.Resize(s, s)));
                IcoFileWriter.SaveAsIco(outputPath, frames);
            }
            finally
            {
                foreach (var f in frames) f.Dispose();
            }
            progress?.Report(100);
            return;
        }

        // ---- 普通格式（可选圆角） ----
        using var image = SImage.Load<Rgba32>(inputPath);
        bool transparentTarget = IsTransparentFormat(outputPath);
        // 透明格式(png/webp)圆角外透明；不透明格式(jpg/bmp)圆角外填白
        if (options.RoundedCorners && options.CornerRadius > 0)
            ImageSharpRoundedCorners.Apply(image, options.CornerRadius, transparentTarget);

        SaveTo(image, outputPath, GetEncoder(outputPath, options.Quality));
        progress?.Report(100);
    }

    private static bool IsTransparentFormat(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".webp";
    }

    public void Compress(string inputPath, string outputPath, CompressOptions options, IProgress<double>? progress = null)
    {
        progress?.Report(0);
        using var image = Load(inputPath);

        // 剥离 EXIF/IPTC/XMP 元数据以减小体积（保留 ICC 以维持色彩准确）
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;

        SaveTo(image, outputPath, GetCompressEncoder(outputPath, options.Quality));
        progress?.Report(100);
    }

    private static SImage Load(string path)
    {
        using var fs = File.OpenRead(path);
        return SImage.Load(fs);
    }

    private static void SaveTo(SImage image, string path, IImageEncoder encoder)
    {
        using var fs = File.Create(path);
        image.Save(fs, encoder);
    }

    private static IImageEncoder GetEncoder(string path, int quality)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => new JpegEncoder { Quality = quality },
            ".webp" => new WebpEncoder { Quality = quality },
            ".bmp" => new BmpEncoder(),
            _ => new PngEncoder()
        };
    }

    /// <summary>
    /// 压缩用编码器：PNG 在质量 &lt; 100 时量化到调色板（TinyPNG 同类思路）；JPG/WebP 按质量有损。
    /// </summary>
    private static IImageEncoder GetCompressEncoder(string path, int quality)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => quality >= 100
                ? new PngEncoder()
                : new PngEncoder
                {
                    ColorType = PngColorType.Palette,
                    Quantizer = new WuQuantizer(new QuantizerOptions { MaxColors = MapQualityToColors(quality) })
                },
            ".jpg" or ".jpeg" => new JpegEncoder { Quality = quality },
            ".webp" => new WebpEncoder { Quality = quality },
            ".bmp" => new BmpEncoder(),
            _ => new PngEncoder()
        };
    }

    private static int MapQualityToColors(int quality)
    {
        // quality 1..99 映射到 16..256 色（质量越高颜色越多、越清晰）
        int c = (int)Math.Round(quality / 100.0 * 256);
        return Math.Clamp(c, 16, 256);
    }
}
