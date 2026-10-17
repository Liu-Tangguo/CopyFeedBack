using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CopyFeedback
{
    /// <summary>一次复制快捷键触发（由钩子线程捕获，携带回调瞬间的剪贴板序列号）。</summary>
    internal sealed class KeyTrigger
    {
        public int Vk;
        public bool Ctrl;
        public bool Shift;
        public bool Alt;
        /// <summary>钩子回调瞬间读取的剪贴板序列号；-1 表示未采样。
        /// 在钩子回调内采样可避免“目标程序已先写入剪贴板、我们消息后到”导致的漏报竞态。</summary>
        public long SeqAtHook = -1;
    }

    /// <summary>
    /// 全局低级键盘钩子（WH_KEYBOARD_LL）。
    /// 只观察 Ctrl+C / Ctrl+Shift+C / Ctrl+Insert，永远调用 CallNextHookEx，
    /// 不吞掉、不修改、不延迟原有按键，因此不会影响应用本身的复制行为。
    /// </summary>
    internal sealed class KeyboardHook : IDisposable
    {
        private readonly NativeMethods.LowLevelKeyboardProc _proc; // 必须保持引用，防止被 GC 回收导致回调崩溃
        private IntPtr _hook = IntPtr.Zero;
        private readonly bool[] _keyDown = new bool[256];

        public event Action<KeyTrigger> Triggered;

        public KeyboardHook()
        {
            _proc = HookCallback;
        }

        public bool Install()
        {
            try
            {
                _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
                if (_hook == IntPtr.Zero)
                {
                    Logger.Error("安装全局键盘钩子失败，Win32 错误码 " + Marshal.GetLastWin32Error());
                    return false;
                }
                ResetKeyState();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("安装全局键盘钩子异常", ex);
                return false;
            }
        }

        private void ResetKeyState()
        {
            for (int i = 0; i < _keyDown.Length; i++) _keyDown[i] = false;
        }

        private bool IsKeyDown(int vk)
        {
            return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int message = wParam.ToInt32();
                    bool isDownMessage = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;
                    bool isUpMessage = message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP;

                    if (isDownMessage || isUpMessage)
                    {
                        NativeMethods.KBDLLHOOKSTRUCT data =
                            (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                        int vk = (int)data.vkCode;
                        if (vk >= 0 && vk < 256)
                        {
                            if (isDownMessage)
                            {
                                bool repeat = _keyDown[vk];
                                _keyDown[vk] = true;
                                if (!repeat)
                                {
                                    bool ctrl = IsKeyDown(NativeMethods.VK_CONTROL);
                                    bool shift = IsKeyDown(NativeMethods.VK_SHIFT);
                                    bool alt = IsKeyDown(NativeMethods.VK_MENU);
                                    bool copyCombo = false;
                                    if (!alt && ctrl)
                                    {
                                        if (vk == NativeMethods.VK_C) copyCombo = true; // Ctrl+C 与 Ctrl+Shift+C
                                        else if (vk == NativeMethods.VK_INSERT && !shift) copyCombo = true; // Ctrl+Insert
                                    }
                                    if (copyCombo)
                                    {
                                        Action<KeyTrigger> handler = Triggered;
                                        if (handler != null)
                                        {
                                            // 在钩子回调里采样序列号（极轻量），作为检测基线，
                                            // 避免目标应用先写剪贴板而导致后续消息处理时基线已过期。
                                            handler(new KeyTrigger
                                            {
                                                Vk = vk,
                                                Ctrl = ctrl,
                                                Shift = shift,
                                                Alt = alt,
                                                SeqAtHook = NativeMethods.GetClipboardSequenceNumber()
                                            });
                                        }
                                    }
                                }
                            }
                            else
                            {
                                _keyDown[vk] = false;
                            }
                        }
                    }
                }
            }
            catch
            {
                // 钩子回调绝不能抛异常，否则可能影响输入
            }
            // 永远放行：“不吞掉、不阻止、不改变”原有的 Ctrl+C
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            try
            {
                if (_hook != IntPtr.Zero)
                {
                    NativeMethods.UnhookWindowsHookEx(_hook);
                    _hook = IntPtr.Zero;
                }
                Triggered = null;
            }
            catch (Exception ex)
            {
                Logger.Error("卸载全局键盘钩子失败", ex);
            }
        }
    }
}
