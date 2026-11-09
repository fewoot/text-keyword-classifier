using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WordClassify
{
    /// <summary>
    /// 类别 / 关键词编辑器。
    ///
    /// 交互（尽量贴近 Windows 习惯）：
    ///   · 左边是类别列表；选中一个类别，右边就能改名字、改插入标记、加/删关键词
    ///   · 类别名和标记是**可直接编辑**的文本框，改完立即生效
    ///   · 关键词支持「回车添加」「Delete 删除」「双击删除」「清空」
    ///   · 上移/下移调整类别顺序，删除类别有二次确认
    ///
    /// 说明：这里只用 ListBox + Button + TextBox 这类"老实"控件，
    /// 不用自绘芯片，布局在缩放和不同 DPI 下更稳（KeywordChips 版本出现过被压扁的问题）。
    /// </summary>
    public class CategoryEditor : UserControl
    {
        private readonly List<Category> _categories;
        private readonly ListBox _list = new ListBox();
        private readonly ListBox _keywords = new ListBox();
        private readonly TextBox _nameBox = new TextBox();
        private readonly TextBox _markerBox = new TextBox();
        private readonly TextBox _keywordInput = new TextBox();
        private readonly Label _summary = new Label();
        private readonly Label _keywordCount = new Label();
        private readonly TableLayoutPanel _detail;
        private readonly ToolTip _tips = new ToolTip();
        private bool _loading;

        /// <summary>类别或关键词发生变化时触发。</summary>
        public event EventHandler CategoriesChanged;

        private static readonly Color Accent = Color.FromArgb(43, 87, 154);
        private static readonly Color Soft = Color.FromArgb(247, 249, 252);

        public CategoryEditor(List<Category> categories)
        {
            _categories = categories;
            // 尺寸缩放统一由 Ui 负责，关掉 WinForms 自动缩放，避免缩放两次
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.White;
            Padding = new Padding(Ui.S(8), Ui.S(4), Ui.S(8), Ui.S(4));

            // ---------- 标题与统计 ----------
            Label title = new Label();
            title.Text = "检索类别与关键词";
            title.Dock = DockStyle.Top;
            title.Height = Ui.S(24);
            title.Font = Ui.Font(Font.Size, FontStyle.Bold);
            title.ForeColor = Accent;
            title.TextAlign = ContentAlignment.MiddleLeft;

            _summary.Dock = DockStyle.Top;
            _summary.Height = Ui.S(20);
            _summary.ForeColor = Color.Gray;
            _summary.TextAlign = ContentAlignment.MiddleLeft;

            // ---------- 左右分栏 ----------
            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 2;
            body.RowCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.SF(236)));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.Padding = new Padding(0, Ui.S(4), 0, 0);

            body.Controls.Add(BuildLeftPane(), 0, 0);

            _detail = BuildDetailPane();
            body.Controls.Add(_detail, 1, 0);

            Controls.Add(body);
            Controls.Add(_summary);
            Controls.Add(title);

            RefreshList(0);
        }

        /// <summary>左边：类别列表 + 四个竖排按钮（竖着放就不会因为宽度不够被截断）。</summary>
        private Control BuildLeftPane()
        {
            TableLayoutPanel left = new TableLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.ColumnCount = 1;
            left.RowCount = 2;
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(96)));
            left.Margin = new Padding(0, 0, Ui.S(8), 0);

            _list.Dock = DockStyle.Fill;
            _list.IntegralHeight = false;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = Ui.S(24);
            _list.MinimumSize = new Size(Ui.S(150), Ui.S(96));   // 至少能看 4 行，不能缩成一条缝
            _list.DrawItem += ListDrawItem;
            _list.SelectedIndexChanged += delegate { LoadCurrentCategory(); };
            _tips.SetToolTip(_list, "选中一个类别，右边即可改名字、改标记、增删关键词");

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.ColumnCount = 2;
            buttons.RowCount = 2;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(30)));
            buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(30)));
            buttons.Margin = new Padding(0, Ui.S(4), 0, 0);

            buttons.Controls.Add(MakeFlatButton("＋ 新建类别", delegate { AddCategory(); }), 0, 0);
            buttons.Controls.Add(MakeFlatButton("删除本类", delegate { DeleteCategory(); }), 1, 0);
            buttons.Controls.Add(MakeFlatButton("↑ 类别上移", delegate { MoveCategory(-1); }), 0, 1);
            buttons.Controls.Add(MakeFlatButton("↓ 类别下移", delegate { MoveCategory(1); }), 1, 1);
            left.Controls.Add(_list, 0, 0);
            left.Controls.Add(buttons, 0, 1);
            return left;
        }

        /// <summary>右边：类别名 / 插入标记 / 关键词列表 / 新增输入框。</summary>
        private TableLayoutPanel BuildDetailPane()
        {
            TableLayoutPanel detail = new TableLayoutPanel();
            detail.Dock = DockStyle.Fill;
            detail.ColumnCount = 1;
            detail.RowCount = 5;
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(30)));   // 类别名
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(30)));   // 插入标记
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(22)));   // 关键词标题 + 计数
            detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));         // 关键词列表
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(30)));   // 输入框
            detail.BackColor = Soft;
            detail.Padding = new Padding(Ui.S(8), Ui.S(4), Ui.S(8), Ui.S(4));

            detail.Controls.Add(MakeFieldRow("类名：", _nameBox, 52), 0, 0);
            detail.Controls.Add(MakeFieldRow("标记：", _markerBox, 52), 0, 1);

            _nameBox.TextChanged += delegate { ApplyName(); };
            _tips.SetToolTip(_nameBox, "类别名字，改完立即生效；文件名里就是「这个名字+N次-」");
            _markerBox.TextChanged += delegate { ApplyMarker(); };
            _tips.SetToolTip(_markerBox, "命中这个类别时插入正文的标记，例如 123 → 正文出现〔123〕关键词");

            TableLayoutPanel kwHeader = new TableLayoutPanel();
            kwHeader.Dock = DockStyle.Fill;
            kwHeader.ColumnCount = 2;
            kwHeader.RowCount = 1;
            kwHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            kwHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.SF(52)));   // 「2 个」只需要这么宽

            Label kwTitle = new Label();
            kwTitle.Text = "关键词（回车添加）";
            kwTitle.Dock = DockStyle.Fill;
            kwTitle.ForeColor = Accent;
            kwTitle.TextAlign = ContentAlignment.MiddleLeft;

            _keywordCount.Dock = DockStyle.Fill;
            _keywordCount.ForeColor = Color.Gray;
            _keywordCount.TextAlign = ContentAlignment.MiddleRight;

            kwHeader.Controls.Add(kwTitle, 0, 0);
            kwHeader.Controls.Add(_keywordCount, 1, 0);
            detail.Controls.Add(kwHeader, 0, 2);

            TableLayoutPanel kwArea = new TableLayoutPanel();
            kwArea.Dock = DockStyle.Fill;
            kwArea.ColumnCount = 2;
            kwArea.RowCount = 1;
            kwArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            kwArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.SF(84)));

            _keywords.Dock = DockStyle.Fill;
            _keywords.IntegralHeight = false;
            _keywords.BorderStyle = BorderStyle.FixedSingle;
            _keywords.MinimumSize = new Size(Ui.S(120), Ui.S(72));   // 至少能看 3 行关键词
            _keywords.Margin = new Padding(0, 0, Ui.S(6), 0);
            _keywords.DoubleClick += delegate { DeleteKeyword(); };
            _keywords.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete) { e.SuppressKeyPress = true; DeleteKeyword(); }
            };
            _tips.SetToolTip(_keywords, "双击一个关键词即可删除；按 Delete 键也可以");

            TableLayoutPanel kwButtons = new TableLayoutPanel();
            kwButtons.Dock = DockStyle.Fill;
            kwButtons.ColumnCount = 1;
            kwButtons.RowCount = 3;
            kwButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(28)));
            kwButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.SF(28)));
            kwButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            kwButtons.Margin = new Padding(0);
            kwButtons.Controls.Add(MakeFlatButton("删除选中", delegate { DeleteKeyword(); }), 0, 0);
            kwButtons.Controls.Add(MakeFlatButton("清空全部", delegate { ClearKeywords(); }), 0, 1);

            kwArea.Controls.Add(_keywords, 0, 0);
            kwArea.Controls.Add(kwButtons, 1, 0);
            detail.Controls.Add(kwArea, 0, 3);

            _keywordInput.Dock = DockStyle.Fill;
            _keywordInput.Margin = new Padding(0, Ui.S(4), 0, 0);
            _keywordInput.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    AddKeyword();
                }
            };
            _tips.SetToolTip(_keywordInput, "输入关键词后按回车添加，例如 我爱你 / 爱你");
            detail.Controls.Add(_keywordInput, 0, 4);

            return detail;
        }

        /// <summary>宽度铺满、高度固定 26 设计像素的按钮（用 Dock.Top 而不是 Fill，避免被纵向拉伸变形）。</summary>
        private Button MakeFlatButton(string text, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.Dock = DockStyle.Top;
            b.Height = Ui.S(26);
            b.Margin = new Padding(0, 0, Ui.S(4), Ui.S(4));
            b.FlatStyle = FlatStyle.System;
            b.Click += onClick;
            return b;
        }

        /// <summary>「标签 + 输入框」一行。</summary>
        private Control MakeFieldRow(string label, Control input, int labelWidth)
        {
            TableLayoutPanel row = new TableLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.ColumnCount = 2;
            row.RowCount = 1;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.SF(labelWidth)));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.Margin = new Padding(0);

            Label lb = new Label();
            lb.Text = label;
            lb.Dock = DockStyle.Fill;
            lb.TextAlign = ContentAlignment.MiddleLeft;

            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(0, Ui.S(4), 0, Ui.S(4));

            row.Controls.Add(lb, 0, 0);
            row.Controls.Add(input, 1, 0);
            return row;
        }

        public Category SelectedCategory
        {
            get
            {
                int i = _list.SelectedIndex;
                if (i < 0 || i >= _categories.Count) return null;
                return _categories[i];
            }
        }

        private void ListDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _categories.Count) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (SolidBrush back = new SolidBrush(selected ? Color.FromArgb(207, 224, 247) : Color.White))
                e.Graphics.FillRectangle(back, e.Bounds);

            Category c = _categories[e.Index];
            Rectangle swatch = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + 7, 10, 10);
            using (SolidBrush sb = new SolidBrush(CategoryColors.Get(e.Index)))
                e.Graphics.FillRectangle(sb, swatch);

            string text = c.Name;
            if (c.Keywords.Count == 0) text += "（还没关键词）";
            else text += "  " + c.Keywords.Count + " 词";
            using (SolidBrush fb = new SolidBrush(selected ? Color.FromArgb(20, 40, 80) : Color.Black))
                e.Graphics.DrawString(text, e.Font, fb, e.Bounds.Left + 20, e.Bounds.Top + 4);
        }

        private void RefreshList(int selectIndex)
        {
            _loading = true;
            _list.Items.Clear();
            foreach (Category c in _categories) _list.Items.Add(c.Name);
            if (_categories.Count > 0)
            {
                if (selectIndex < 0 || selectIndex >= _categories.Count) selectIndex = 0;
                _list.SelectedIndex = selectIndex;
            }
            _loading = false;
            LoadCurrentCategory();
        }

        private void LoadCurrentCategory()
        {
            _loading = true;
            Category c = SelectedCategory;
            bool has = c != null;
            _detail.Enabled = has;
            if (has)
            {
                _nameBox.Text = c.Name;
                _markerBox.Text = c.Marker;
            }
            else
            {
                _nameBox.Text = "";
                _markerBox.Text = "";
            }
            _loading = false;

            _keywords.BeginUpdate();
            _keywords.Items.Clear();
            if (c != null)
                foreach (string k in c.Keywords) _keywords.Items.Add(k);
            _keywords.EndUpdate();

            _keywordCount.Text = has ? c.Keywords.Count + " 个" : "";
            UpdateSummary();
            RaiseChanged();
        }

        private void UpdateSummary()
        {
            int keywords = 0;
            int noKeyword = 0;
            int noMarker = 0;
            foreach (Category c in _categories)
            {
                keywords += c.Keywords.Count;
                if (c.Keywords.Count == 0) noKeyword++;
                if (string.IsNullOrEmpty(c.Marker)) noMarker++;
            }
            string s = _categories.Count + " 个类别 · " + keywords + " 个关键词";
            List<string> warn = new List<string>();
            if (noKeyword > 0) warn.Add(noKeyword + " 个类别还没关键词");
            if (noMarker > 0) warn.Add(noMarker + " 个类别没设标记");
            if (warn.Count > 0) s += "　（" + string.Join("；", warn.ToArray()) + "）";
            _summary.Text = s;
            _summary.ForeColor = warn.Count > 0 ? Color.FromArgb(176, 96, 0) : Color.Gray;
        }

        // ---------- 操作 ----------

        private void AddCategory()
        {
            string name = "类别" + (_categories.Count + 1);
            int n = 1;
            while (FindByName(name) >= 0) { n++; name = "类别" + (_categories.Count + n); }

            Category c = new Category();
            c.Name = name;
            c.Marker = (_categories.Count + 1).ToString();
            _categories.Add(c);
            RefreshList(_categories.Count - 1);
            RaiseChanged();
            _nameBox.Focus();
            _nameBox.SelectAll();
        }

        private void RenameCategoryInline()
        {
            if (SelectedCategory == null)
            {
                MessageBox.Show("请先在左边选中一个类别。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _nameBox.Focus();
            _nameBox.SelectAll();
        }

        private int FindByName(string name)
        {
            for (int i = 0; i < _categories.Count; i++)
                if (string.Equals(_categories[i].Name, name, StringComparison.Ordinal)) return i;
            return -1;
        }

        private void DeleteCategory()
        {
            int idx = _list.SelectedIndex;
            if (idx < 0 || idx >= _categories.Count) return;
            Category c = _categories[idx];
            if (MessageBox.Show("确定删除类别「" + c.Name + "」和它的 " + c.Keywords.Count + " 个关键词吗？",
                "删除类别", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            _categories.RemoveAt(idx);
            RefreshList(idx >= _categories.Count ? _categories.Count - 1 : idx);
            RaiseChanged();
        }

        private void MoveCategory(int delta)
        {
            int idx = _list.SelectedIndex;
            int target = idx + delta;
            if (idx < 0 || target < 0 || target >= _categories.Count) return;
            Category tmp = _categories[idx];
            _categories[idx] = _categories[target];
            _categories[target] = tmp;
            RefreshList(target);
            RaiseChanged();
        }

        private void AddKeyword()
        {
            Category c = SelectedCategory;
            if (c == null)
            {
                MessageBox.Show("请先在左边选中或新建一个类别。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string k = _keywordInput.Text.Trim();
            if (k.Length == 0) return;
            foreach (string existing in c.Keywords)
            {
                if (string.Equals(existing, k, StringComparison.Ordinal))
                {
                    _keywordInput.SelectAll();
                    return;
                }
            }
            c.Keywords.Add(k);
            _keywordInput.Clear();
            LoadCurrentCategory();
            _keywords.TopIndex = _keywords.Items.Count - 1;
            RaiseChanged();
            _keywordInput.Focus();
        }

        private void DeleteKeyword()
        {
            Category c = SelectedCategory;
            if (c == null) return;
            int i = _keywords.SelectedIndex;
            if (i < 0 || i >= c.Keywords.Count) return;
            c.Keywords.RemoveAt(i);
            LoadCurrentCategory();
            if (_keywords.Items.Count > 0)
                _keywords.SelectedIndex = Math.Min(i, _keywords.Items.Count - 1);
            RaiseChanged();
        }

        private void ClearKeywords()
        {
            Category c = SelectedCategory;
            if (c == null || c.Keywords.Count == 0) return;
            if (MessageBox.Show("清空类别「" + c.Name + "」的全部 " + c.Keywords.Count + " 个关键词？", "确认",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            c.Keywords.Clear();
            LoadCurrentCategory();
            RaiseChanged();
        }

        private void ApplyName()
        {
            if (_loading) return;
            Category c = SelectedCategory;
            if (c == null) return;
            string name = _nameBox.Text.Trim();
            c.Name = name.Length == 0 ? "未命名" : name;
            int idx = _list.SelectedIndex;
            _list.Invalidate();
            if (idx >= 0 && idx < _list.Items.Count)
            {
                _loading = true;
                _list.Items[idx] = c.Name;
                _loading = false;
            }
            RaiseChanged();
        }

        private void ApplyMarker()
        {
            if (_loading) return;
            Category c = SelectedCategory;
            if (c == null) return;
            c.Marker = _markerBox.Text.Trim();
            UpdateSummary();
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            EventHandler h = CategoriesChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>类别颜色，仅用于界面区分。</summary>
        public static class CategoryColors
        {
            private static readonly Color[] Palette = new Color[]
            {
                Color.FromArgb(224, 92, 122), Color.FromArgb(58, 167, 109), Color.FromArgb(224, 163, 58),
                Color.FromArgb(90, 140, 220), Color.FromArgb(150, 110, 200), Color.FromArgb(60, 170, 180),
                Color.FromArgb(200, 100, 60), Color.FromArgb(120, 130, 140)
            };

            public static Color Get(int index)
            {
                if (index < 0) index = 0;
                return Palette[index % Palette.Length];
            }
        }
    }
}
