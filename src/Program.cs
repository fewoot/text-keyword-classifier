using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WordClassify
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
#if CONSOLE
            SelfTest.UseUtf8Console();
#endif
            if (args != null && args.Length > 0)
            {
                string a0 = args[0].ToLowerInvariant();
                if (a0 == "--selftest" || a0 == "-t" || a0 == "/selftest")
                    return SelfTest.Run();
                if (a0 == "--help" || a0 == "-h" || a0 == "/?")
                {
                    SelfTest.UseUtf8Console();
                    Console.WriteLine("文本关键词分类工具");
                    Console.WriteLine();
                    Console.WriteLine("用法：");
                    Console.WriteLine("  关键词分类工具.exe              启动图形界面");
                    Console.WriteLine("  关键词分类工具.exe --selftest   运行核心算法自检（不写文件、不改数据）");
                    Console.WriteLine();
                    Console.WriteLine("配置保存在程序目录下的 config.json。");
                    return 0;
                }
            }

            EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // 必须在创建任何窗体之前初始化：读取显示器 DPI，之后所有尺寸都按这个比例缩放
            Ui.Initialize();

            try
            {
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序启动失败：" + ex.Message, "文本关键词分类工具",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        /// <summary>尽量开启高 DPI 感知，避免 125%/150% 缩放下界面发虚。</summary>
        private static void EnableDpiAwareness()
        {
            try
            {
                // Windows 10 1703+ ：Per-Monitor V2
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch (Exception) { }
            try
            {
                SetProcessDPIAware();
            }
            catch (Exception) { }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }
}
