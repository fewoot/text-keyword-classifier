using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

/// <summary>
/// 布局诊断：在指定尺寸下构造主窗体，打印左侧面板各控件的实际位置与尺寸，
/// 并标出"被压扁"（宽或高小于可用阈值）与"文字放不下"的控件。
/// 用法： Probe.exe &lt;exe路径&gt; [宽x高 ...]
/// </summary>
internal static class Probe
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (Exception) { }

        string exe = args[0];
        List<Size> sizes = new List<Size>();
        if (args.Length > 1)
        {
            for (int i = 1; i < args.Length; i++)
            {
                string[] p = args[i].Split('x');
                sizes.Add(new Size(int.Parse(p[0]), int.Parse(p[1])));
            }
        }
        else
        {
            sizes.Add(new Size(1103, 590));   // 用户截图的实际尺寸
            sizes.Add(new Size(1400, 900));
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Assembly asm = Assembly.UnsafeLoadFrom(exe);
        Type mainForm = asm.GetType("WordClassify.MainForm");

        foreach (Size size in sizes)
        {
            Console.WriteLine("================ 客户区 " + size.Width + " x " + size.Height + " ================");
            Form form = (Form)Activator.CreateInstance(mainForm);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-5000, -5000);
            form.ClientSize = size;
            form.Show();
            Application.DoEvents();
            form.PerformLayout();
            Application.DoEvents();

            Console.WriteLine("实际客户区：" + form.ClientSize.Width + " x " + form.ClientSize.Height);
            Console.WriteLine();

            // 类别编辑器内部
            Control editor = Find(form, "CategoryEditor");
            if (editor != null)
            {
                Console.WriteLine("类别编辑器： " + editor.Bounds);
                Dump(editor, 1);
            }
            else
            {
                Console.WriteLine("!! 找不到 CategoryEditor");
            }

            Console.WriteLine();
            Console.WriteLine("--- 文字放不下的控件 ---");
            int bad = 0;
            foreach (Control c in All(form))
            {
                if (!c.Visible) continue;
                string text = c.Text;
                if (string.IsNullOrEmpty(text)) continue;
                Size need = TextRenderer.MeasureText(text, c.Font);
                if (c is Button && need.Width > c.Width - 4)
                {
                    bad++;
                    Console.WriteLine("  按钮文字被截: \"" + Clip(text) + "\" 需要 " + need.Width
                        + "px 实际 " + c.Width + "px");
                }
                else if (c is Label && !((Label)c).AutoSize && need.Width > c.Width)
                {
                    bad++;
                    Console.WriteLine("  标签文字被截: \"" + Clip(text) + "\" 需要 " + need.Width
                        + "px 实际 " + c.Width + "px");
                }
                else if (c is TextBox && need.Width > c.Width - 8)
                {
                    bad++;
                    Console.WriteLine("  输入框太窄: 内容需要 " + need.Width + "px 实际 " + c.Width + "px");
                }
            }
            if (bad == 0) Console.WriteLine("  没有发现问题");

            Console.WriteLine();
            Console.WriteLine("--- 被压扁到不可用的控件 ---");
            int squeezed = 0;
            foreach (Control c in All(form))
            {
                if (!c.Visible) continue;
                if (c is ListBox && (c.Height < 60 || c.Width < 110))
                {
                    squeezed++;
                    Console.WriteLine("  列表被压扁: " + c.Bounds);
                }
                if (c is Button && c.Height > 40)
                {
                    squeezed++;
                    Console.WriteLine("  按钮被拉伸变形: \"" + Clip(c.Text) + "\" " + c.Bounds);
                }
                if (c is TextBox && !((TextBox)c).Multiline && c.Height > 34)
                {
                    squeezed++;
                    Console.WriteLine("  输入框被拉伸变形: " + c.Bounds);
                }
            }
            if (squeezed == 0) Console.WriteLine("  没有发现问题");
            Console.WriteLine();

            form.Close();
            form.Dispose();
            Application.DoEvents();
        }
        return 0;
    }

    private static void Dump(Control root, int depth)
    {
        string pad = new string(' ', depth * 2);
        foreach (Control c in root.Controls)
        {
            string text = c.Text == null ? "" : c.Text.Replace("\r", " ").Replace("\n", " ");
            Console.WriteLine(pad + c.GetType().Name + " " + c.Bounds
                + (text.Length > 0 ? "  \"" + Clip(text) + "\"" : ""));
            if (depth < 4) Dump(c, depth + 1);
        }
    }

    private static string Clip(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 22 ? s.Substring(0, 22) + "…" : s;
    }

    private static Control Find(Control root, string typeName)
    {
        foreach (Control c in root.Controls)
        {
            if (c.GetType().Name == typeName) return c;
            Control f = Find(c, typeName);
            if (f != null) return f;
        }
        return null;
    }

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control d in All(c)) yield return d;
        }
    }
}
