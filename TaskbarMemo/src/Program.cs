using System;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarMemo
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Mutex mutex = null;
            bool created = false;

            try
            {
                mutex = new Mutex(true, "TaskbarMemo_SingleInstance_{8F2A1C4E}", out created);
                if (!created)
                {
                    // 已经在运行：别静默退出，否则用户以为双击没反应。
                    MessageBox.Show("任务栏备忘已经在运行了。\r\n请查看任务栏右下角的托盘图标。",
                        "任务栏备忘", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += OnThreadException;
                AppDomain.CurrentDomain.UnhandledException += OnDomainException;

                Application.Run(new TrayContext());
            }
            catch (Exception ex)
            {
                ShowError(ex, "启动失败");
            }
            finally
            {
                if (mutex != null)
                {
                    try
                    {
                        if (created) mutex.ReleaseMutex();
                    }
                    catch
                    {
                    }
                    mutex.Dispose();
                }
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ShowError(e.Exception, "运行错误");
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            ShowError(e.ExceptionObject as Exception, "运行错误");
        }

        private static void ShowError(Exception ex, string title)
        {
            if (ex == null) return;
            Store.Log(title + ": " + ex);
            try
            {
                MessageBox.Show(ex.ToString(), "任务栏备忘 - " + title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
            }
        }
    }
}
