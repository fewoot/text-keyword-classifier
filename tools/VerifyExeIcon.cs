using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

/// <summary>
/// 校验 exe 里真的带上了图标：用三种系统 API 取图标并存成 PNG，
/// 便于和 assets\app.ico 的帧做肉眼对比。
///   1. Icon.ExtractAssociatedIcon —— 资源管理器"大图标"用的那一个
///   2. ExtractIconEx(large)        —— 桌面/资源管理器大图标
///   3. ExtractIconEx(small)        —— 任务栏/标题栏小图标
///
/// 用法： VerifyExeIcon.exe &lt;exe&gt; &lt;输出目录&gt;
/// </summary>
internal static class VerifyExeIcon
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static int Main(string[] args)
    {
        string exe = Path.GetFullPath(args[0]);
        string dir = Path.GetFullPath(args[1]);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        Console.WriteLine("=== 校验 exe 内嵌图标 ===");
        Console.WriteLine("exe：" + exe + "  (" + new FileInfo(exe).Length + " 字节)");

        // 1) 关联图标
        try
        {
            using (Icon ico = Icon.ExtractAssociatedIcon(exe))
            {
                if (ico == null) { Console.WriteLine("[失败] ExtractAssociatedIcon 返回空"); }
                else
                {
                    using (Bitmap bmp = ico.ToBitmap())
                        bmp.Save(Path.Combine(dir, "exe-associated.png"), ImageFormat.Png);
                    Console.WriteLine("[通过] 关联图标 " + ico.Width + "x" + ico.Height);
                }
            }
        }
        catch (Exception ex) { Console.WriteLine("[失败] 关联图标：" + ex.Message); }

        // 2) ExtractIconEx 大/小图标
        IntPtr[] large = new IntPtr[1];
        IntPtr[] small = new IntPtr[1];
        int n = ExtractIconEx(exe, 0, large, small, 1);
        Console.WriteLine("[信息] ExtractIconEx 返回图标数：" + n);

        SaveHandle(dir, "exe-large.png", large[0], "大图标");
        SaveHandle(dir, "exe-small.png", small[0], "小图标");

        // 3) 简单判定：图标是否非空（不是全透明）
        foreach (string f in Directory.GetFiles(dir, "exe-*.png"))
        {
            using (Bitmap b = new Bitmap(f))
            {
                int opaque = 0;
                for (int y = 0; y < b.Height; y += 2)
                    for (int x = 0; x < b.Width; x += 2)
                        if (b.GetPixel(x, y).A > 200) opaque++;
                Console.WriteLine("  " + Path.GetFileName(f) + " " + b.Width + "x" + b.Height
                    + " 不透明采样点 " + opaque + (opaque > 10 ? "  [非空]" : "  [疑似空图标]"));
            }
        }
        return 0;
    }

    private static void SaveHandle(string dir, string name, IntPtr handle, string label)
    {
        if (handle == IntPtr.Zero)
        {
            Console.WriteLine("[失败] " + label + " 句柄为空");
            return;
        }
        try
        {
            using (Icon ico = Icon.FromHandle(handle))
            using (Bitmap bmp = ico.ToBitmap())
            {
                bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
                Console.WriteLine("[通过] " + label + " " + ico.Width + "x" + ico.Height);
            }
        }
        catch (Exception ex) { Console.WriteLine("[失败] " + label + "：" + ex.Message); }
        finally { DestroyIcon(handle); }
    }
}
