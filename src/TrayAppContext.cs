using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace CopyFeedback
{
    /// <summary>
    /// 托盘应用主体：托盘图标/菜单、全局钩子、复制检测、提示弹窗、自启动开关、退出清理。
    /// </summary>
    internal sealed class TrayAppContext : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly Icon _trayIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _autostartItem;
        private readonly KeyboardHook _hook;
        private readonly CopyDetectionService _detector;
        private readonly ToastForm _toast;
        private readonly HiddenWindow _window;
        private readonly EventWaitHandle _quitEvent;
        private readonly RegisteredWaitHandle _quitWait;
        private readonly Queue<KeyTrigger> _pendingTriggers = new Queue<KeyTrigger>();
        private readonly Font _headerFont;
        private readonly Font _statusFont;

        private bool _cleaned;
        private bool _hookInstalled;
        private string _pendingQuitReason;

        public TrayAppContext(CommandLine commandLine)
        {
            _window = new HiddenWindow(HandlePendingTriggers, HandleQuitRequest);

            _toast = new ToastForm();
            _detector = new CopyDetectionService(delegate(CopyInfo info)
            {
                try
                {
                    _toast.ShowCopy(info);
                }
                catch (Exception ex)
                {
                    Logger.Error("显示复制提示失败", ex);
                }
            });

            // ---- 托盘图标与菜单 ----
            _trayIcon = AppIcon.Load();
            _tray = new NotifyIcon();
            _tray.Icon = _trayIcon;
            _tray.Text = "复制反馈工具 · 运行中";
            _tray.Visible = true;

            _menu = new ContextMenuStrip();
            _headerFont = new Font(SystemFonts.MenuFont, FontStyle.Bold);
            _statusFont = new Font(SystemFonts.MenuFont, FontStyle.Regular);

            ToolStripMenuItem header = new ToolStripMenuItem("复制反馈工具 v" + Program.Version);
            header.Enabled = false;
            header.Font = _headerFont;

            _statusItem = new ToolStripMenuItem("正在监听 Ctrl+C");
            _statusItem.Enabled = false;
            _statusItem.Font = _statusFont;

            _autostartItem = new ToolStripMenuItem("开机自动启动");
            _autostartItem.CheckOnClick = true;
            _autostartItem.Checked = SafeIsAutostartEnabled();
            _autostartItem.Click += OnAutostartItemClick;

            ToolStripMenuItem quitItem = new ToolStripMenuItem("退出");
            quitItem.Click += OnQuitItemClick;

            _menu.Items.Add(header);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_statusItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_autostartItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(quitItem);
            _menu.Opening += OnMenuOpening;

            _tray.ContextMenuStrip = _menu;
            Logger.Info("TRAY ready=1");

            // ---- 开机自启动状态（日志 + 必要时修复路径）----
            try
            {
                bool enabled = AutostartManager.IsEnabled();
                string command = AutostartManager.GetRegisteredCommand();
                Logger.Info(string.Format("AUTOSTART enabled={0} path=\"{1}\"", enabled ? 1 : 0, command == null ? string.Empty : command));
                AutostartManager.RepairIfBroken();
            }
            catch (Exception ex)
            {
                Logger.Error("检查开机自启动状态失败", ex);
            }

            // ---- 全局键盘钩子 ----
            _hook = new KeyboardHook();
            _hook.Triggered += OnKeyTriggered;
            _hookInstalled = _hook.Install();
            if (_hookInstalled)
            {
                Logger.Info("HOOK installed=1");
            }
            else
            {
                Logger.Info("HOOK installed=0 error=\"无法安装全局键盘钩子\"");
                _tray.ShowBalloonTip(5000, "复制反馈工具",
                    "无法安装全局键盘监听，复制提示可能不可用。", ToolTipIcon.Warning);
            }

            // ---- 进程间退出通知（供 --quit 使用）----
            EventWaitHandle quitEvent = null;
            try
            {
                quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.QuitEventName);
                _quitWait = ThreadPool.RegisterWaitForSingleObject(quitEvent, OnQuitSignal, null, Timeout.Infinite, false);
            }
            catch (Exception ex)
            {
                Logger.Error("创建退出通知事件失败", ex);
            }
            _quitEvent = quitEvent;
        }

        private bool SafeIsAutostartEnabled()
        {
            try
            {
                return AutostartManager.IsEnabled();
            }
            catch
            {
                return false;
            }
        }

        // ---------------- 菜单 ----------------

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _statusItem.Text = _hookInstalled ? "正在监听 Ctrl+C" : "键盘监听不可用";
                _autostartItem.Checked = SafeIsAutostartEnabled();
                Logger.Info(string.Format("MENU open=1 autostart={0}", _autostartItem.Checked ? 1 : 0));
            }
            catch (Exception ex)
            {
                Logger.Error("刷新托盘菜单失败", ex);
            }
        }

        private void OnAutostartItemClick(object sender, EventArgs e)
        {
            bool want = _autostartItem.Checked;
            bool ok;
            try
            {
                ok = want ? AutostartManager.Enable() : AutostartManager.Disable();
            }
            catch (Exception ex)
            {
                Logger.Error("修改开机自启动设置异常", ex);
                ok = false;
            }

            Logger.Info(string.Format("MENU action=autostart value={0} ok={1}", want ? 1 : 0, ok ? 1 : 0));
            if (!ok)
            {
                _autostartItem.Checked = !want;
                _tray.ShowBalloonTip(4000, "复制反馈工具",
                    "无法修改开机自动启动设置（可能被系统策略限制）。", ToolTipIcon.Warning);
            }
        }

        private void OnQuitItemClick(object sender, EventArgs e)
        {
            // 延迟到菜单点击事件返回后再做清理，避免在菜单项 Click 处理过程中销毁菜单
            Logger.Info("MENU action=quit");
            _pendingQuitReason = "tray-menu";
            _window.PostQuit();
        }

        // ---------------- 全局复制触发 ----------------

        private void OnKeyTriggered(KeyTrigger trigger)
        {
            // 钩子回调只做入队 + 投递消息，绝不在回调里做耗时操作
            _pendingTriggers.Enqueue(trigger);
            _window.PostTrigger();
        }

        private void HandlePendingTriggers()
        {
            while (_pendingTriggers.Count > 0)
            {
                KeyTrigger trigger = _pendingTriggers.Dequeue();
                _detector.Trigger(trigger);
            }
        }

        // ---------------- 退出 ----------------

        private void OnQuitSignal(object state, bool timedOut)
        {
            try
            {
                _window.PostQuit();
            }
            catch (Exception ex)
            {
                Logger.Error("处理外部退出通知失败", ex);
            }
        }

        private void HandleQuitRequest()
        {
            RequestExit(string.IsNullOrEmpty(_pendingQuitReason) ? "quit-signal" : _pendingQuitReason);
        }

        private void RequestExit(string reason)
        {
            if (_cleaned) return;
            Logger.Info("EXIT reason=" + reason);
            Cleanup();
            try
            {
                ExitThread();
            }
            catch (Exception ex)
            {
                Logger.Error("退出消息循环失败", ex);
            }
        }

        private void Cleanup()
        {
            if (_cleaned) return;
            _cleaned = true;

            try
            {
                if (_hook != null) _hook.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error("释放键盘钩子失败", ex);
            }
            try
            {
                if (_detector != null) _detector.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error("释放复制检测器失败", ex);
            }
            try
            {
                if (_toast != null) _toast.HideForExit();
            }
            catch
            {
            }
            try
            {
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
                if (_trayIcon != null) _trayIcon.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error("释放托盘图标失败", ex);
            }
            try
            {
                if (_quitWait != null) _quitWait.Unregister(null);
                if (_quitEvent != null) _quitEvent.Close();
            }
            catch (Exception ex)
            {
                Logger.Error("释放退出通知事件失败", ex);
            }
            try
            {
                if (_menu != null) _menu.Dispose();
                if (_headerFont != null) _headerFont.Dispose();
                if (_statusFont != null) _statusFont.Dispose();
            }
            catch
            {
            }
            try
            {
                if (_toast != null) _toast.Dispose();
            }
            catch
            {
            }
            try
            {
                if (_window != null) _window.Dispose();
            }
            catch
            {
            }

            Logger.Info("SHUTDOWN hook=0 tray=0");
            Logger.Shutdown();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Cleanup();
            }
            base.Dispose(disposing);
        }
    }
}
