using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// 验证"运行时窗口标题栏/任务栏"用的确实是新图标：
///   启动 exe → 等主窗口 → 用 WM_GETICON 取大/小图标 → 存 PNG → 关闭进程
///
/// 用法： VerifyWindowIcon.exe &lt;exe&gt; &lt;输出目录&gt;
/// </summary>
internal static class VerifyWindowIcon
{
    private const int WM_GETICON = 0x007F;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

    private const int GCLP_HICON = -14;

    private static int Main(string[] args)
    {
        string exe = Path.GetFullPath(args[0]);
        string dir = Path.GetFullPath(args[1]);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        Console.WriteLine("=== 验证窗口图标 ===");
        Console.WriteLine("exe：" + exe);

        // 用 UseShellExecute=false 直接 CreateProcess：避免触发"打开文件 - 安全警告"对话框
        Process p = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
        try
        {
            IntPtr hwnd = IntPtr.Zero;
            for (int i = 0; i < 50; i++)
            {
                Thread.Sleep(150);
                p.Refresh();
                if (p.MainWindowHandle != IntPtr.Zero) { hwnd = p.MainWindowHandle; break; }
            }
            if (hwnd == IntPtr.Zero)
            {
                Console.WriteLine("[失败] 等不到主窗口");
                return 1;
            }
            Console.WriteLine("[通过] 主窗口已出现，句柄 " + hwnd);
            Thread.Sleep(400);   // 等图标设置完成

            IntPtr small = SendMessage(hwnd, WM_GETICON, ICON_SMALL, 0);
            IntPtr big = SendMessage(hwnd, WM_GETICON, ICON_BIG, 0);
            IntPtr cls = GetClassLongPtr(hwnd, GCLP_HICON);
            Console.WriteLine("  WM_GETICON 小图标句柄：" + small);
            Console.WriteLine("  WM_GETICON 大图标句柄：" + big);
            Console.WriteLine("  窗口类图标句柄：" + cls);

            bool any = false;
            any |= SaveIcon(dir, "win-small.png", small, "标题栏小图标");
            any |= SaveIcon(dir, "win-big.png", big, "任务栏大图标");
            if (!any) any |= SaveIcon(dir, "win-class.png", cls, "窗口类图标");

            return any ? 0 : 1;
        }
        finally
        {
            try { if (!p.HasExited) p.Kill(); } catch (Exception) { }
            try { p.Dispose(); } catch (Exception) { }
        }
    }

    private static bool SaveIcon(string dir, string name, IntPtr handle, string label)
    {
        if (handle == IntPtr.Zero)
        {
            Console.WriteLine("  [跳过] " + label + "：句柄为空");
            return false;
        }
        try
        {
            using (Icon ico = Icon.FromHandle(handle))
            using (Bitmap bmp = ico.ToBitmap())
            {
                bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
                int opaque = 0;
                for (int y = 0; y < bmp.Height; y += 2)
                    for (int x = 0; x < bmp.Width; x += 2)
                        if (bmp.GetPixel(x, y).A > 200) opaque++;
                Console.WriteLine("  [通过] " + label + " " + ico.Width + "x" + ico.Height
                    + "，不透明采样点 " + opaque);
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  [失败] " + label + "：" + ex.Message);
            return false;
        }
    }
}
