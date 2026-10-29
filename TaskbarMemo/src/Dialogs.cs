using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace TaskbarMemo
{
    internal static class Dlg
    {
        public static Button Btn(string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.UseVisualStyleBackColor = true;
            return b;
        }

        public static Label Lbl(string text, int x, int y, int w, int h)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, h);
            return l;
        }

        public static void InitDialog(Form f, string title, Size client)
        {
            f.Text = title;
            f.Font = UiFont.Get(9f, FontStyle.Regular);
            f.AutoScaleMode = AutoScaleMode.None;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.TopMost = true;
            f.MinimizeBox = false;
            f.MaximizeBox = false;
            f.ShowInTaskbar = false;
            f.ClientSize = client;
            try
            {
                f.Icon = AppIcon.Create();
            }
            catch
            {
            }
        }
    }

    // ---------------------------------------------------------------- 自定义备忘

    /// <summary>一行一条，纯文本编辑。</summary>
    public class CustomDialog : Form
    {
        private readonly TextBox _box;
        private readonly List<string> _lines;

        public List<string> Lines
        {
            get { return _lines; }
        }

        public CustomDialog(List<string> existing)
        {
            _lines = new List<string>();
            Dlg.InitDialog(this, "自定义备忘", new Size(480, 400));
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimumSize = new Size(400, 320);

            Label lbl = Dlg.Lbl("一行一个备忘，空行会被忽略：", 12, 10, 456, 20);
            lbl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            _box = new TextBox();
            _box.Multiline = true;
            _box.AcceptsReturn = true;
            _box.WordWrap = true;
            _box.ScrollBars = ScrollBars.Vertical;
            _box.Font = UiFont.Get(10.5f, FontStyle.Regular);
            _box.SetBounds(12, 34, 456, 314);
            _box.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            if (existing != null && existing.Count > 0)
            {
                _box.Lines = existing.ToArray();
            }

            Button ok = Dlg.Btn("确定", 304, 358, 80, 28);
            ok.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            ok.Click += delegate { AcceptInput(); };

            Button cancel = Dlg.Btn("取消", 392, 358, 80, 28);
            cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            cancel.DialogResult = DialogResult.Cancel;

            Controls.Add(lbl);
            Controls.Add(_box);
            Controls.Add(ok);
            Controls.Add(cancel);
            CancelButton = cancel;
        }

        private void AcceptInput()
        {
            // _lines 是 readonly，构造时已初始化成空列表，这里只能清空后重填。
            _lines.Clear();
            string[] raw = _box.Lines;
            foreach (string r in raw)
            {
                string t = r == null ? "" : r.Trim();
                if (t.Length > 0) _lines.Add(t);
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // ---------------------------------------------------------------- 常规备忘

    /// <summary>
    /// 周一到周日分 7 个页签。每天一个大文本框，**一行一条**，
    /// 所以同一天可以写多条备忘，各自独立完成。
    /// </summary>
    public class RoutineDialog : Form
    {
        private readonly TabControl _tabs;
        private readonly TextBox[] _boxes = new TextBox[7];
        private readonly CheckBox[] _checks = new CheckBox[7];
        private readonly List<RoutineItem> _result;
        private readonly List<RoutineItem> _source;

        public List<RoutineItem> Result
        {
            get { return _result; }
        }

        public RoutineDialog(List<RoutineItem> source)
        {
            _result = new List<RoutineItem>();
            _source = new List<RoutineItem>();
            if (source != null)
            {
                foreach (RoutineItem r in source) _source.Add(r);
            }

            Dlg.InitDialog(this, "常规备忘", new Size(520, 398));
            FormBorderStyle = FormBorderStyle.FixedDialog;

            _tabs = new TabControl();
            _tabs.SetBounds(12, 12, 496, 320);

            for (int i = 0; i < 7; i++)
            {
                TabPage page = new TabPage();
                page.Text = DayUtil.Names[i];

                CheckBox cb = new CheckBox();
                cb.Text = "这天启用";
                cb.SetBounds(8, 8, 120, 22);
                cb.Checked = DefaultEnabled(i);
                _checks[i] = cb;

                Label hint = Dlg.Lbl("一行一条，可以写多条", 140, 10, 300, 20);

                TextBox tb = new TextBox();
                tb.Multiline = true;
                tb.AcceptsReturn = true;
                tb.WordWrap = true;
                tb.ScrollBars = ScrollBars.Vertical;
                tb.Font = UiFont.Get(10f, FontStyle.Regular);
                tb.SetBounds(8, 36, 470, 244);
                tb.Text = ExistingText(i);
                _boxes[i] = tb;

                page.Controls.Add(cb);
                page.Controls.Add(hint);
                page.Controls.Add(tb);
                _tabs.TabPages.Add(page);
            }

            Button ok = Dlg.Btn("确定", 336, 348, 80, 28);
            ok.Click += delegate { AcceptInput(); };
            Button cancel = Dlg.Btn("取消", 424, 348, 84, 28);
            cancel.DialogResult = DialogResult.Cancel;

            Controls.Add(_tabs);
            Controls.Add(ok);
            Controls.Add(cancel);
            CancelButton = cancel;
        }

        private bool DefaultEnabled(int day)
        {
            bool any = false;
            bool enabled = false;
            foreach (RoutineItem r in _source)
            {
                if (r.Day != day) continue;
                any = true;
                if (r.Enabled) enabled = true;
            }
            return any ? enabled : true;
        }

        private string ExistingText(int day)
        {
            StringBuilder sb = new StringBuilder();
            foreach (RoutineItem r in _source)
            {
                if (r.Day != day) continue;
                string t = r.Text == null ? "" : r.Text.Trim();
                if (t.Length == 0) continue;
                if (sb.Length > 0) sb.Append("\r\n");
                sb.Append(t);
            }
            return sb.ToString();
        }

        private void AcceptInput()
        {
            _result.Clear();

            for (int i = 0; i < 7; i++)
            {
                List<RoutineItem> olds = new List<RoutineItem>();
                foreach (RoutineItem r in _source)
                {
                    if (r.Day == i) olds.Add(r);
                }
                bool[] used = new bool[olds.Count];

                string[] raw = _boxes[i].Lines;
                foreach (string line in raw)
                {
                    string t = line == null ? "" : line.Trim();
                    if (t.Length == 0) continue;

                    RoutineItem item = new RoutineItem();
                    item.Day = i;
                    item.Text = t;
                    item.Enabled = _checks[i].Checked;
                    // 按文本匹配旧条目，把"今天已完成"的状态沿用下来，
                    // 否则每次打开对话框都会把今天已经做完的条目复活。
                    item.LastDone = FindLastDone(olds, used, t);
                    _result.Add(item);
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private static string FindLastDone(List<RoutineItem> olds, bool[] used, string text)
        {
            for (int i = 0; i < olds.Count; i++)
            {
                if (used[i]) continue;
                if (olds[i].Text != text) continue;
                used[i] = true;
                return olds[i].LastDone == null ? "" : olds[i].LastDone;
            }
            return "";
        }
    }

    // ---------------------------------------------------------------- 远期备忘

    /// <summary>只在指定日期当天显示的任务管理。</summary>
    public class FutureDialog : Form
    {
        private readonly List<FutureItem> _items;
        private readonly ListBox _list;
        private readonly TextBox _text;
        private readonly DateTimePicker _date;
        private bool _loading;
        private int _sel;

        public List<FutureItem> Result
        {
            get { return _items; }
        }

        public FutureDialog(List<FutureItem> source)
        {
            _items = new List<FutureItem>();
            if (source != null)
            {
                foreach (FutureItem f in source) _items.Add(CloneFuture(f));
            }
            _sel = -1;

            Dlg.InitDialog(this, "远期备忘", new Size(520, 400));
            FormBorderStyle = FormBorderStyle.FixedDialog;

            _list = new ListBox();
            _list.SetBounds(12, 12, 236, 336);
            _list.IntegralHeight = false;
            _list.SelectedIndexChanged += delegate
            {
                if (_loading) return;
                // 关键：用户点列表时要把游标同步过来。否则 _sel 一直是旧值
                // （构造后是 -1），删除/更新都会被判成"未选中"而失效。
                _sel = _list.SelectedIndex;
                LoadSelection();
            };

            _text = new TextBox();
            _text.Multiline = true;
            _text.AcceptsReturn = true;
            _text.WordWrap = true;
            _text.ScrollBars = ScrollBars.Vertical;
            _text.SetBounds(260, 36, 248, 88);

            _date = new DateTimePicker();
            _date.Format = DateTimePickerFormat.Short;
            _date.SetBounds(260, 152, 160, 26);
            _date.Value = DateTime.Today;

            Controls.Add(_list);
            Controls.Add(Dlg.Lbl("任务内容（一行一条，可写多条）", 260, 14, 250, 20));
            Controls.Add(_text);
            Controls.Add(Dlg.Lbl("显示日期（仅在当天出现）", 260, 130, 250, 20));
            Controls.Add(_date);

            Button add = Dlg.Btn("添加", 260, 190, 58, 28);
            add.Click += delegate { AddItem(); };
            Button upd = Dlg.Btn("更新", 323, 190, 58, 28);
            upd.Click += delegate { UpdateItem(); };
            Button del = Dlg.Btn("删除", 386, 190, 58, 28);
            del.Click += delegate { DeleteItem(); };
            Button clr = Dlg.Btn("清空输入", 449, 190, 59, 28);
            clr.Click += delegate { ClearFields(); };
            Controls.Add(add);
            Controls.Add(upd);
            Controls.Add(del);
            Controls.Add(clr);

            Button delAll = Dlg.Btn("全部删除", 260, 224, 120, 28);
            delAll.Click += delegate { DeleteAll(); };
            Controls.Add(delAll);

            Controls.Add(Dlg.Lbl("“清空输入”只清空上面的输入框。\r\n“更新”把选中这条换成输入框里的每一行。\r\n“全部删除”删掉左侧列出的全部远期任务。",
                260, 258, 248, 52));

            Button ok = Dlg.Btn("确定", 336, 356, 80, 28);
            ok.Click += delegate
            {
                DialogResult = DialogResult.OK;
                Close();
            };
            Button cancel = Dlg.Btn("取消", 424, 356, 84, 28);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(ok);
            Controls.Add(cancel);
            CancelButton = cancel;

            RebuildList();
            ClearFields();
        }

        private static FutureItem CloneFuture(FutureItem src)
        {
            FutureItem f = new FutureItem();
            if (src != null)
            {
                f.Text = src.Text;
                f.Date = src.Date;
                f.Done = src.Done;
            }
            return f;
        }

        private static string Describe(FutureItem f)
        {
            return f.Text + "   [" + f.Date + "]";
        }

        private void RebuildList()
        {
            _loading = true;
            int keep = _sel;

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (FutureItem f in _items)
            {
                _list.Items.Add(Describe(f));
            }
            _list.EndUpdate();

            if (_items.Count == 0)
            {
                _sel = -1;
                _list.SelectedIndex = -1;
                _loading = false;
                ClearFields();
                return;
            }

            if (keep < 0) keep = 0;
            if (keep > _items.Count - 1) keep = _items.Count - 1;
            _sel = keep;
            _list.SelectedIndex = keep;
            _loading = false;
            LoadSelection();
        }

        private void LoadSelection()
        {
            if (_sel < 0 || _sel >= _items.Count) return;
            FutureItem f = _items[_sel];
            _text.Text = f.Text;

            DateTime d;
            if (DayUtil.TryParseDate(f.Date, out d))
            {
                // data.xml 被手工改过的话日期可能越界，DateTimePicker 会抛异常。
                if (d < _date.MinDate) d = _date.MinDate;
                if (d > _date.MaxDate) d = _date.MaxDate;
                _date.Value = d;
            }
        }

        private void ClearFields()
        {
            _loading = true;
            _text.Text = "";
            _date.Value = DateTime.Today;
            _sel = -1;
            _list.SelectedIndex = -1;
            _loading = false;
            _text.Focus();
        }

        /// <summary>把输入框按行拆成多条内容，空行忽略。</summary>
        private List<string> ReadLines()
        {
            List<string> lines = new List<string>();

            string[] raw = _text.Lines;
            foreach (string r in raw)
            {
                string t = r == null ? "" : r.Trim();
                if (t.Length > 0) lines.Add(t);
            }

            if (lines.Count == 0)
            {
                MessageBox.Show(this, "请先输入任务内容（一行一条）。", "远期备忘",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _text.Focus();
                return null;
            }

            return lines;
        }

        private void AddItem()
        {
            List<string> lines = ReadLines();
            if (lines == null) return;

            string dateKey = DayUtil.DateKey(_date.Value.Date);
            foreach (string t in lines)
            {
                FutureItem f = new FutureItem();
                f.Text = t;
                f.Date = dateKey;
                _items.Add(f);
            }

            _sel = _items.Count - 1;
            RebuildList();
        }

        private void UpdateItem()
        {
            if (_sel < 0 || _sel >= _items.Count)
            {
                MessageBox.Show(this, "请先在左侧列表中选择一条任务。", "远期备忘",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<string> lines = ReadLines();
            if (lines == null) return;

            string dateKey = DayUtil.DateKey(_date.Value.Date);
            FutureItem old = _items[_sel];
            int at = _sel;

            _items.RemoveAt(_sel);

            for (int i = 0; i < lines.Count; i++)
            {
                FutureItem f = new FutureItem();
                f.Text = lines[i];
                f.Date = dateKey;

                // 第一行沿用原来这条的完成状态，其余算新条目
                if (i == 0)
                {
                    f.Done = old.Done;
                    f.DoneDate = old.DoneDate;
                }

                _items.Insert(at + i, f);
            }

            _sel = at + lines.Count - 1;
            RebuildList();
        }

        private void DeleteItem()
        {
            if (_sel < 0 || _sel >= _items.Count)
            {
                MessageBox.Show(this, "请先在左侧列表中选择一条任务。", "远期备忘",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "确定删除选中的任务吗？", "远期备忘",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }

            _items.RemoveAt(_sel);
            if (_sel >= _items.Count) _sel = _items.Count - 1;
            RebuildList();
        }

        /// <summary>批量清空：删除列表里全部远期任务。</summary>
        private void DeleteAll()
        {
            if (_items.Count == 0)
            {
                MessageBox.Show(this, "左侧列表里没有任务。", "远期备忘",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string ask = "确定删除全部 " + _items.Count + " 条远期任务吗？\r\n此操作不可撤销。";
            if (MessageBox.Show(this, ask, "远期备忘",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            {
                return;
            }

            _items.Clear();
            _sel = -1;
            _list.SelectedIndex = -1;
            RebuildList();   // 空列表分支会顺带清空输入框
        }
    }

    // ---------------------------------------------------------------- 设置

    public class SettingsDialog : Form
    {
        private readonly CheckBox _scroll;
        private readonly NumericUpDown _sec;
        private readonly CheckBox _custom;
        private readonly CheckBox _routine;
        private readonly CheckBox _future;
        private readonly RadioButton _rbTaskbar;
        private readonly RadioButton _rbFloat;
        private readonly CheckBox _auto;

        public Settings Result { get; private set; }

        public SettingsDialog(Settings current)
        {
            if (current == null) current = new Settings();

            Dlg.InitDialog(this, "设置", new Size(420, 428));
            FormBorderStyle = FormBorderStyle.FixedDialog;

            GroupBox g1 = new GroupBox();
            g1.Text = "滚动";
            g1.SetBounds(12, 12, 396, 92);

            _scroll = new CheckBox();
            _scroll.Text = "自动滚动（每 N 秒切换到下一条备忘）";
            _scroll.SetBounds(14, 24, 340, 22);
            _scroll.Checked = current.ScrollEnabled;

            _sec = new NumericUpDown();
            _sec.SetBounds(34, 52, 70, 26);
            _sec.Minimum = 1;
            _sec.Maximum = 3600;
            decimal v = current.ScrollSeconds;
            if (v < 1m) v = 1m;
            if (v > 3600m) v = 3600m;
            _sec.Value = v;

            Label l1 = Dlg.Lbl("每", 14, 56, 20, 22);
            Label l2 = Dlg.Lbl("秒切换一条", 112, 56, 140, 22);

            _scroll.CheckedChanged += delegate { _sec.Enabled = _scroll.Checked; };
            _sec.Enabled = _scroll.Checked;

            g1.Controls.Add(_scroll);
            g1.Controls.Add(l1);
            g1.Controls.Add(_sec);
            g1.Controls.Add(l2);

            GroupBox g2 = new GroupBox();
            g2.Text = "显示内容（可多选）";
            g2.SetBounds(12, 112, 396, 110);

            _custom = new CheckBox();
            _custom.Text = "自定义备忘";
            _custom.SetBounds(14, 22, 200, 22);
            _custom.Checked = current.ShowCustom;

            _routine = new CheckBox();
            _routine.Text = "常规备忘";
            _routine.SetBounds(14, 46, 200, 22);
            _routine.Checked = current.ShowRoutine;

            _future = new CheckBox();
            _future.Text = "远期备忘";
            _future.SetBounds(14, 70, 200, 22);
            _future.Checked = current.ShowFuture;

            g2.Controls.Add(_custom);
            g2.Controls.Add(_routine);
            g2.Controls.Add(_future);

            GroupBox g3 = new GroupBox();
            g3.Text = "显示位置";
            g3.SetBounds(12, 230, 396, 76);

            _rbTaskbar = new RadioButton();
            _rbTaskbar.Text = "贴着任务栏（占任务栏空白处，不遮挡内容）";
            _rbTaskbar.SetBounds(14, 24, 360, 22);

            _rbFloat = new RadioButton();
            _rbFloat.Text = "浮在托盘上方（窗口形式，会盖住桌面一角）";
            _rbFloat.SetBounds(14, 48, 360, 22);

            if (current.Placement == 1) _rbFloat.Checked = true;
            else _rbTaskbar.Checked = true;

            g3.Controls.Add(_rbTaskbar);
            g3.Controls.Add(_rbFloat);

            _auto = new CheckBox();
            _auto.Text = "开机后自动启动";
            _auto.SetBounds(20, 318, 300, 22);
            _auto.Checked = current.AutoStart;

            Label hint = Dlg.Lbl("提示：贴着任务栏时，若任务栏自动隐藏、竖向放置、或按钮排满，\r\n会自动退回“浮在托盘上方”模式。",
                20, 344, 390, 34);

            Button ok = Dlg.Btn("确定", 232, 388, 80, 28);
            ok.Click += delegate { AcceptInput(); };
            Button cancel = Dlg.Btn("取消", 320, 388, 84, 28);
            cancel.DialogResult = DialogResult.Cancel;

            Controls.Add(g1);
            Controls.Add(g2);
            Controls.Add(g3);
            Controls.Add(_auto);
            Controls.Add(hint);
            Controls.Add(ok);
            Controls.Add(cancel);
            CancelButton = cancel;
        }

        private void AcceptInput()
        {
            Settings s = new Settings();
            s.ScrollEnabled = _scroll.Checked;
            s.ScrollSeconds = (int)_sec.Value;
            s.ShowCustom = _custom.Checked;
            s.ShowRoutine = _routine.Checked;
            s.ShowFuture = _future.Checked;
            s.AutoStart = _auto.Checked;
            s.Placement = _rbFloat.Checked ? 1 : 0;
            Result = s;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
