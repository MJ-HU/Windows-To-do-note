using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace TaskbarMemo
{
    /// <summary>
    /// 数据持久化。写入失败过就说明"只写一个固定位置"这件事本身不可靠，
    /// 所以这里做成分级回退：
    ///
    ///   1. %APPDATA%\TaskbarMemo\data.xml      （漫游，首选）
    ///   2. %LOCALAPPDATA%\TaskbarMemo\data.xml （漫游不可写时的常见退路）
    ///   3. exe 同目录\TaskbarMemoData\data.xml
    ///   4. %TEMP%\TaskbarMemo\data.xml
    ///   5. 注册表 HKCU\Software\TaskbarMemo    （文件系统彻底写不了时兜底）
    ///
    /// 三条硬性约束：
    ///   A. 任何情况下都不能"先清空再写"，序列化失败不许碰到已有数据。
    ///   B. 断电/被杀进程留下的中间文件（.tmp/.new）比正式文件新时，要能捡回来。
    ///   C. 上次成功保存的位置记在注册表里，重启后先看那里，避免被旧位置的
    ///      陈旧副本盖掉。
    /// </summary>
    public static class Store
    {
        private const string RegKeyPath = @"Software\TaskbarMemo";
        private const string RegValueName = "data.xml";
        private const string RegDirName = "DataDir";
        private const string RegMarker = "@registry";

        private static string _activeDir;

        /// <summary>上一次加载出的问题，null 表示一切正常。</summary>
        public static string LastProblem { get; private set; }

        /// <summary>上一次保存失败的真实原因，null 表示没失败过。</summary>
        public static string LastError { get; private set; }

        public static string DataDir
        {
            get
            {
                if (_activeDir == null) _activeDir = ResolveDir();
                return _activeDir;
            }
        }

        public static string DataFile
        {
            get { return Path.Combine(DataDir, "data.xml"); }
        }

        public static string BakFile
        {
            get { return Path.Combine(DataDir, "data.bak.xml"); }
        }

        // ---------------------------------------------------------------- 候选目录

        private static List<string> Candidates()
        {
            List<string> list = new List<string>();

            try
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (roaming != null && roaming.Length > 0)
                {
                    list.Add(Path.Combine(roaming, "TaskbarMemo"));
                }
            }
            catch
            {
            }

            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (local != null && local.Length > 0)
                {
                    list.Add(Path.Combine(local, "TaskbarMemo"));
                }
            }
            catch
            {
            }

            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                if (exeDir != null && exeDir.Length > 0)
                {
                    // 不要 TrimEnd('\\')：exe 在盘根时会变成 "C:"，Path.Combine
                    // 会拼出驱动器相对路径 "C:TaskbarMemoData"。
                    list.Add(Path.Combine(exeDir, "TaskbarMemoData"));
                }
            }
            catch
            {
            }

            try
            {
                string temp = Path.GetTempPath();
                if (temp != null && temp.Length > 0)
                {
                    list.Add(Path.Combine(temp, "TaskbarMemo"));
                }
            }
            catch
            {
            }

            return list;
        }

        private static string ResolveDir()
        {
            List<string> cands = Candidates();

            foreach (string d in cands)
            {
                if (FileExistsSafe(Path.Combine(d, "data.xml"))) return d;
            }

            foreach (string d in cands)
            {
                try
                {
                    if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                    if (Directory.Exists(d)) return d;
                }
                catch
                {
                }
            }

            return cands.Count > 0 ? cands[cands.Count - 1] : "TaskbarMemo";
        }

        // ---------------------------------------------------------------- 小工具

        private static bool FileExistsSafe(string path)
        {
            try
            {
                return !string.IsNullOrEmpty(path) && File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static DateTime LastWriteSafe(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static void AddSource(List<string> list, string path)
        {
            if (FileExistsSafe(path)) list.Add(path);
        }

        private static string Append(string a, string b)
        {
            if (b == null || b.Length == 0) return a;
            if (a == null || a.Length == 0) return b;
            return a + "\r\n\r\n" + b;
        }

        private static void SortNewestFirst(List<string> list)
        {
            list.Sort(delegate(string a, string b)
            {
                return LastWriteSafe(b).CompareTo(LastWriteSafe(a));
            });
        }

        private static bool IsRegistryMarker(string s)
        {
            return s != null && s.Length > 0 && s[0] == '@';
        }

        // ---------------------------------------------------------------- 读

        public static AppData Load()
        {
            LastProblem = null;
            List<string> cands = Candidates();
            string marker = ReadDirMarker();

            // 1) 上次成功保存的位置优先，避免被其它位置的陈旧副本盖掉
            if (!string.IsNullOrEmpty(marker))
            {
                if (IsRegistryMarker(marker))
                {
                    AppData reg = LoadFromRegistry();
                    if (reg != null)
                    {
                        _activeDir = null;
                        return Fix(reg);
                    }
                }
                else
                {
                    string main = Path.Combine(marker, "data.xml");
                    if (FileExistsSafe(main))
                    {
                        bool ok = false;
                        AppData d = TryRead(main, out ok);
                        if (ok && d != null)
                        {
                            _activeDir = marker;
                            return Fix(d);
                        }
                        RenameCorrupt(main);
                    }
                }
            }

            // 2) 所有位置的正式文件与中断残留（.tmp/.new），按修改时间从新到旧，
            //    取第一个能解析成功的。这样"新写的临时文件"不会被"旧的主文件"挡住。
            List<string> sources = new List<string>();
            foreach (string dir in cands)
            {
                AddSource(sources, Path.Combine(dir, "data.xml"));
                AddSource(sources, Path.Combine(dir, "data.xml.tmp"));
                AddSource(sources, Path.Combine(dir, "data.xml.new"));
            }
            SortNewestFirst(sources);

            foreach (string path in sources)
            {
                bool ok = false;
                AppData d = TryRead(path, out ok);
                if (ok && d != null)
                {
                    _activeDir = SafeDirName(path);
                    if (path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".new", StringComparison.OrdinalIgnoreCase))
                    {
                        LastProblem = Append(LastProblem, "上次写入未完成，已从残留文件恢复：" + path);
                    }
                    return Fix(d);
                }
                RenameCorrupt(path);
            }

            // 3) 备份
            List<string> baks = new List<string>();
            foreach (string dir in cands)
            {
                AddSource(baks, Path.Combine(dir, "data.bak.xml"));
            }
            SortNewestFirst(baks);

            foreach (string path in baks)
            {
                bool ok = false;
                AppData d = TryRead(path, out ok);
                if (ok && d != null)
                {
                    _activeDir = SafeDirName(path);
                    LastProblem = Append(LastProblem, "已从备份恢复：" + path);
                    return Fix(d);
                }
            }

            // 4) 注册表
            AppData regData = LoadFromRegistry();
            if (regData != null)
            {
                _activeDir = null;
                LastProblem = Append(LastProblem, "已从注册表 HKCU\\Software\\TaskbarMemo 恢复。");
                return Fix(regData);
            }

            _activeDir = null;   // 交给 ResolveDir 决定写哪儿
            return new AppData();
        }

        private static string SafeDirName(string path)
        {
            try
            {
                return Path.GetDirectoryName(path);
            }
            catch
            {
                return null;
            }
        }

        private static void RenameCorrupt(string path)
        {
            Log("数据文件无法解析: " + path);
            try
            {
                string dir = SafeDirName(path);
                if (dir == null) return;
                string keep = Path.Combine(dir,
                    Path.GetFileName(path) + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                File.Move(path, keep);
                LastProblem = Append(LastProblem, "数据文件损坏，已另存为：" + keep);
            }
            catch
            {
            }
        }

        private static AppData TryRead(string path, out bool ok)
        {
            ok = false;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    XmlSerializer ser = new XmlSerializer(typeof(AppData));
                    AppData d = ser.Deserialize(fs) as AppData;
                    if (d == null) return null;
                    ok = true;
                    return d;
                }
            }
            catch (Exception ex)
            {
                Log("读取 " + path + " 失败: " + ex.Message);
                return null;
            }
        }

        // ---------------------------------------------------------------- 写

        public static bool Save(AppData d)
        {
            LastError = null;
            if (d == null) return false;

            List<string> order = new List<string>();
            string prefer = DataDir;
            order.Add(prefer);
            foreach (string c in Candidates())
            {
                if (!string.Equals(c, prefer, StringComparison.OrdinalIgnoreCase)) order.Add(c);
            }

            string firstErr = null;

            foreach (string dir in order)
            {
                string err;
                if (SaveToDir(dir, d, out err))
                {
                    _activeDir = dir;
                    WriteDirMarker(dir);
                    return true;
                }
                if (firstErr == null && err != null) firstErr = err;
            }

            // 文件系统全线失败 → 注册表兜底
            string regErr;
            if (SaveToRegistry(d, out regErr))
            {
                _activeDir = null;
                WriteDirMarker(RegMarker);
                return true;
            }

            LastError = firstErr == null ? "文件系统写入失败。" : firstErr;
            LastError = Append(LastError, "注册表写入也失败：" + (regErr == null ? "未知原因" : regErr));
            LastError = Append(LastError, "已尝试的目录：\r\n" + string.Join("\r\n", order.ToArray()));
            return false;
        }

        private static bool SaveToDir(string dir, AppData d, out string error)
        {
            error = null;

            string final = Path.Combine(dir, "data.xml");
            string tmp = final + ".tmp";
            string fresh = final + ".new";
            string bak = Path.Combine(dir, "data.bak.xml");

            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                error = "无法创建目录 " + dir + "：" + ex.Message;
                Log("保存失败（建目录）: " + ex.Message);
                return false;
            }

            Exception first = null;

            // 方案一：写 .tmp，再原子替换；旧的留成 data.bak.xml
            try
            {
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    XmlSerializer ser = new XmlSerializer(typeof(AppData));
                    ser.Serialize(fs, d);
                }

                if (FileExistsSafe(final))
                {
                    File.Replace(tmp, final, bak, true);
                }
                else
                {
                    File.Move(tmp, final);
                }
                return true;
            }
            catch (Exception ex)
            {
                first = ex;
            }

            // 方案二：File.Replace 在 FAT/exFAT/网络盘上不支持，改成整体覆盖。
            // 关键点：先把完整内容写进 .new，序列化失败时**碰不到** data.xml；
            // 再 .new → data.xml 整体拷贝。就算拷贝中途失败，.new 里仍是一份完整数据，
            // 下次 Load 会把它当恢复源（它比 data.xml 新）。
            try
            {
                using (FileStream fs = new FileStream(fresh, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    XmlSerializer ser = new XmlSerializer(typeof(AppData));
                    ser.Serialize(fs, d);
                }

                File.Copy(fresh, final, true);

                try
                {
                    if (FileExistsSafe(tmp)) File.Delete(tmp);
                }
                catch
                {
                }
                try
                {
                    if (FileExistsSafe(fresh)) File.Delete(fresh);
                }
                catch
                {
                }
                return true;
            }
            catch (Exception ex2)
            {
                error = "写入 " + final + " 失败：" + ex2.Message +
                        (first != null ? "（原子替换也失败：" + first.Message + "）" : "");
                Log("保存失败（" + dir + "）: " + (first == null ? "" : first.Message) + " || " + ex2.Message);
                return false;
            }
        }

        // ---------------------------------------------------------------- 位置标记

        private static string ReadDirMarker()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegKeyPath, false))
                {
                    if (k == null) return null;
                    return k.GetValue(RegDirName) as string;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void WriteDirMarker(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegKeyPath))
                {
                    if (k != null) k.SetValue(RegDirName, value, RegistryValueKind.String);
                }
            }
            catch
            {
            }
        }

        // ---------------------------------------------------------------- 注册表

        private static bool SaveToRegistry(AppData d, out string error)
        {
            error = null;
            try
            {
                string xml = ToXmlString(d);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegKeyPath))
                {
                    if (k == null)
                    {
                        error = "无法打开注册表项 " + RegKeyPath;
                        return false;
                    }
                    k.SetValue(RegValueName, xml, RegistryValueKind.String);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "写入注册表失败：" + ex.Message;
                Log("保存失败（注册表）: " + ex.Message);
                return false;
            }
        }

        private static AppData LoadFromRegistry()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegKeyPath, false))
                {
                    if (k == null) return null;
                    string xml = k.GetValue(RegValueName) as string;
                    if (xml == null || xml.Length == 0) return null;

                    using (StringReader sr = new StringReader(xml))
                    {
                        XmlSerializer ser = new XmlSerializer(typeof(AppData));
                        return ser.Deserialize(sr) as AppData;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("读取注册表数据失败: " + ex.Message);
                return null;
            }
        }

        private static string ToXmlString(AppData d)
        {
            using (StringWriter sw = new StringWriter())
            {
                XmlSerializer ser = new XmlSerializer(typeof(AppData));
                ser.Serialize(sw, d);
                return sw.ToString();
            }
        }

        // ---------------------------------------------------------------- 规范化

        private static AppData Fix(AppData d)
        {
            if (d == null) d = new AppData();
            if (d.Settings == null) d.Settings = new Settings();
            if (d.Customs == null) d.Customs = new List<MemoItem>();
            if (d.Routines == null) d.Routines = new List<RoutineItem>();
            if (d.Futures == null) d.Futures = new List<FutureItem>();

            if (d.Settings.ScrollSeconds < 1) d.Settings.ScrollSeconds = 10;
            if (d.Settings.ScrollSeconds > 3600) d.Settings.ScrollSeconds = 3600;
            if (d.Settings.Placement < 0 || d.Settings.Placement > 1) d.Settings.Placement = 0;

            foreach (MemoItem m in d.Customs)
            {
                if (m.Text == null) m.Text = "";
                if (m.DoneDate == null) m.DoneDate = "";
            }
            foreach (FutureItem f in d.Futures)
            {
                if (f.Text == null) f.Text = "";
                if (f.Date == null) f.Date = "";
                if (f.DoneDate == null) f.DoneDate = "";
            }

            MigrateRoutines(d);
            return d;
        }

        /// <summary>把旧版"一条任务 + 星期掩码"展开成"每天一条独立任务"。</summary>
        private static void MigrateRoutines(AppData d)
        {
            bool needMigrate = false;
            foreach (RoutineItem r in d.Routines)
            {
                if (r.Day < 0 || r.Day > 6) { needMigrate = true; break; }
            }

            if (!needMigrate)
            {
                foreach (RoutineItem r in d.Routines)
                {
                    if (r.Text == null) r.Text = "";
                    if (r.LastDone == null) r.LastDone = "";
                }
                return;
            }

            List<RoutineItem> list = new List<RoutineItem>();
            foreach (RoutineItem r in d.Routines)
            {
                if (r.Day >= 0 && r.Day <= 6)
                {
                    if (r.Text == null) r.Text = "";
                    if (r.LastDone == null) r.LastDone = "";
                    list.Add(r);
                    continue;
                }

                int mask = r.DaysMask;
                if (mask <= 0 || mask > 127) mask = 127;
                for (int i = 0; i < 7; i++)
                {
                    if (((mask >> i) & 1) != 1) continue;
                    RoutineItem n = new RoutineItem();
                    n.Day = i;
                    n.Text = r.Text == null ? "" : r.Text;
                    n.Enabled = true;
                    n.LastDone = r.LastDone == null ? "" : r.LastDone;
                    list.Add(n);
                }
            }
            d.Routines = list;
        }

        // ---------------------------------------------------------------- 日志

        public static void Log(string message)
        {
            if (message == null) return;

            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " +
                          message + Environment.NewLine;

            // 首选数据目录
            try
            {
                string dir = DataDir;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), line);
                return;
            }
            catch
            {
            }

            // 退路：临时目录。日志一定要落得下来，否则出问题时完全没有线索。
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "TaskbarMemo-error.log"), line);
            }
            catch
            {
            }
        }
    }
}
