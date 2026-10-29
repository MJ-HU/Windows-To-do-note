using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarMemo
{
    /// <summary>
    /// 开机自启。两条路：
    ///
    ///   1. 注册表 HKCU\...\Run —— 常规做法，但部分机器上 .NET 运行在受限安全
    ///      上下文，OpenSubKey(writable:true) 会抛 SecurityException。
    ///   2. 启动文件夹里的一个 .cmd —— 文件系统通常可写，作为注册表被拒时的兜底。
    ///
    /// 两条路都失败才算设置失败。
    /// </summary>
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "TaskbarMemo";
        private const string StartupFileName = "TaskbarMemo.cmd";

        private static string StartupDirPath
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.Startup); }
        }

        private static string StartupFilePath
        {
            get { return Path.Combine(StartupDirPath, StartupFileName); }
        }

        /// <summary>以实际状态为准：注册表或启动文件夹任一存在即为已启用。</summary>
        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k != null && k.GetValue(ValueName) != null) return true;
                }
            }
            catch
            {
                // 读注册表被拒是正常的（受限上下文），继续看启动文件夹
            }

            try
            {
                return File.Exists(StartupFilePath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>返回是否设置成功。失败原因写入 error.log。</summary>
        public static bool Set(bool enabled)
        {
            bool regDone = TryRegistry(enabled);
            if (regDone)
            {
                // 注册表既然能用，就清掉可能残留的启动文件夹兜底
                DeleteStartupFile();
                return true;
            }

            // 注册表被拒 → 改用启动文件夹
            return TryStartupFolder(enabled);
        }

        // ------------------------------------------------------------ 注册表

        private static bool TryRegistry(bool enabled)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return false;
                    if (enabled)
                    {
                        string exe = Application.ExecutablePath;
                        k.SetValue(ValueName, "\"" + exe + "\"", RegistryValueKind.String);
                    }
                    else
                    {
                        if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Store.Log("设置开机启动（注册表）失败，将改用启动文件夹: " + ex.Message);
                return false;
            }
        }

        // ------------------------------------------------------------ 启动文件夹

        private static bool TryStartupFolder(bool enabled)
        {
            try
            {
                string path = StartupFilePath;

                if (!enabled)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return true;
                }

                string dir = StartupDirPath;
                if (dir == null || dir.Length == 0) return false;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // 用系统默认 ANSI 编码写：.cmd 由 cmd.exe 按控制台代码页解析，
                // 写成 UTF-8 的话路径含中文时会乱码。
                StringBuilder sb = new StringBuilder();
                sb.Append("@echo off\r\n");
                sb.Append("start \"\" \"");
                sb.Append(Application.ExecutablePath);
                sb.Append("\"\r\n");

                File.WriteAllText(path, sb.ToString(), Encoding.Default);
                return File.Exists(path);
            }
            catch (Exception ex)
            {
                Store.Log("设置开机启动（启动文件夹）失败: " + ex.Message);
                return false;
            }
        }

        private static void DeleteStartupFile()
        {
            try
            {
                string path = StartupFilePath;
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
