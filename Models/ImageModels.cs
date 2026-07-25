namespace ImageTool.Models;

/// <summary>
/// 缩放模式
/// </summary>
public enum ResizeMode
{
    /// <summary>按宽度，高度等比</summary>
    Width,
    /// <summary>按高度，宽度等比</summary>
    Height,
    /// <summary>按百分比</summary>
    Percentage,
    /// <summary>指定宽高（可能变形）</summary>
    Exact
}

/// <summary>
/// 缩放选项
/// </summary>
public class ResizeOptions
{
    public ResizeMode Mode { get; set; } = ResizeMode.Width;
    public int Width { get; set; } = 800;
    public int Height { get; set; } = 600;
    public float Percentage { get; set; } = 50;
    public int Quality { get; set; } = 90;
}

/// <summary>
/// 压缩选项：质量 1-100。
/// PNG 目标下质量映射为调色板颜色数（越大越清晰、体积越大）；JPG/WebP 直接作为编码器质量。
/// </summary>
public class CompressOptions
{
    public int Quality { get; set; } = 80;
}

/// <summary>
/// 格式转换选项：
/// - Quality：编码质量 1-100（PNG/BMP/ICO 忽略）。
/// - RoundedCorners / CornerRadius：是否启用圆角及圆角半径（原图像素单位）。圆角外像素：透明目标格式(png/webp/ico)透明，不透明格式(jpg/bmp)填白。
/// - ToIco / IcoSizes：是否输出 ICO 及包含的多个分辨率（如 16/32/48/64/128/256）。
/// </summary>
public class ConvertOptions
{
    public int Quality { get; set; } = 90;
    public bool RoundedCorners { get; set; }
    public int CornerRadius { get; set; } = 24;
    public bool ToIco { get; set; }
    public int[] IcoSizes { get; set; } = { 16, 32, 48, 64, 128, 256 };
}
