using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WordClassify
{
    /// <summary>
    /// 主窗口。
    ///
    /// 布局全部用 TableLayoutPanel / FlowLayoutPanel / Dock 完成，不使用坐标硬定位，
    /// 这样窗口缩放、不同 DPI、不同字体下都不会出现控件挤压或重叠
    /// （早期版本用绝对坐标，左侧面板被压缩后所有控件叠在一起，已废弃）。
    /// </summary>
    public class MainForm : Form
    {
        private const int LeftWidth = 600;

        private static readonly Color Accent = Color.FromArgb(43, 87, 154);
        private static readonly Color PageBack = Color.FromArgb(243, 245, 248);

        private readonly Settings _settings;
        private CategoryEditor _editor;
        private TextBox _sourceBox;
        private TextBox _outputBox;
        private TextBox _logBox;
        private ProgressBarEx _progressBar;
        private DataGridView _grid;
        private BindingList<FileResult> _rows = new BindingList<FileResult>();
        private Label _lblCurrent;
        private Label _lblCounters;
        private Label _lblStats;
        private Label _lblElapsed;
        private Label _statusLabel;
        private Button _startButton;
        private Button _pauseButton;
        private Button _previewButton;
        private CheckBox _chkRecurse, _chkDocx, _chkTxt, _chkInsertMarker, _chkFirstOnly, _chkSkipNoMatch, _chkIgnoreCase;
        private ComboBox _cmbOrder, _cmbCount;
        private System.Windows.Forms.Timer _timer;
        private volatile Processor _processor;
        private Thread _worker;
        private volatile bool _running;
        private volatile bool _paused;
        private volatile bool _previewMode;
        private List<FileResult> _collected = new List<FileResult>();
        private StringBuilder _logBuffer = new StringBuilder();

        public MainForm()
        {
            _settings = Settings.Load();

            Text = "文本关键词分类工具";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Ui.S(1400), Ui.S(900));
            // 左栏三张卡片最低需要约 730 设计像素高，低于这个高度内容会被压扁，所以限制窗口最小尺寸
            MinimumSize = new Size(Ui.S(1180), Ui.S(880));
            Font = Ui.Font(9F);
            BackColor = PageBack;
            // 尺寸缩放全部由 Ui 负责，关掉 WinForms 自带的缩放，避免缩放两次导致文字被挤压
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(46)));   // 顶栏
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));         // 中部
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(62)));   // 底栏

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildCenter(), 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);
            Controls.Add(root);

            ApplyAppIcon();
            BuildTimer();
            LoadSettingsToUi();

            Log("就绪。先选源文件夹和输出文件夹，配好类别与关键词，然后点「开始处理」。");
            Log("建议先点「预览」看一遍每个文件会变成什么名字，确认没问题再正式处理。");
        }

        // ==================== 顶栏 ====================

        /// <summary>
        /// 把 exe 里嵌的图标挂到窗体上（标题栏和任务栏都用它）。
        /// 编译时通过 /win32icon 嵌入了 assets\app.ico；万一取不到就沿用默认图标。
        /// </summary>
        private void ApplyAppIcon()
        {
            try
            {
                string exe = Application.ExecutablePath;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    Icon appIcon = Icon.ExtractAssociatedIcon(exe);
                    if (appIcon != null) Icon = appIcon;
                }
            }
            catch (Exception)
            {
                // 取不到图标不影响使用
            }
        }

        private Control BuildHeader()
        {
            TableLayoutPanel bar = new TableLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.BackColor = Accent;
            bar.ColumnCount = 2;
            bar.RowCount = 1;
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.SF(264)));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bar.Margin = new Padding(0);

            Label title = new Label();
            title.Text = "文本关键词分类工具";
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Font = Ui.Font(11.5F, FontStyle.Bold);
            title.ForeColor = Color.White;
            title.Padding = new Padding(Ui.S(16), 0, 0, 0);

            Label note = new Label();
            note.Text = "原文件不会被改动　·　结果按「类名N次-原名称」输出到指定文件夹";
            note.Dock = DockStyle.Fill;
            note.TextAlign = ContentAlignment.MiddleLeft;
            note.ForeColor = Color.FromArgb(214, 226, 246);
            note.AutoEllipsis = true;

            bar.Controls.Add(title, 0, 0);
            bar.Controls.Add(note, 1, 0);
            return bar;
        }

        // ==================== 中部 ====================

        private Control BuildCenter()
        {
            TableLayoutPanel center = new TableLayoutPanel();
            center.Dock = DockStyle.Fill;
            center.ColumnCount = 2;
            center.RowCount = 1;
            center.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.SF(LeftWidth)));
            center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            center.Padding = new Padding(Ui.S(10), Ui.S(10), Ui.S(10), Ui.S(4));
            center.BackColor = PageBack;

            center.Controls.Add(BuildLeftPanel(), 0, 0);
            center.Controls.Add(BuildRightPanel(), 1, 0);
            return center;
        }

        private Control BuildLeftPanel()
        {
            // 三张卡片的高度：文件夹 150 + 类别 382 + 选项 150 + 间距 = 约 730
            TableLayoutPanel left = new TableLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.ColumnCount = 1;
            left.RowCount = 3;
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(150)));  // 文件夹
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(382)));  // 类别
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));         // 选项
            left.Margin = new Padding(0, 0, Ui.S(8), 0);
            // 窗口低于约 730 设计像素时整栏滚动，而不是把内容压扁
            left.AutoScroll = true;
            left.AutoScrollMinSize = new Size(Ui.S(LeftWidth - 60), Ui.S(738));

            Control folder = BuildFolderCard();
            folder.MinimumSize = new Size(0, Ui.S(150));

            Control category = BuildCategoryCard();
            category.MinimumSize = new Size(0, Ui.S(382));

            Control options = BuildOptionsCard();
            options.MinimumSize = new Size(0, Ui.S(150));

            left.Controls.Add(folder, 0, 0);
            left.Controls.Add(category, 0, 1);
            left.Controls.Add(options, 0, 2);
            return left;
        }

        private Control BuildFolderCard()
        {
            TableLayoutPanel card = MakeCard("① 文件夹");
            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.ColumnCount = 1;
            content.RowCount = 3;
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(36)));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(36)));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _sourceBox = MakePathBox();
            _outputBox = MakePathBox();

            content.Controls.Add(MakeFolderRow("源文件夹", _sourceBox,
                delegate { BrowseFolder(_sourceBox, true); }, "选择要处理的文档所在文件夹"), 0, 0);
            content.Controls.Add(MakeFolderRow("输出文件夹", _outputBox,
                delegate { BrowseFolder(_outputBox, false); }, "选择处理后文件的存放位置"), 0, 1);

            Label hint = new Label();
            hint.Dock = DockStyle.Fill;
            hint.Text = "输出文件夹留空 = 就在源文件夹里生成新文件（原文件依然不动）";
            hint.ForeColor = Color.Gray;
            hint.TextAlign = ContentAlignment.MiddleLeft;
            content.Controls.Add(hint, 0, 2);

            FillCard(card, content);
            return card;
        }

        private TextBox MakePathBox()
        {
            TextBox tb = new TextBox();
            tb.Dock = DockStyle.Fill;
            tb.Margin = new Padding(0, Ui.S(6), Ui.S(6), Ui.S(6));
            tb.ReadOnly = true;
            tb.BackColor = Color.White;
            tb.TabStop = false;
            return tb;
        }

        private Control MakeFolderRow(string label, TextBox box, EventHandler onBrowse, string tip)
        {
            TableLayoutPanel row = new TableLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.ColumnCount = 3;
            row.RowCount = 1;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.SF(78)));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.SF(116)));
            row.Margin = new Padding(0);

            Label lb = new Label();
            lb.Text = label;
            lb.Dock = DockStyle.Fill;
            lb.TextAlign = ContentAlignment.MiddleLeft;

            Button browse = new Button();
            browse.Text = "浏览文件夹…";
            browse.Dock = DockStyle.Fill;
            browse.Margin = new Padding(0, Ui.S(4), 0, Ui.S(4));
            browse.FlatStyle = FlatStyle.System;
            browse.Click += onBrowse;

            ToolTip tips = new ToolTip();
            tips.SetToolTip(browse, tip);
            tips.SetToolTip(box, tip);

            row.Controls.Add(lb, 0, 0);
            row.Controls.Add(box, 1, 0);
            row.Controls.Add(browse, 2, 0);
            return row;
        }

        private Control BuildCategoryCard()
        {
            TableLayoutPanel card = MakeCard("② 检索类别与关键词");
            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.ColumnCount = 1;
            content.RowCount = 2;
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(34)));

            _editor = new CategoryEditor(_settings.Categories);
            _editor.Dock = DockStyle.Fill;
            _editor.CategoriesChanged += delegate { UpdateCategorySummary(); };

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0, Ui.S(2), 0, 0);
            buttons.Controls.Add(MakeSmallButton("导入配置", delegate { ImportConfig(); }, 88));
            buttons.Controls.Add(MakeSmallButton("导出配置", delegate { ExportConfig(); }, 88));
            buttons.Controls.Add(MakeSmallButton("保存配置", delegate { SaveSettings(true); }, 88));

            content.Controls.Add(_editor, 0, 0);
            content.Controls.Add(buttons, 0, 1);

            FillCard(card, content);
            return card;
        }

        private Control BuildOptionsCard()
        {
            TableLayoutPanel card = MakeCard("③ 处理选项");
            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.ColumnCount = 1;
            content.RowCount = 3;
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(62)));   // 选项（双列排布）
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(32)));   // 两个下拉框
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));         // 提示

            // 双列流式排布：窗口窄时自动折行，不会把勾选框挤出可视区
            FlowLayoutPanel checks = new FlowLayoutPanel();
            checks.Dock = DockStyle.Fill;
            checks.FlowDirection = FlowDirection.LeftToRight;
            checks.WrapContents = true;
            checks.AutoScroll = true;
            checks.Margin = new Padding(0);

            _chkInsertMarker = MakeCheck("插入类别标记", true);
            _chkInsertMarker.Text = "在正文里插入类别标记（例：〔123〕我爱你）";
            _chkFirstOnly = MakeCheck("每个类别只标第一次出现", true);
            _chkRecurse = MakeCheck("递归处理子文件夹", true);
            _chkTxt = MakeCheck("处理 .txt 文件", true);
            _chkDocx = MakeCheck("处理 .docx 文件", true);
            _chkSkipNoMatch = MakeCheck("无命中不生成新文件", true);
            _chkIgnoreCase = MakeCheck("英文忽略大小写", false);

            ToolTip tips = new ToolTip();
            tips.SetToolTip(_chkInsertMarker, "命中关键词时在正文里插成 〔标记〕关键词");
            tips.SetToolTip(_chkFirstOnly, "取消勾选 = 每次出现都插标记");
            tips.SetToolTip(_chkRecurse, "子文件夹里的文件也会处理，输出保持同样目录结构");
            tips.SetToolTip(_chkSkipNoMatch, "一个类别都没命中的文件直接跳过");

            checks.Controls.Add(_chkInsertMarker);
            checks.Controls.Add(_chkRecurse);
            checks.Controls.Add(_chkFirstOnly);
            checks.Controls.Add(_chkTxt);
            checks.Controls.Add(_chkDocx);
            checks.Controls.Add(_chkSkipNoMatch);
            checks.Controls.Add(_chkIgnoreCase);

            TableLayoutPanel combos = new TableLayoutPanel();
            combos.Dock = DockStyle.Fill;
            combos.ColumnCount = 4;
            combos.RowCount = 1;
            combos.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.SF(112)));
            combos.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            combos.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.SF(88)));
            combos.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            _cmbOrder = MakeCombo();
            _cmbOrder.Items.Add("按类别列表顺序");
            _cmbOrder.Items.Add("按命中次数从多到少");
            _cmbOrder.Items.Add("按在文中首次出现的先后");

            _cmbCount = MakeCombo();
            _cmbCount.Items.Add("命中片段数（推荐）");
            _cmbCount.Items.Add("关键词出现次数");
            _cmbCount.Items.Add("命中的不同关键词个数");

            tips.SetToolTip(_cmbCount, "命中片段数：同一类别里被更长关键词包含的短词不重复计数");
            tips.SetToolTip(_cmbOrder, "文件名里多个类别的排列顺序");

            combos.Controls.Add(MakeRightLabel("类别顺序"), 0, 0);
            combos.Controls.Add(_cmbOrder, 1, 0);
            combos.Controls.Add(MakeRightLabel("「N次」"), 2, 0);
            combos.Controls.Add(_cmbCount, 3, 0);

            Label note = new Label();
            note.Dock = DockStyle.Fill;
            note.Text = "提示：同一类别里「红色」被「深红色」包含时只算 1 次";
            note.ForeColor = Color.Gray;
            note.TextAlign = ContentAlignment.MiddleLeft;

            content.Controls.Add(checks, 0, 0);
            content.Controls.Add(combos, 0, 1);
            content.Controls.Add(note, 0, 2);

            FillCard(card, content);
            return card;
        }

        private Label MakeRightLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = new Padding(0, 0, Ui.S(6), 0);
            return l;
        }

        private ComboBox MakeCombo()
        {
            ComboBox c = new ComboBox();
            c.Dock = DockStyle.Fill;
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Margin = new Padding(0, Ui.S(4), Ui.S(8), Ui.S(4));
            return c;
        }

        private CheckBox MakeCheck(string text, bool isChecked)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.Checked = isChecked;
            c.Margin = new Padding(0, Ui.S(3), Ui.S(10), Ui.S(3));
            return c;
        }

        /// <summary>统一的卡片式分组：标题 + 内容区，用表格布局保证不会重叠。</summary>
        private TableLayoutPanel MakeCard(string title)
        {
            TableLayoutPanel card = new TableLayoutPanel();
            card.Dock = DockStyle.Fill;
            card.ColumnCount = 1;
            card.RowCount = 2;
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(30)));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            card.BackColor = Color.White;
            card.Margin = new Padding(0, 0, 0, Ui.S(8));
            card.Padding = new Padding(Ui.S(10), Ui.S(2), Ui.S(10), Ui.S(8));

            Label lb = new Label();
            lb.Text = title;
            lb.Dock = DockStyle.Fill;
            lb.ForeColor = Accent;
            lb.Font = Ui.Font(9.5F, FontStyle.Bold);
            lb.TextAlign = ContentAlignment.MiddleLeft;

            Panel placeholder = new Panel();
            placeholder.Dock = DockStyle.Fill;
            placeholder.BackColor = Color.White;

            card.Controls.Add(lb, 0, 0);
            card.Controls.Add(placeholder, 0, 1);
            card.Tag = placeholder;
            return card;
        }

        /// <summary>把内容放进卡片内容区。</summary>
        private void FillCard(TableLayoutPanel card, Control content)
        {
            Panel placeholder = card.Tag as Panel;
            content.Dock = DockStyle.Fill;
            if (placeholder == null) card.Controls.Add(content, 0, 1);
            else placeholder.Controls.Add(content);
        }

        private Button MakeSmallButton(string text, EventHandler onClick)
        {
            return MakeSmallButton(text, onClick, 0);
        }

        private Button MakeSmallButton(string text, EventHandler onClick, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.Height = Ui.S(28);
            if (width > 0)
            {
                b.Width = Ui.S(width);
                b.Margin = new Padding(0, 0, Ui.S(6), 0);
            }
            else
            {
                b.AutoSize = true;
                b.Margin = new Padding(0, 0, Ui.S(6), 0);
            }
            b.FlatStyle = FlatStyle.System;
            b.Click += onClick;
            return b;
        }

        // ==================== 右侧 ====================

        private Control BuildRightPanel()
        {
            TableLayoutPanel right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.ColumnCount = 1;
            right.RowCount = 3;
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(136)));  // 进度
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));          // 明细
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(216)));  // 日志
            right.Margin = new Padding(0);

            right.Controls.Add(BuildProgressCard(), 0, 0);
            right.Controls.Add(BuildDetailCard(), 0, 1);
            right.Controls.Add(BuildLogCard(), 0, 2);
            return right;
        }

        private Control BuildProgressCard()
        {
            TableLayoutPanel card = MakeCard("处理进度");
            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.ColumnCount = 1;
            content.RowCount = 4;
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(24)));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(22)));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(22)));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            TableLayoutPanel firstLine = new TableLayoutPanel();
            firstLine.Dock = DockStyle.Fill;
            firstLine.ColumnCount = 2;
            firstLine.RowCount = 1;
            firstLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            firstLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.SF(200)));

            _lblCurrent = new Label();
            _lblCurrent.Dock = DockStyle.Fill;
            _lblCurrent.Text = "尚未开始";
            _lblCurrent.AutoEllipsis = true;
            _lblCurrent.TextAlign = ContentAlignment.MiddleLeft;

            _lblElapsed = new Label();
            _lblElapsed.Dock = DockStyle.Fill;
            _lblElapsed.TextAlign = ContentAlignment.MiddleRight;
            _lblElapsed.ForeColor = Color.Gray;

            firstLine.Controls.Add(_lblCurrent, 0, 0);
            firstLine.Controls.Add(_lblElapsed, 1, 0);

            _progressBar = new ProgressBarEx();
            _progressBar.Dock = DockStyle.Fill;
            _progressBar.Margin = new Padding(0, Ui.S(2), 0, Ui.S(2));

            _lblCounters = new Label();
            _lblCounters.Dock = DockStyle.Fill;
            _lblCounters.Text = "共 0 个文件";
            _lblCounters.ForeColor = Color.FromArgb(60, 60, 60);
            _lblCounters.TextAlign = ContentAlignment.MiddleLeft;
            _lblCounters.AutoEllipsis = true;

            _lblStats = new Label();
            _lblStats.Dock = DockStyle.Fill;
            _lblStats.Text = "等待开始";
            _lblStats.ForeColor = Color.FromArgb(60, 60, 60);
            _lblStats.TextAlign = ContentAlignment.MiddleLeft;
            _lblStats.AutoEllipsis = true;

            content.Controls.Add(firstLine, 0, 0);
            content.Controls.Add(_progressBar, 0, 1);
            content.Controls.Add(_lblCounters, 0, 2);
            content.Controls.Add(_lblStats, 0, 3);

            FillCard(card, content);
            return card;
        }

        private Control BuildDetailCard()
        {
            TableLayoutPanel card = MakeCard("文件明细（双击一行打开生成的文件，右键可打开原文件 / 文件夹 / 复制信息）");

            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.AutoGenerateColumns = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.ReadOnly = true;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.BackgroundColor = Color.White;
            _grid.BorderStyle = BorderStyle.FixedSingle;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.RowTemplate.Height = Ui.S(24);
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.ColumnHeadersHeight = Ui.S(28);
            _grid.Columns.Add(MakeColumn("SourceName", "原文件", 24));
            _grid.Columns.Add(MakeColumn("HitText", "命中类别（次数）", 16));
            _grid.Columns.Add(MakeColumn("OutputName", "生成的新文件名", 30));
            _grid.Columns.Add(MakeColumn("StatusText", "结果", 8));
            _grid.Columns.Add(MakeColumn("Message", "说明", 22));
            _grid.DataSource = _rows;
            _grid.CellDoubleClick += delegate (object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0) return;
                FileResult r = _rows[e.RowIndex];
                if (File.Exists(r.OutputPath)) OpenPath(r.OutputPath);
            };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("打开原文件", null, delegate { OpenSelected(true); });
            menu.Items.Add("打开生成的文件", null, delegate { OpenSelected(false); });
            menu.Items.Add("打开所在文件夹", null, delegate { OpenSelectedFolder(); });
            menu.Items.Add("复制本行信息", null, delegate { CopySelectedRow(); });
            _grid.ContextMenuStrip = menu;

            FillCard(card, _grid);
            return card;
        }

        private Control BuildLogCard()
        {
            TableLayoutPanel card = MakeCard("运行日志");
            card.Margin = new Padding(0);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.ColumnCount = 2;
            content.RowCount = 1;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.SF(100)));

            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Both;
            _logBox.WordWrap = false;
            _logBox.BackColor = Color.FromArgb(32, 32, 34);
            _logBox.ForeColor = Color.FromArgb(214, 214, 214);
            _logBox.Font = Ui.MonoFont(9F);
            _logBox.Dock = DockStyle.Fill;
            _logBox.Margin = new Padding(0, 0, Ui.S(8), 0);

            FlowLayoutPanel logButtons = new FlowLayoutPanel();
            logButtons.Dock = DockStyle.Fill;
            logButtons.FlowDirection = FlowDirection.TopDown;
            logButtons.WrapContents = false;
            logButtons.Margin = new Padding(0);
            logButtons.Controls.Add(MakeSmallButton("复制日志", delegate { CopyLog(); }, 84));
            logButtons.Controls.Add(MakeSmallButton("清空日志", delegate { _logBox.Clear(); _logBuffer.Length = 0; }, 84));

            content.Controls.Add(_logBox, 0, 0);
            content.Controls.Add(logButtons, 1, 0);

            FillCard(card, content);
            return card;
        }

        private DataGridViewTextBoxColumn MakeColumn(string prop, string header, int fillWeight)
        {
            DataGridViewTextBoxColumn col = new DataGridViewTextBoxColumn();
            col.DataPropertyName = prop;
            col.HeaderText = header;
            col.FillWeight = fillWeight;
            col.SortMode = DataGridViewColumnSortMode.NotSortable;
            return col;
        }

        // ==================== 底栏 ====================

        private Control BuildFooter()
        {
            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 1;
            footer.RowCount = 2;
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(34)));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            footer.BackColor = Color.FromArgb(250, 251, 253);
            footer.Padding = new Padding(Ui.S(12), Ui.S(4), Ui.S(12), Ui.S(4));
            footer.Margin = new Padding(0);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0);
            buttons.Margin = new Padding(0);

            _startButton = new Button();
            _startButton.Text = "开始处理";
            _startButton.Width = Ui.S(118);
            _startButton.Height = Ui.S(30);
            _startButton.Font = Ui.Font(9.5F, FontStyle.Bold);
            _startButton.BackColor = Accent;
            _startButton.ForeColor = Color.White;
            _startButton.FlatStyle = FlatStyle.Flat;
            _startButton.FlatAppearance.BorderColor = Color.FromArgb(31, 66, 120);
            _startButton.Margin = new Padding(0, 0, Ui.S(8), 0);
            _startButton.Click += delegate { StartRun(false); };

            _previewButton = new Button();
            _previewButton.Text = "预览（不写文件）";
            _previewButton.Width = Ui.S(136);
            _previewButton.Height = Ui.S(30);
            _previewButton.FlatStyle = FlatStyle.System;
            _previewButton.Margin = new Padding(0, 0, Ui.S(8), 0);
            _previewButton.Click += delegate { StartRun(true); };

            _pauseButton = new Button();
            _pauseButton.Text = "暂停";
            _pauseButton.Width = Ui.S(78);
            _pauseButton.Height = Ui.S(30);
            _pauseButton.Enabled = false;
            _pauseButton.FlatStyle = FlatStyle.System;
            _pauseButton.Margin = new Padding(0, 0, Ui.S(8), 0);
            _pauseButton.Click += delegate { TogglePause(); };

            Button openOut = MakeSmallButton("打开输出文件夹", delegate { OpenOutputFolder(); }, 128);
            openOut.Height = Ui.S(30);
            Button openCfg = MakeSmallButton("打开配置目录", delegate { OpenPath(Settings.ConfigPath); }, 116);
            openCfg.Height = Ui.S(30);

            buttons.Controls.Add(_startButton);
            buttons.Controls.Add(_previewButton);
            buttons.Controls.Add(_pauseButton);
            buttons.Controls.Add(openOut);
            buttons.Controls.Add(openCfg);

            _statusLabel = new Label();
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.ForeColor = Color.Gray;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.Text = "配置保存在程序目录的 config.json";

            footer.Controls.Add(buttons, 0, 0);
            footer.Controls.Add(_statusLabel, 0, 1);
            return footer;
        }

        private void BuildTimer()
        {
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 250;
            _timer.Tick += delegate { UpdateProgressUi(); };
        }

        // ==================== 设置读写 ====================

        private void LoadSettingsToUi()
        {
            _sourceBox.Text = _settings.SourceDir;
            _outputBox.Text = _settings.OutputDir;
            _chkRecurse.Checked = _settings.Recurse;
            _chkTxt.Checked = _settings.IncludeTxt;
            _chkDocx.Checked = _settings.IncludeDocx;
            _chkInsertMarker.Checked = _settings.InsertMarkers;
            _chkFirstOnly.Checked = _settings.MarkFirstOccurrenceOnly;
            _chkSkipNoMatch.Checked = _settings.SkipNoMatch;
            _chkIgnoreCase.Checked = _settings.IgnoreCase;
            _cmbOrder.SelectedIndex = _settings.NameOrder < 0 || _settings.NameOrder > 2 ? 0 : _settings.NameOrder;
            _cmbCount.SelectedIndex = _settings.CountModeIndex < 0 || _settings.CountModeIndex > 2 ? 0 : _settings.CountModeIndex;
            UpdateCategorySummary();
        }

        private void SaveSettings(bool showTip)
        {
            _settings.SourceDir = _sourceBox.Text.Trim();
            _settings.OutputDir = _outputBox.Text.Trim();
            _settings.Recurse = _chkRecurse.Checked;
            _settings.IncludeTxt = _chkTxt.Checked;
            _settings.IncludeDocx = _chkDocx.Checked;
            _settings.InsertMarkers = _chkInsertMarker.Checked;
            _settings.MarkFirstOccurrenceOnly = _chkFirstOnly.Checked;
            _settings.SkipNoMatch = _chkSkipNoMatch.Checked;
            _settings.IgnoreCase = _chkIgnoreCase.Checked;
            _settings.NameOrder = _cmbOrder.SelectedIndex;
            _settings.CountModeIndex = _cmbCount.SelectedIndex;
            _settings.Save();
            if (showTip) Log("配置已保存：" + Settings.ConfigPath);
        }

        private void UpdateCategorySummary()
        {
            int categories = _settings.Categories.Count;
            int keywords = 0;
            int withoutMarker = 0;
            int withoutKeyword = 0;
            foreach (Category c in _settings.Categories)
            {
                keywords += c.Keywords.Count;
                if (string.IsNullOrEmpty(c.Marker)) withoutMarker++;
                if (c.Keywords.Count == 0) withoutKeyword++;
            }

            List<string> warnings = new List<string>();
            if (categories == 0) warnings.Add("还没有任何类别，请点「＋ 新建类别」");
            else if (withoutKeyword > 0) warnings.Add(withoutKeyword + " 个类别还没有关键词");
            if (categories > 0 && withoutMarker > 0) warnings.Add(withoutMarker + " 个类别没设标记");

            _statusLabel.Text = warnings.Count == 0
                ? "共 " + categories + " 个类别 / " + keywords + " 个关键词　·　配置保存在 config.json"
                : "提示：" + string.Join("；", warnings.ToArray());
            _statusLabel.ForeColor = warnings.Count == 0 ? Color.Gray : Color.FromArgb(176, 96, 0);
        }

        // ==================== 运行 ====================

        private void BrowseFolder(TextBox target, bool isSource)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = isSource ? "选择要处理的文档所在文件夹" : "选择处理后文件的存放文件夹";
                dlg.ShowNewFolderButton = true;

                string current = target.Text.Trim();
                if (current.Length > 0 && Directory.Exists(current)) dlg.SelectedPath = current;
                else if (_sourceBox.Text.Trim().Length > 0 && Directory.Exists(_sourceBox.Text.Trim()))
                    dlg.SelectedPath = _sourceBox.Text.Trim();

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                target.Text = dlg.SelectedPath;
                if (isSource && _outputBox.Text.Trim().Length == 0)
                    _outputBox.Text = Path.Combine(dlg.SelectedPath, "分类输出");
                SaveSettings(false);
            }
        }

        private bool ValidateBeforeRun(bool dryRun, out string sourceDir, out string outputDir)
        {
            sourceDir = _sourceBox.Text.Trim();
            outputDir = _outputBox.Text.Trim();

            if (sourceDir.Length == 0 || !Directory.Exists(sourceDir))
            {
                MessageBox.Show("请先点「浏览文件夹…」选择一个存在的源文件夹。", "还差一步",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!_chkTxt.Checked && !_chkDocx.Checked)
            {
                MessageBox.Show("至少要勾选一种文件类型（.txt 或 .docx）。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (_settings.Categories.Count == 0)
            {
                MessageBox.Show("请先在左边点「＋ 新建类别」建立至少一个检索类别。", "还差一步",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            int usable = 0;
            foreach (Category c in _settings.Categories) if (c.Keywords.Count > 0) usable++;
            if (usable == 0)
            {
                MessageBox.Show("所有类别都还没有关键词。请在类别下添加关键词后再开始。", "还差一步",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!dryRun && outputDir.Length == 0)
            {
                outputDir = sourceDir;
                _outputBox.Text = sourceDir;
            }
            if (!dryRun && string.Equals(Path.GetFullPath(outputDir).TrimEnd('\\'),
                                         Path.GetFullPath(sourceDir).TrimEnd('\\'),
                                         StringComparison.OrdinalIgnoreCase))
            {
                Log("注意：输出文件夹和源文件夹相同，新文件会和原文件放在一起（原文件不会被动）。");
            }
            return true;
        }

        private void StartRun(bool dryRun)
        {
            if (_running) return;
            SaveSettings(false);

            string sourceDir, outputDir;
            if (!ValidateBeforeRun(dryRun, out sourceDir, out outputDir)) return;

            List<string> notes = new List<string>();
            foreach (Category c in _settings.Categories)
            {
                if (c.Keywords.Count == 0) notes.Add("类别「" + c.Name + "」没有关键词，将被跳过");
                else if (string.IsNullOrEmpty(c.Marker) && _chkInsertMarker.Checked)
                    notes.Add("类别「" + c.Name + "」没设标记，只计数不插入标记");
            }

            _rows.Clear();
            _collected.Clear();
            _logBuffer.Length = 0;
            _logBox.Clear();
            _previewMode = dryRun;
            Log("===== " + (dryRun ? "预览（不写任何文件）" : "开始处理") + " =====");
            Log("源文件夹：" + sourceDir);
            if (!dryRun) Log("输出文件夹：" + outputDir);
            Log("类别：" + DescribeCategories());
            foreach (string n in notes) Log("提示：" + n);

            Settings snapshot = _settings.Clone();
            _processor = new Processor(snapshot.Categories, snapshot);

            _running = true;
            _paused = false;
            _startButton.Enabled = false;
            _previewButton.Enabled = false;
            _pauseButton.Enabled = true;
            _pauseButton.Text = "暂停";
            _statusLabel.Text = dryRun ? "预览中…" : "处理中…";
            _timer.Start();

            bool insertMarker = _chkInsertMarker.Checked;
            RunOptions options = snapshot.ToRunOptions(dryRun);
            Processor processor = _processor;

            _worker = new Thread(delegate ()
            {
                try
                {
                    RunSummary summary = processor.RunBatch(sourceDir, outputDir, options, null,
                        delegate (FileResult r) { CollectResult(r, insertMarker); });

                    BeginInvoke((MethodInvoker)delegate
                    {
                        FinishRun(summary, dryRun, sourceDir, outputDir, true);
                    });
                }
                catch (Exception ex)
                {
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            Log("错误：" + ex.Message);
                            FinishRun(null, dryRun, sourceDir, outputDir, false);
                        });
                    }
                    catch (Exception) { }
                }
            });
            _worker.IsBackground = true;
            _worker.Name = "WordClassifyWorker";
            _worker.Start();
        }

        private string DescribeCategories()
        {
            List<string> parts = new List<string>();
            foreach (Category c in _settings.Categories)
            {
                string marker = string.IsNullOrEmpty(c.Marker) ? "无标记" : "标记" + c.Marker;
                parts.Add(c.Name + "(" + c.Keywords.Count + "词/" + marker + ")");
            }
            return string.Join("，", parts.ToArray());
        }

        private void CollectResult(FileResult r, bool insertMarker)
        {
            lock (_collected)
            {
                if (!_previewMode && r.Status == FileStatus.Success && !insertMarker)
                    r.Message = "已生成（按要求不插标记）";
                _collected.Add(r);
            }
        }

        private void UpdateProgressUi()
        {
            Processor p = _processor;
            if (p != null)
            {
                ProcessProgress live = p.LiveProgress;
                if (live.Total > 0)
                {
                    _progressBar.Percent = (int)(live.Scanned * 100L / live.Total);
                    _lblCurrent.Text = live.Scanned >= live.Total
                        ? "正在收尾…"
                        : "正在处理：第 " + (live.Scanned + 1) + " / " + live.Total + " 个　" + live.CurrentFile;
                }
                else
                {
                    _lblCurrent.Text = "正在扫描文件夹…";
                }
                _lblCounters.Text = "共 " + live.Total + " 个待处理文件　·　已处理 " + live.Scanned
                    + "　·　成功 " + live.Succeeded + "　·　跳过 " + live.Skipped + "　·　失败 " + live.Failed
                    + "　·　已读 " + FormatNumber(live.CharCount) + " 字";
                _lblElapsed.Text = "已用时间 " + (DateTime.Now - live.StartedAt).ToString(@"hh\:mm\:ss");
            }

            List<FileResult> pending = null;
            lock (_collected)
            {
                if (_collected.Count > 0)
                {
                    pending = new List<FileResult>(_collected);
                    _collected.Clear();
                }
            }
            if (pending != null)
            {
                foreach (FileResult r in pending)
                {
                    _rows.Add(r);
                    LogFileResult(r);
                }
                if (_grid.Rows.Count > 0) _grid.FirstDisplayedScrollingRowIndex = _grid.Rows.Count - 1;
            }
        }

        private void FinishRun(RunSummary summary, bool dryRun, string sourceDir, string outputDir, bool ok)
        {
            _running = false;
            _paused = false;
            _timer.Stop();
            _startButton.Enabled = true;
            _previewButton.Enabled = true;
            _pauseButton.Enabled = false;
            _pauseButton.Text = "暂停";
            UpdateProgressUi();

            if (!ok || summary == null)
            {
                _statusLabel.Text = "处理中断了，请看日志。";
                Log("===== 处理中断 =====");
                return;
            }

            _progressBar.Percent = 100;
            _lblCurrent.Text = dryRun ? "预览完成（没有写任何文件）" : "处理完成";
            _lblElapsed.Text = "总耗时 " + FormatDuration(summary.ElapsedMs);

            Log("===== 处理完成 =====");
            Log("文件总数 " + summary.Total + "，成功 " + summary.Succeeded + "，跳过 " + summary.Skipped
                + "，失败 " + summary.Failed + "，总耗时 " + FormatDuration(summary.ElapsedMs));

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _settings.Categories.Count; i++)
            {
                if (summary.CategoryHitCount[i] == 0) continue;
                if (sb.Length > 0) sb.Append("　");
                sb.Append(_settings.Categories[i].Name).Append("：")
                  .Append(summary.CategoryFileCount[i]).Append(" 个文件命中，共 ")
                  .Append(summary.CategoryHitCount[i]).Append(" 次");
            }
            _lblStats.Text = sb.Length == 0 ? "没有任何类别命中。" : sb.ToString();
            if (sb.Length > 0) Log("分类统计：" + sb.ToString());

            if (dryRun)
            {
                _statusLabel.Text = "预览完成，没有写任何文件。确认无误后点「开始处理」。";
            }
            else
            {
                _statusLabel.Text = "处理完成，输出到：" + outputDir;
                if (summary.Succeeded > 0)
                    Log("输出文件夹：" + outputDir + "（可点下面的「打开输出文件夹」查看）");
            }
        }

        private void LogFileResult(FileResult r)
        {
            switch (r.Status)
            {
                case FileStatus.Success:
                    Log("✔ " + r.SourceName + "  →  " + r.OutputName
                        + "    [" + r.HitText + "]"
                        + (r.EncodingName.Length > 0 ? "  " + r.EncodingName : "")
                        + (r.CharCount > 0 ? "  " + FormatNumber(r.CharCount) + "字" : "")
                        + (r.ElapsedMs > 0 ? "  " + r.ElapsedMs + "ms" : ""));
                    break;
                case FileStatus.Skipped:
                    Log("· " + r.SourceName + "  →  跳过（" + r.Message + "）");
                    break;
                default:
                    Log("✖ " + r.SourceName + "  →  失败：" + r.Message);
                    break;
            }
        }

        private void TogglePause()
        {
            Processor p = _processor;
            if (p == null || !_running) return;
            if (_paused)
            {
                p.Resume();
                _paused = false;
                _pauseButton.Text = "暂停";
                _statusLabel.Text = _previewMode ? "预览中…" : "处理中…";
                Log("已继续。");
            }
            else
            {
                p.Pause();
                _paused = true;
                _pauseButton.Text = "继续";
                _statusLabel.Text = "已暂停";
                Log("已暂停，点「继续」接着处理。");
            }
        }

        // ==================== 其它按钮 ====================

        private void ImportConfig()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "导入配置";
                dlg.Filter = "配置文件 (*.json)|*.json|所有文件 (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Settings loaded = Settings.FromJson(File.ReadAllText(dlg.FileName, Encoding.UTF8));
                    _settings.Categories.Clear();
                    foreach (Category c in loaded.Categories) _settings.Categories.Add(c);
                    _settings.MarkerPrefix = loaded.MarkerPrefix;
                    _settings.MarkerSuffix = loaded.MarkerSuffix;
                    _settings.NameOrder = loaded.NameOrder;
                    _settings.CountModeIndex = loaded.CountModeIndex;
                    _settings.IgnoreCase = loaded.IgnoreCase;
                    _chkIgnoreCase.Checked = loaded.IgnoreCase;
                    _cmbOrder.SelectedIndex = loaded.NameOrder;
                    _cmbCount.SelectedIndex = loaded.CountModeIndex;
                    RebuildEditor();
                    Log("已导入配置：" + dlg.FileName + "（" + _settings.Categories.Count + " 个类别）");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导入失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportConfig()
        {
            SaveSettings(false);
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "导出配置";
                dlg.Filter = "配置文件 (*.json)|*.json";
                dlg.FileName = "关键词类别配置.json";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, _settings.ToJson(), new UTF8Encoding(true));
                    Log("已导出配置：" + dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void RebuildEditor()
        {
            if (_editor == null || _editor.Parent == null) return;
            Control parent = _editor.Parent;
            parent.Controls.Remove(_editor);
            _editor = new CategoryEditor(_settings.Categories);
            _editor.Dock = DockStyle.Fill;
            _editor.CategoriesChanged += delegate { UpdateCategorySummary(); };
            parent.Controls.Add(_editor);
            UpdateCategorySummary();
        }

        private void OpenOutputFolder()
        {
            string dir = _outputBox.Text.Trim();
            if (dir.Length == 0) dir = _sourceBox.Text.Trim();
            if (dir.Length == 0 || !Directory.Exists(dir))
            {
                MessageBox.Show("还没有可打开的输出文件夹。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            OpenPath(dir);
        }

        private FileResult SelectedRow()
        {
            if (_grid.SelectedRows.Count == 0) return null;
            int i = _grid.SelectedRows[0].Index;
            if (i < 0 || i >= _rows.Count) return null;
            return _rows[i];
        }

        private void OpenSelected(bool source)
        {
            FileResult r = SelectedRow();
            if (r == null) return;
            string path = source ? r.SourcePath : r.OutputPath;
            if (path.Length > 0 && File.Exists(path)) OpenPath(path);
            else MessageBox.Show(source ? "原文件不存在。" : "还没有生成文件（可能在预览模式下）。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenSelectedFolder()
        {
            FileResult r = SelectedRow();
            if (r == null) return;
            string path = r.OutputPath.Length > 0 && File.Exists(r.OutputPath) ? r.OutputPath : r.SourcePath;
            if (File.Exists(path)) OpenPath(Path.GetDirectoryName(path));
        }

        private void CopySelectedRow()
        {
            FileResult r = SelectedRow();
            if (r == null) return;
            string text = r.SourceName + "\t" + r.HitText + "\t" + r.OutputName + "\t" + r.StatusText + "\t" + r.Message;
            try { Clipboard.SetText(text); } catch (Exception) { }
        }

        private void CopyLog()
        {
            try { Clipboard.SetText(_logBox.Text); } catch (Exception) { }
        }

        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ==================== 日志与工具 ====================

        private void Log(string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine;
            _logBuffer.Append(line);
            if (_logBuffer.Length > 400000)
            {
                _logBuffer.Remove(0, 200000);
                _logBox.Text = _logBuffer.ToString();
            }
            else
            {
                _logBox.AppendText(line);
            }
        }

        private static string FormatNumber(long n)
        {
            return n.ToString("N0");
        }

        private static string FormatDuration(long ms)
        {
            if (ms < 1000) return ms + " 毫秒";
            TimeSpan t = TimeSpan.FromMilliseconds(ms);
            if (t.TotalMinutes < 1) return t.TotalSeconds.ToString("0.0") + " 秒";
            return t.ToString(@"hh\:mm\:ss");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_running)
            {
                if (MessageBox.Show("正在处理中，确定要退出吗？", "确认退出",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                {
                    e.Cancel = true;
                    return;
                }
                Processor p = _processor;
                if (p != null) p.Cancel();
            }
            SaveSettings(false);
            base.OnFormClosing(e);
        }
    }

    /// <summary>自绘进度条：win10 上 ProgressBar 是绿色主题色，这里用蓝色和整体风格统一。</summary>
    public class ProgressBarEx : Control
    {
        private static readonly Color AccentColor = Color.FromArgb(43, 87, 154);
        private int _percent;

        public ProgressBarEx()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(234, 234, 234);
        }

        public int Percent
        {
            get { return _percent; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v > 100) v = 100;
                if (v == _percent) return;
                _percent = v;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            int w = (int)(Width * (_percent / 100.0));
            if (w > 0)
            {
                Rectangle r = new Rectangle(0, 0, w, Height);
                using (System.Drawing.Drawing2D.LinearGradientBrush b =
                    new System.Drawing.Drawing2D.LinearGradientBrush(r, Color.FromArgb(91, 155, 213), AccentColor, 0f))
                {
                    e.Graphics.FillRectangle(b, r);
                }
            }
            using (Pen p = new Pen(Color.FromArgb(200, 200, 200)))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (_percent > 0 && Width > 44)
            {
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    e.Graphics.DrawString(_percent + "%", Font, Brushes.White,
                        new RectangleF(0, 0, Width, Height), sf);
                }
            }
        }
    }
}
