using System;
using System.Threading;
using System.Windows.Forms;

namespace CopyFeedback
{
    internal static class Program
    {
        public const string AppName = "复制反馈工具";
        public const string Version = "1.0.0";
        public const string MutexName = "Local\\CopyFeedback.SingleInstance";
        public const string QuitEventName = "Local\\CopyFeedback.Quit";

        [STAThread]
        private static void Main(string[] args)
        {
            CommandLine commandLine = new CommandLine(args);
            Logger.Init(commandLine.Debug);

            // ---- 自动化 / 安装辅助命令（不需要主实例，也不弹窗）----
            if (commandLine.EnableAutostart || commandLine.DisableAutostart || commandLine.AutostartStatus)
            {
                int exitCode = 0;
                try
                {
                    if (commandLine.EnableAutostart)
                    {
                        exitCode = AutostartManager.Enable() ? 0 : 1;
                        Logger.Info("CLI action=enable-autostart exit=" + exitCode);
                    }
                    else if (commandLine.DisableAutostart)
                    {
                        exitCode = AutostartManager.Disable() ? 0 : 1;
                        Logger.Info("CLI action=disable-autostart exit=" + exitCode);
                    }
                    else
                    {
                        exitCode = AutostartManager.IsEnabledAndCurrent() ? 0 : 1;
                        Logger.Info("CLI action=autostart-status exit=" + exitCode);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("执行开机自启动命令失败", ex);
                    exitCode = 2;
                }
                Logger.Shutdown();
                Environment.ExitCode = exitCode;
                return;
            }

            if (commandLine.Quit)
            {
                SignalQuit();
                Logger.Shutdown();
                Environment.ExitCode = 0;
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += OnThreadException;
                AppDomain.CurrentDomain.UnhandledException += OnDomainException;
            }
            catch (Exception ex)
            {
                Logger.Error("初始化 WinForms 失败", ex);
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    Logger.Info("INSTANCE already-running args=\"" + commandLine.RawArgs + "\"");
                    Logger.Shutdown();
                    if (!commandLine.Startup)
                    {
                        ShowAlreadyRunning();
                    }
                    Environment.ExitCode = 0;
                    return;
                }

                try
                {
                    string autostart = "0";
                    try
                    {
                        autostart = AutostartManager.IsEnabled() ? "1" : "0";
                    }
                    catch
                    {
                    }

                    Logger.Info(string.Format(
                        "START pid={0} exe=\"{1}\" args=\"{2}\" version={3} autostart={4}",
                        System.Diagnostics.Process.GetCurrentProcess().Id,
                        AutostartManager.CurrentExePath,
                        commandLine.RawArgs,
                        Version,
                        autostart));

                    using (TrayAppContext context = new TrayAppContext(commandLine))
                    {
                        Application.Run(context);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("主循环发生未处理异常", ex);
                }
                finally
                {
                    Logger.Shutdown();
                }
            }
        }

        private static void SignalQuit()
        {
            try
            {
                using (EventWaitHandle quitEvent = EventWaitHandle.OpenExisting(QuitEventName))
                {
                    quitEvent.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // 没有正在运行的实例，--quit 视为成功
            }
            catch (Exception ex)
            {
                Logger.Error("发送退出通知失败", ex);
            }
        }

        private static void ShowAlreadyRunning()
        {
            try
            {
                MessageBox.Show(
                    AppName + " 已在后台运行。\n\n请在 Windows 系统托盘（可能位于 \"^\" 折叠区）中查看图标。",
                    AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("显示已运行提示失败", ex);
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Logger.Error("UI 线程未处理异常", e == null ? null : e.Exception);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e == null ? null : (e.ExceptionObject as Exception);
            Logger.Error("进程未处理异常", ex);
        }
    }
}
