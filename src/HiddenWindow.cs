using System;
using System.Windows.Forms;

namespace CopyFeedback
{
    /// <summary>
    /// 隐藏的顶层窗口：用于接收钩子线程/线程池投递过来的自定义消息（复制触发、退出请求、系统注销）。
    /// 该窗口不显示、不在任务栏、不在 Alt+Tab 中。
    /// </summary>
    internal sealed class HiddenWindow : NativeWindow, IDisposable
    {
        private readonly Action _onTrigger;
        private readonly Action _onQuit;

        public HiddenWindow(Action onTrigger, Action onQuit)
        {
            _onTrigger = onTrigger;
            _onQuit = onQuit;

            CreateHandle(new CreateParams
            {
                Caption = "CopyFeedbackHiddenWindow",
                Style = 0,
                ExStyle = NativeMethods.WS_EX_TOOLWINDOW,
                X = 0,
                Y = 0,
                Width = 0,
                Height = 0,
                Parent = IntPtr.Zero
            });
        }

        public void PostTrigger()
        {
            if (Handle != IntPtr.Zero)
            {
                NativeMethods.PostMessage(Handle, NativeMethods.WM_APP_TRIGGER, IntPtr.Zero, IntPtr.Zero);
            }
        }

        public void PostQuit()
        {
            if (Handle != IntPtr.Zero)
            {
                NativeMethods.PostMessage(Handle, NativeMethods.WM_APP_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_APP_TRIGGER)
            {
                try
                {
                    if (_onTrigger != null) _onTrigger();
                }
                catch (Exception ex)
                {
                    Logger.Error("处理复制触发消息失败", ex);
                }
                return;
            }
            if (m.Msg == NativeMethods.WM_APP_QUIT)
            {
                try
                {
                    if (_onQuit != null) _onQuit();
                }
                catch (Exception ex)
                {
                    Logger.Error("处理退出消息失败", ex);
                }
                return;
            }
            if (m.Msg == NativeMethods.WM_QUERYENDSESSION)
            {
                m.Result = new IntPtr(1);
                return;
            }
            if (m.Msg == NativeMethods.WM_ENDSESSION)
            {
                try
                {
                    if (_onQuit != null) _onQuit();
                }
                catch
                {
                }
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            try
            {
                if (Handle != IntPtr.Zero)
                {
                    DestroyHandle();
                }
            }
            catch
            {
            }
        }
    }
}
