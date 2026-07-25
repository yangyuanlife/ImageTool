using SixLabors.ImageSharp.PixelFormats;
using RgbaImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

namespace ImageTool.Services;

/// <summary>
/// 纯像素级圆角处理（不依赖 SixLabors.ImageSharp.Drawing 包）。
/// 预览与正式导出共用同一套算法，保证「所见即所得」。
/// </summary>
public static class ImageSharpRoundedCorners
{
    /// <summary>
    /// 对图像做圆角处理（原地图操作）。圆角外的像素：
    /// transparentOutside=true 则置透明(alpha=0)，否则填白。
    /// radius 为原图像素单位；超过短边一半时自动截断。
    /// </summary>
    public static void Apply(RgbaImage image, float radius, bool transparentOutside)
    {
        int w = image.Width, h = image.Height;
        float r = Math.Min(radius, Math.Min(w, h) / 2f);
        if (r <= 0) return;

        // 使用 Image<TPixel> 的 [x,y] 索引器写入（确定可写，避开 ProcessPixelRows 行 span 的不确定性）
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!InsideRoundedRect(x, y, w, h, r))
                {
                    if (transparentOutside)
                        image[x, y] = new Rgba32(0, 0, 0, 0);          // 透明
                    else
                        image[x, y] = new Rgba32(255, 255, 255);      // 白底
                }
            }
        }
    }

    /// <summary>
    /// 判断像素 (x,y) 是否在 (0,0,w,h) 的圆角矩形内（四角用半径 r 的圆形裁切）。
    /// </summary>
    private static bool InsideRoundedRect(int x, int y, int w, int h, float r)
    {
        if (r <= 0) return true;

        float cx, cy;
        if (x < r && y < r) { cx = r; cy = r; }                         // 左上
        else if (x >= w - r && y < r) { cx = w - r; cy = r; }           // 右上
        else if (x < r && y >= h - r) { cx = r; cy = h - r; }           // 左下
        else if (x >= w - r && y >= h - r) { cx = w - r; cy = h - r; }  // 右下
        else return true;                                               // 直边/中间区域

        float dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy <= r * r;
    }
}
