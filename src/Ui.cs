using System;
using System.Drawing;
using System.Windows.Forms;

namespace WordClassify
{
    /// <summary>
    /// 界面尺寸与字体的统一缩放中心。
    ///
    /// 背景：早期版本把像素值写死在代码里（左栏 600、卡片 382、按钮 26 …），
    /// 在 96 DPI 下正常，但显示器缩放为 125%/150% 时字体变大而像素不变，
    /// 于是标签被截断、按钮文字被切掉、控件看起来"挤在一起"。
    ///
    /// 现在的做法：
    ///   · 启动时算出 DPI 缩放比例 Scale（96 DPI = 1.0，120 DPI = 1.25，144 DPI = 1.5）
    ///   · 所有尺寸都写成 S(设计值)，运行时按比例放大
    ///   · 字体也由这里统一创建，并关掉 WinForms 自带的 AutoScale，避免缩放两次
    /// </summary>
    public static class Ui
    {
        private static float _scale = 1.0f;
        private static string _fontFamily = "Microsoft YaHei UI";
        private static bool _initialized;

        /// <summary>当前 DPI 缩放比例（1.0 = 100%，1.25 = 125%，1.5 = 150%）。</summary>
        public static float Scale { get { return _scale; } }

        public static string FontFamily { get { return _fontFamily; } }

        /// <summary>
        /// 仅供自动化测试：强制指定缩放比例，用来在 96 DPI 的机器上验证
        /// 125%/150% 显示器下的界面布局。正式运行不会调用。
        /// </summary>
        public static void ForceScaleForTesting(float scale)
        {
            if (scale < 0.75f) scale = 0.75f;
            if (scale > 3.0f) scale = 3.0f;
            _scale = scale;
            _initialized = true;
            _fontFamily = PickFont();
        }

        /// <summary>
        /// 用主显示器 DPI 初始化。必须在创建任何窗体之前调用。
        /// 环境变量 WCT_DPI_SCALE=1.5 可强制指定缩放比例，用于在 96 DPI 机器上验证
        /// 125%/150% 显示器下的布局（自动化测试用）。
        /// </summary>
        public static void Initialize()
        {
            Initialize(false);
        }

        /// <summary>force = true 时忽略"已初始化"标记，重新读取缩放（自动化测试用）。</summary>
        public static void Initialize(bool force)
        {
            if (_initialized && !force) return;
            _initialized = true;

            float forced = 0f;
            try
            {
                string env = Environment.GetEnvironmentVariable("WCT_DPI_SCALE");
                if (!string.IsNullOrEmpty(env))
                {
                    float parsed;
                    if (float.TryParse(env, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out parsed) && parsed > 0f)
                        forced = parsed;
                }
            }
            catch (Exception) { }

            if (forced > 0f)
            {
                _scale = forced;
            }
            else
            {
                try
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                        _scale = g.DpiX / 96f;
                }
                catch (Exception)
                {
                    _scale = 1.0f;
                }
            }
            if (_scale < 0.75f) _scale = 0.75f;
            if (_scale > 3.0f) _scale = 3.0f;

            // 选一个系统里确实存在的中文字体，避免落到默认字体导致字宽估算偏差
            _fontFamily = PickFont();
        }

        private static string PickFont()
        {
            string[] candidates = new string[]
            {
                "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "SimHei", "SimSun"
            };
            foreach (string name in candidates)
            {
                try
                {
                    using (Font f = new Font(name, 9F))
                    {
                        if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return name;
                    }
                }
                catch (Exception) { }
            }
            return "Microsoft YaHei";
        }

        /// <summary>把设计像素换算成当前 DPI 下的像素。</summary>
        public static int S(int designPixels)
        {
            int v = (int)Math.Round(designPixels * _scale);
            return v < 0 ? 0 : v;
        }

        /// <summary>把设计像素换算成当前 DPI 下的浮点值（给 ColumnStyle/RowStyle 用）。</summary>
        public static float SF(float designPixels)
        {
            return designPixels * _scale;
        }

        /// <summary>统一的界面字体。</summary>
        public static Font Font(float sizePoints)
        {
            return new Font(_fontFamily, sizePoints);
        }

        public static Font Font(float sizePoints, FontStyle style)
        {
            return new Font(_fontFamily, sizePoints, style);
        }

        /// <summary>等宽字体（日志区用）。</summary>
        public static Font MonoFont(float sizePoints)
        {
            try
            {
                return new Font("Consolas", sizePoints);
            }
            catch (Exception)
            {
                return new Font(FontFamily, sizePoints);
            }
        }

        /// <summary>按当前 DPI 缩放的固定行高（用于给列表行、按钮做统一高度）。</summary>
        public static int RowHeight(int designPixels)
        {
            return S(designPixels);
        }
    }
}
