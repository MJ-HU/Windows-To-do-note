using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarMemo
{
    /// <summary>
    /// 应用主体：托盘图标 + 菜单 + 窗件 + 三个分区队列 + 位置维持。
    ///
    /// 三个分区（自定义 / 常规 / 远期）各自维护队列与游标，同屏并列显示；
    /// 双击某个分区只完成那个分区的当前条目。
    /// </summary>
    public class TrayContext : ApplicationContext
    {
        private const int CustomSlot = 0;
        private const int RoutineSlot = 1;
        private const int FutureSlot = 2;

        private readonly AppData _data;
        private readonly ContextMenuStrip _menu;
        private readonly NotifyIcon _tray;
        private readonly MemoForm _form;
        private readonly Timer _scrollTimer;
        private readonly Timer _dayTimer;
        private readonly Timer _placeTimer;

        private List<DisplayEntry>[] _queues;
        private int[] _indices;
        private string _dayStamp;
        private bool _saveErrorShown;

        public TrayContext()
        {
            _data = Store.Load();
            _data.Settings.AutoStart = AutoStart.IsEnabled();

            _queues = NewQueues();
            _indices = new int[3];
            _dayStamp = DayUtil.DateKey(DateTime.Today);

            _menu = BuildMenu();

            _tray = new NotifyIcon();
            _tray.Text = "任务栏备忘";
            try
            {
                _tray.Icon = AppIcon.Create();
            }
            catch
            {
                _tray.Icon = SystemIcons.Application;
            }
            _tray.ContextMenuStrip = _menu;
            _tray.MouseDoubleClick += OnTrayDoubleClick;
            _tray.Visible = true;

            _form = new MemoForm();
            _form.CompleteSectionRequested += OnCompleteSection;
            _form.MenuRequested += OnMenuRequested;

            _scrollTimer = new Timer();
            _scrollTimer.Tick += OnScrollTick;

            _dayTimer = new Timer();
            _dayTimer.Interval = 30000;
            _dayTimer.Tick += OnDayTick;
            _dayTimer.Start();

            _placeTimer = new Timer();
            _placeTimer.Interval = 800;
            _placeTimer.Tick += OnPlaceTick;
            _placeTimer.Start();

            RefreshQueues(false);
            ApplyPlacement();
            Persist();

            if (Store.LastProblem != null)
            {
                MessageBox.Show(Store.LastProblem, "任务栏备忘 - 数据提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static List<DisplayEntry>[] NewQueues()
        {
            List<DisplayEntry>[] q = new List<DisplayEntry>[3];
            for (int i = 0; i < 3; i++) q[i] = new List<DisplayEntry>();
            return q;
        }

        // ------------------------------------------------------------ 菜单

        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.ShowImageMargin = false;

            m.Items.Add(Item("自定义备忘(&C)...", delegate { EditCustom(); }));
            m.Items.Add(Item("常规备忘(&R)...", delegate { EditRoutine(); }));
            m.Items.Add(Item("远期备忘(&F)...", delegate { EditFuture(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("设置(&S)...", delegate { OpenSettings(); }));
            m.Items.Add(Item("数据位置(&D)...（复制路径）", delegate { OpenDataFolder(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("退出(&X)", delegate { ExitApp(); }));

            return m;
        }

        private static ToolStripMenuItem Item(string text, EventHandler onClick)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Click += onClick;
            return it;
        }

        private void OnMenuRequested(object sender, EventArgs e)
        {
            try
            {
                _menu.Show(Cursor.Position);
            }
            catch
            {
            }
        }

        private void OnTrayDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            try
            {
                if (!_form.Visible) ApplyPlacement();
                _form.Invalidate();
            }
            catch
            {
            }
        }

        private void OpenDataFolder()
        {
            string dir = Store.DataDir;

            try
            {
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                Store.Log("创建数据目录失败: " + ex.Message);
            }

            bool copied = false;
            try
            {
                Clipboard.SetText(dir);
                copied = true;
            }
            catch
            {
            }

            MessageBox.Show(
                "数据目录：\r\n" + dir + "\r\n\r\n" +
                (copied ? "路径已复制到剪贴板。\r\n\r\n" : "") +
                "要打开的话，把上面的路径粘贴到资源管理器地址栏即可。\r\n\r\n" +
                "说明：本程序不主动启动外部进程。受限环境里新起进程会失败" +
                "（0xc0000142），强行拉 explorer 只会弹一个没用的错误框。",
                "任务栏备忘 - 数据位置", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------ 位置

        private void OnPlaceTick(object sender, EventArgs e)
        {
            ApplyPlacement();

            // 任务栏也是 topmost 窗口，切窗口时会把它抬到我们上面去。
            // 这里周期性重新声明 topmost，让备忘始终可见。
            try
            {
                if (_form.Visible) Shell.RaiseTopMost(_form.Handle);
            }
            catch
            {
            }
        }

        private int VisibleSectionCount()
        {
            int n = 0;
            for (int k = 0; k < 3; k++)
            {
                if (_queues[k] != null && _queues[k].Count > 0) n++;
            }
            return n;
        }

        /// <summary>按设置决定"贴任务栏"还是"浮在托盘上方"，探测失败自动回退。</summary>
        private void ApplyPlacement()
        {
            try
            {
                bool wantBar = _data.Settings.Placement == 0;
                Rectangle slot;

                // 三个分区要平分宽度，所以比单条显示时给得宽一些
                if (wantBar && Shell.TryGetTaskbarSlot(340, out slot))
                {
                    bool dark = Shell.TaskbarIsDark();
                    _form.SetMode(true,
                        dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243),
                        dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(28, 28, 28),
                        dark ? Color.FromArgb(150, 170, 176, 184) : Color.FromArgb(150, 90, 96, 104));

                    if (_form.Bounds != slot) _form.Bounds = slot;
                    if (!_form.Visible) _form.Show();
                    return;
                }

                _form.SetMode(false,
                    Color.FromArgb(26, 28, 32),
                    Color.FromArgb(246, 250, 251, 253),
                    Color.FromArgb(150, 200, 206, 214));

                Size card = new Size(300, MemoForm.CardHeight(VisibleSectionCount()));
                if (_form.ClientSize != card) _form.ClientSize = card;
                _form.SnapToCorner();
                if (!_form.Visible) _form.Show();
            }
            catch (Exception ex)
            {
                Store.Log("定位窗件失败: " + ex);
            }
        }

        // ------------------------------------------------------------ 队列

        private static bool HasText(string s)
        {
            return s != null && s.Trim().Length > 0;
        }

        private List<DisplayEntry>[] BuildQueues()
        {
            List<DisplayEntry>[] q = NewQueues();
            Settings s = _data.Settings;
            string today = DayUtil.DateKey(DateTime.Today);
            int dow = DayUtil.MondayIndex(DateTime.Today);

            if (s.ShowCustom)
            {
                foreach (MemoItem m in _data.Customs)
                {
                    if (!m.Done && HasText(m.Text))
                    {
                        q[CustomSlot].Add(new DisplayEntry(MemoKind.Custom, m.Text, m));
                    }
                }
            }

            if (s.ShowRoutine)
            {
                foreach (RoutineItem r in _data.Routines)
                {
                    if (r.Day != dow) continue;
                    if (!r.Enabled) continue;
                    if (!HasText(r.Text)) continue;
                    if (r.LastDone == today) continue;   // 今天已完成，明天再来
                    q[RoutineSlot].Add(new DisplayEntry(MemoKind.Routine, r.Text, r));
                }
            }

            if (s.ShowFuture)
            {
                foreach (FutureItem f in _data.Futures)
                {
                    if (!f.Done && HasText(f.Text) && f.Date == today)
                    {
                        q[FutureSlot].Add(new DisplayEntry(MemoKind.Future, f.Text, f));
                    }
                }
            }

            return q;
        }

        private void RefreshQueues(bool keepIndex)
        {
            int[] old = new int[3];
            for (int k = 0; k < 3; k++) old[k] = _indices[k];

            _queues = BuildQueues();

            for (int k = 0; k < 3; k++)
            {
                int idx = keepIndex ? old[k] : 0;
                if (_queues[k].Count == 0)
                {
                    idx = 0;
                }
                else
                {
                    if (idx < 0) idx = 0;
                    if (idx >= _queues[k].Count) idx = _queues[k].Count - 1;
                }
                _indices[k] = idx;
            }

            Render();
            ApplyScrollTimer();
        }

        private void Render()
        {
            List<SectionData> sections = new List<SectionData>();
            List<string> tips = new List<string>();

            for (int k = 0; k < 3; k++)
            {
                List<DisplayEntry> q = _queues[k];
                if (q == null || q.Count == 0) continue;

                int idx = _indices[k];
                if (idx < 0) idx = 0;
                if (idx >= q.Count) idx = q.Count - 1;

                SectionData sd = new SectionData();
                sd.Kind = q[idx].Kind;
                sd.Text = q[idx].Text;
                sd.Index = idx + 1;
                sd.Total = q.Count;
                sections.Add(sd);

                tips.Add(MemoForm.TagOf(sd.Kind) + ":" + sd.Text);
            }

            if (sections.Count == 0)
            {
                string msg = AnyCompletedToday() ? "今日备忘已全部完成" : "今日无待办备忘";
                _form.ShowEmpty(msg);
                SetTrayText("任务栏备忘 - " + msg);
                return;
            }

            _form.ShowSections(sections.ToArray());
            SetTrayText(string.Join(" | ", tips.ToArray()));
        }

        private void SetTrayText(string raw)
        {
            string t = raw == null ? "" : raw;
            t = t.Replace("\r", " ").Replace("\n", " ").Trim();
            if (t.Length > 60) t = t.Substring(0, 59) + "…";
            if (t.Length == 0) t = "任务栏备忘";
            try
            {
                _tray.Text = t;
            }
            catch
            {
            }
        }

        /// <summary>
        /// 今天是否真的完成过东西。
        /// 注意：Done 是永久标记且列表从不清理，所以必须比对完成日期，
        /// 否则上周做完的一条会让今天误报"今日备忘已全部完成"。
        /// </summary>
        private bool AnyCompletedToday()
        {
            string today = DayUtil.DateKey(DateTime.Today);
            foreach (MemoItem m in _data.Customs)
            {
                if (m.Done && m.DoneDate == today) return true;
            }
            foreach (FutureItem f in _data.Futures)
            {
                if (f.Done && f.DoneDate == today) return true;
            }
            foreach (RoutineItem r in _data.Routines)
            {
                if (r.LastDone == today) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ 完成某个分区

        private void OnCompleteSection(object sender, SectionEventArgs e)
        {
            CompleteSection(e.Kind);
        }

        private static int KindSlot(MemoKind kind)
        {
            if (kind == MemoKind.Routine) return RoutineSlot;
            if (kind == MemoKind.Future) return FutureSlot;
            return CustomSlot;
        }

        private void CompleteSection(MemoKind kind)
        {
            int k = KindSlot(kind);
            List<DisplayEntry> q = _queues[k];
            if (q == null || q.Count == 0) return;

            int idx = _indices[k];
            if (idx < 0 || idx >= q.Count) idx = 0;

            DisplayEntry e = q[idx];
            string today = DayUtil.DateKey(DateTime.Today);

            if (e.Kind == MemoKind.Custom)
            {
                MemoItem m = e.Source as MemoItem;
                if (m != null)
                {
                    m.Done = true;
                    m.DoneDate = today;
                }
            }
            else if (e.Kind == MemoKind.Routine)
            {
                RoutineItem r = e.Source as RoutineItem;
                if (r != null) r.LastDone = today;
            }
            else
            {
                FutureItem f = e.Source as FutureItem;
                if (f != null)
                {
                    f.Done = true;
                    f.DoneDate = today;
                }
            }

            Persist();
            RefreshQueues(true);
        }

        // ------------------------------------------------------------ 保存

        /// <summary>所有落盘点都走这里，保存失败要让人看见，不能静默丢数据。</summary>
        private void Persist()
        {
            if (Store.Save(_data)) return;
            if (_saveErrorShown) return;
            _saveErrorShown = true;
            MessageBox.Show(
                "数据保存失败，本次修改可能无法保留。\r\n\r\n" +
                "错误信息：\r\n" +
                (Store.LastError == null ? "（无）" : Store.LastError) +
                "\r\n\r\n当前数据目录：\r\n" + Store.DataDir +
                "\r\n\r\n程序已尝试 %APPDATA%、%LOCALAPPDATA%、exe 所在目录、" +
                "%TEMP% 和注册表，全部失败才会弹这个框。请把上面的错误信息发给我。",
                "任务栏备忘 - 保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ------------------------------------------------------------ 计时器

        private void ApplyScrollTimer()
        {
            if (_scrollTimer == null) return;

            _scrollTimer.Stop();

            int sec = _data.Settings.ScrollSeconds;
            if (sec < 1) sec = 1;
            if (sec > 3600) sec = 3600;

            bool anyMulti = false;
            for (int k = 0; k < 3; k++)
            {
                if (_queues[k] != null && _queues[k].Count > 1)
                {
                    anyMulti = true;
                    break;
                }
            }

            if (_data.Settings.ScrollEnabled && anyMulti)
            {
                _scrollTimer.Interval = sec * 1000;
                _scrollTimer.Start();
            }
        }

        private void OnScrollTick(object sender, EventArgs e)
        {
            bool changed = false;
            for (int k = 0; k < 3; k++)
            {
                if (_queues[k] == null || _queues[k].Count <= 1) continue;
                _indices[k] = (_indices[k] + 1) % _queues[k].Count;
                changed = true;
            }
            if (changed) Render();
        }

        private void OnDayTick(object sender, EventArgs e)
        {
            string today = DayUtil.DateKey(DateTime.Today);
            if (today == _dayStamp) return;
            _dayStamp = today;
            RefreshQueues(false);
        }

        // ------------------------------------------------------------ 对话框

        private void RunDialog(Form dlg)
        {
            bool top = _form.TopMost;
            _form.TopMost = false;
            try
            {
                // 用窗件当 owner，保证对话框拿到前台和键盘焦点。
                dlg.ShowDialog(_form);
            }
            finally
            {
                _form.TopMost = top;
            }
        }

        private void EditCustom()
        {
            List<string> pending = new List<string>();
            foreach (MemoItem m in _data.Customs)
            {
                if (!m.Done && HasText(m.Text)) pending.Add(m.Text);
            }

            using (CustomDialog dlg = new CustomDialog(pending))
            {
                RunDialog(dlg);
                if (dlg.DialogResult != DialogResult.OK) return;

                List<MemoItem> kept = new List<MemoItem>();
                foreach (MemoItem m in _data.Customs)
                {
                    if (m.Done) kept.Add(m);
                }
                foreach (string line in dlg.Lines)
                {
                    kept.Add(new MemoItem(line));
                }
                _data.Customs = kept;

                Persist();
                RefreshQueues(false);
            }
        }

        private void EditRoutine()
        {
            using (RoutineDialog dlg = new RoutineDialog(_data.Routines))
            {
                RunDialog(dlg);
                if (dlg.DialogResult != DialogResult.OK) return;

                _data.Routines = dlg.Result;
                Persist();
                RefreshQueues(false);
            }
        }

        private void EditFuture()
        {
            using (FutureDialog dlg = new FutureDialog(_data.Futures))
            {
                RunDialog(dlg);
                if (dlg.DialogResult != DialogResult.OK) return;

                _data.Futures = dlg.Result;
                Persist();
                RefreshQueues(false);
            }
        }

        private void OpenSettings()
        {
            using (SettingsDialog dlg = new SettingsDialog(_data.Settings))
            {
                RunDialog(dlg);
                if (dlg.DialogResult != DialogResult.OK || dlg.Result == null) return;

                _data.Settings = dlg.Result;

                bool autoOk = AutoStart.Set(_data.Settings.AutoStart);
                if (!autoOk && _data.Settings.AutoStart)
                {
                    MessageBox.Show(
                        "开机自启设置失败。\r\n\r\n" +
                        "程序先尝试写入注册表（HKCU\\...\\Run），被系统拒绝后又尝试了启动文件夹，" +
                        "都失败了。\r\n\r\n详情见 error.log。设置里的其它选项不受影响。",
                        "任务栏备忘", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (autoOk)
                {
                    _data.Settings.AutoStart = AutoStart.IsEnabled();
                }

                Persist();
                RefreshQueues(false);
            }

            ApplyPlacement();
        }

        // ------------------------------------------------------------ 退出

        private void ExitApp()
        {
            Persist();

            try
            {
                _placeTimer.Stop();
                _placeTimer.Dispose();
                _dayTimer.Stop();
                _dayTimer.Dispose();
                _scrollTimer.Stop();
                _scrollTimer.Dispose();
            }
            catch
            {
            }

            try
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            catch
            {
            }

            try
            {
                _form.Hide();
                _form.Dispose();
            }
            catch
            {
            }

            ExitThread();
        }
    }
}
