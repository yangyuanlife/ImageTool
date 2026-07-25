using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace ImageTool.Services;

/// <summary>
/// 截图捕获服务：捕获整个虚拟屏幕（含多显示器）为 WPF BitmapImage。
/// </summary>
public class ScreenshotService
{
    /// <summary>捕获全虚拟屏</summary>
    public BitmapImage CaptureFullScreen()
    {
        var bounds = SystemInformation.VirtualScreen;
        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return ToBitmapImage(bmp);
    }

    /// <summary>Bitmap -> WPF BitmapImage（冻结以便跨线程/多窗口使用）</summary>
    public static BitmapImage ToBitmapImage(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }
}
