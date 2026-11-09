using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

/// <summary>
/// 界面截图工具：真实显示一次窗体，渲染成 PNG 保存，然后自动关闭。
/// 用途：检查控件是否重叠、挤压、超出边界。
/// 用法： Shot.exe &lt;exe路径&gt; &lt;输出png&gt; [宽] [高] [显示毫秒]
/// </summary>
internal static class Shot
{
    [STAThread]
    private static int Main(string[] args)
    {
        string exe = Path.GetFullPath(args[0]);
        string png = Path.GetFullPath(args[1]);
        int w = args.Length > 2 ? int.Parse(args[2]) : 1280;
        int h = args.Length > 3 ? int.Parse(args[3]) : 800;
        int delay = args.Length > 4 ? int.Parse(args[4]) : 1500;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Assembly asm = Assembly.UnsafeLoadFrom(exe);
        Form form = (Form)Activator.CreateInstance(asm.GetType("WordClassify.MainForm"));
        form.ClientSize = new Size(w, h);

        Timer t = new Timer();
        t.Interval = delay;
        t.Tick += delegate
        {
            t.Stop();
            try
            {
                form.Refresh();
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(png, ImageFormat.Png);
                }
                Console.WriteLine("已保存：" + png + "  (" + form.Width + "x" + form.Height + ")");
                DumpOverlaps(form);
            }
            catch (Exception ex)
            {
                Console.WriteLine("截图失败：" + ex.Message);
            }
            form.Close();
        };
        t.Start();
        Application.Run(form);
        return 0;
    }

    /// <summary>列出同级控件里互相重叠的组合，帮助定位"堆叠"问题。</summary>
    private static void DumpOverlaps(Control root)
    {
        int found = 0;
        foreach (Control parent in AllControls(root))
        {
            if (parent.Controls.Count < 2) continue;
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                for (int j = i + 1; j < parent.Controls.Count; j++)
                {
                    Control a = parent.Controls[i];
                    Control b = parent.Controls[j];
                    if (!a.Visible || !b.Visible) continue;
                    if (!a.Bounds.IntersectsWith(b.Bounds)) continue;
                    // 完全包含（容器套子控件）不算重叠
                    if (a.Bounds.Contains(b.Bounds) || b.Bounds.Contains(a.Bounds)) continue;
                    found++;
                    Console.WriteLine("重叠: [" + parent.GetType().Name + " " + parent.Name + "] "
                        + Describe(a) + "  <->  " + Describe(b));
                }
            }
            // 超出父容器边界的子控件（跳过会自动出现滚动条的容器，那种"越界"是正常的可滚动内容）
            foreach (Control c in parent.Controls)
            {
                if (!c.Visible) continue;
                if (IsScrollHost(parent)) continue;
                if (c.Right > parent.ClientSize.Width + 2 || c.Bottom > parent.ClientSize.Height + 2)
                {
                    found++;
                    Console.WriteLine("越界: [" + parent.GetType().Name + " " + parent.Name + " 客户区 "
                        + parent.ClientSize.Width + "x" + parent.ClientSize.Height + "] " + Describe(c));
                }
            }
        }
        Console.WriteLine(found == 0 ? "布局检查：没有发现重叠或越界" : "布局检查：发现 " + found + " 处问题");
    }

    /// <summary>会自动滚动或本身就是布局容器的控件，子控件超出可视区属正常现象。</summary>
    private static bool IsScrollHost(Control c)
    {
        ScrollableControl sc = c as ScrollableControl;
        if (sc != null && sc.AutoScroll) return true;
        if (c is FlowLayoutPanel) return true;          // FlowLayoutPanel 常靠滚动承载内容
        return false;
    }

    private static System.Collections.Generic.IEnumerable<Control> AllControls(Control root)
    {
        yield return root;
        foreach (Control c in root.Controls)
            foreach (Control d in AllControls(c))
                yield return d;
    }

    private static string Describe(Control c)
    {
        string text = c.Text == null ? "" : c.Text.Replace("\r", " ").Replace("\n", " ");
        if (text.Length > 24) text = text.Substring(0, 24) + "…";
        return c.GetType().Name + "(\"" + text + "\") " + c.Bounds;
    }
}
