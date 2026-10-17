using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CopyFeedback
{
    /// <summary>
    /// “已复制”提示弹窗：
    ///  - 无边框、圆角、跟随鼠标附近出现、不超出屏幕工作区；
    ///  - TopMost + WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW + WS_EX_TRANSPARENT，
    ///    不抢输入焦点、鼠标可穿透，不影响用户当前操作；
    ///  - 1.5 秒后自动淡出；连续复制时只刷新同一个窗口，不堆叠。
    /// </summary>
    internal sealed class ToastForm : Form
    {
        // 以下尺寸均为 96 DPI 下的设计值，运行时按 DeviceDpi 缩放
        private const int DesignWidth = 300;
        private const int DesignHeight = 64;
        private const int DesignRadius = 12;
        private const int DesignIconX = 14;
        private const int DesignIconY = 14;
        private const int DesignIconSize = 18;
        private const int DesignHeaderX = 40;
        private const int DesignHeaderY = 9;
        private const int DesignPreviewY = 35;
        private const int DesignPadding = 14;
        private const int ShowDurationMs = 1500;

        private readonly Color _bg;
        private readonly Color _border;
        private readonly Color _fg;
        private readonly Color _secondary;
        private readonly Color _accent;
        private readonly Color _check;
        private readonly Font _headerFont;
        private readonly Font _previewFont;
        private readonly Timer _hideTimer;
        private readonly Timer _fadeTimer;

        private string _displayPreview = string.Empty;
        private int _fadeStep;
        private bool _activatedSeen;
        private Region _oldRegion;

        public ToastForm()
        {
            bool dark = DetectDarkTheme();
            if (dark)
            {
                _bg = Color.FromArgb(32, 32, 32);
                _border = Color.FromArgb(58, 58, 58);
                _fg = Color.FromArgb(245, 245, 245);
                _secondary = Color.FromArgb(176, 176, 176);
                _accent = Color.FromArgb(46, 164, 120);
                _check = Color.White;
            }
            else
            {
                _bg = Color.FromArgb(255, 255, 255);
                _border = Color.FromArgb(224, 224, 224);
                _fg = Color.FromArgb(26, 26, 26);
                _secondary = Color.FromArgb(110, 110, 110);
                _accent = Color.FromArgb(15, 123, 65);
                _check = Color.White;
            }

            _headerFont = new Font("Segoe UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
            _previewFont = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            MinimizeBox = false;
            MaximizeBox = false;
            Text = "CopyFeedbackToast";

            _hideTimer = new Timer();
            _hideTimer.Interval = ShowDurationMs;
            _hideTimer.Tick += OnHideTimerTick;

            _fadeTimer = new Timer();
            _fadeTimer.Interval = 25;
            _fadeTimer.Tick += OnFadeTimerTick;

            Activated += OnActivated;

            // 提前创建句柄，确保 DeviceDpi 可用、缩放计算正确（此时窗口不可见）
            IntPtr unused = Handle;
            GC.KeepAlive(unused);
        }

        private static bool DetectDarkTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("AppsUseLightTheme");
                        if (value is int)
                        {
                            return (int)value == 0;
                        }
                    }
                }
            }
            catch
            {
                // 读取失败按浅色处理
            }
            return false;
        }

        private float LayoutScale
        {
            get
            {
                float dpi = 96f;
                try
                {
                    dpi = DeviceDpi;
                }
                catch
                {
                }
                if (dpi <= 0f) dpi = 96f;
                return dpi / 96f;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE
                    | NativeMethods.WS_EX_TOOLWINDOW
                    | NativeMethods.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            // 返回 HTTRANSPARENT，让鼠标点击/命中测试穿透到下层窗口（比仅依赖 WS_EX_TRANSPARENT 更可靠），
            // 保证提示弹窗完全不影响用户当前操作。
            if (m.Msg == NativeMethods.WM_NCHITTEST)
            {
                m.Result = new IntPtr(NativeMethods.HTTRANSPARENT);
                return;
            }
            base.WndProc(ref m);
        }

        private void OnActivated(object sender, EventArgs e)
        {
            // 注意：在 ShowWithoutActivation + WS_EX_NOACTIVATE 下，WinForms 仍可能触发
            // 伪 Activated 事件（它把窗体设为本线程 active），但前台窗口/输入焦点并未改变。
            // 只有“事件触发瞬间前台窗口就是弹窗本身”才算真的抢焦点（缺陷）。
            if (!Visible) return;
            if (NativeMethods.GetForegroundWindow() == Handle)
            {
                _activatedSeen = true;
                Logger.Debug("POPUP activated=1");
            }
            else
            {
                Logger.Debug("POPUP active-signal=spurious");
            }
        }

        /// <summary>显示一次“已复制”提示；如果已经在显示则原地刷新并重置计时。</summary>
        public void ShowCopy(CopyInfo info)
        {
            try
            {
                _displayPreview = info == null ? string.Empty : (info.PreviewText ?? string.Empty);

                if (!Visible)
                {
                    float scale = LayoutScale;
                    Size size = new Size(
                        (int)Math.Round(DesignWidth * scale),
                        (int)Math.Round(DesignHeight * scale));
                    Size = size;
                    UpdateRegion(scale);

                    Point anchor = Cursor.Position;
                    Point location = ComputePosition(size, anchor, scale);
                    Location = location;

                    Opacity = 1.0;
                    _fadeTimer.Stop();
                    _fadeStep = 0;

                    IntPtr fgBefore = NativeMethods.GetForegroundWindow();
                    _activatedSeen = false;
                    Show();
                    NativeMethods.SetWindowPos(
                        Handle,
                        NativeMethods.HWND_TOPMOST,
                        location.X,
                        location.Y,
                        size.Width,
                        size.Height,
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
                    IntPtr fgAfter = NativeMethods.GetForegroundWindow();
                    bool stoleFocus = fgAfter == Handle || _activatedSeen;

                    Logger.Debug(string.Format(
                        "POPUP show=1 x={0} y={1} w={2} h={3} fg_before=0x{4:X} fg_after=0x{5:X} activated={6}",
                        location.X, location.Y, size.Width, size.Height,
                        fgBefore.ToInt64(), fgAfter.ToInt64(),
                        stoleFocus ? 1 : 0));
                }
                else
                {
                    _fadeTimer.Stop();
                    _fadeStep = 0;
                    Opacity = 1.0;
                    Invalidate();
                    // 刷新时重申置顶且不激活，避免被后出现的置顶窗口遮挡
                    NativeMethods.SetWindowPos(
                        Handle,
                        NativeMethods.HWND_TOPMOST,
                        Location.X,
                        Location.Y,
                        Width,
                        Height,
                        NativeMethods.SWP_NOACTIVATE);
                    Logger.Debug(string.Format("POPUP refresh=1 chars={0}", _displayPreview.Length));
                }

                _hideTimer.Stop();
                _hideTimer.Start();
            }
            catch (Exception ex)
            {
                Logger.Error("显示复制提示失败", ex);
            }
        }

        private void OnHideTimerTick(object sender, EventArgs e)
        {
            _hideTimer.Stop();
            Logger.Debug("POPUP hide=1 reason=timeout");
            _fadeStep = 0;
            _fadeTimer.Start();
        }

        private void OnFadeTimerTick(object sender, EventArgs e)
        {
            _fadeStep++;
            double[] opacities = new double[] { 0.82, 0.62, 0.42, 0.24, 0.10 };
            if (_fadeStep >= opacities.Length)
            {
                _fadeTimer.Stop();
                Hide();
                Opacity = 1.0;
                _fadeStep = 0;
                return;
            }
            try
            {
                Opacity = opacities[_fadeStep];
            }
            catch
            {
                _fadeTimer.Stop();
                Hide();
            }
        }

        public void HideForExit()
        {
            try
            {
                _hideTimer.Stop();
                _fadeTimer.Stop();
                if (Visible) Hide();
            }
            catch
            {
            }
        }

        private void UpdateRegion(float scale)
        {
            try
            {
                int radius = (int)Math.Round(DesignRadius * scale);
                using (GraphicsPath path = CreateRoundedRect(
                    new Rectangle(0, 0, Width, Height), radius))
                {
                    Region region = new Region(path);
                    if (_oldRegion != null) _oldRegion.Dispose();
                    _oldRegion = region;
                    Region = region;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("设置弹窗圆角区域失败", ex);
            }
        }

        private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private Point ComputePosition(Size size, Point cursor, float scale)
        {
            Rectangle workArea = Screen.FromPoint(cursor).WorkingArea;
            int offsetX = (int)Math.Round(16 * scale);
            int offsetY = (int)Math.Round(20 * scale);

            int x = cursor.X + offsetX;
            int y = cursor.Y + offsetY;
            if (x + size.Width > workArea.Right) x = cursor.X - size.Width - offsetX;
            if (y + size.Height > workArea.Bottom) y = cursor.Y - size.Height - offsetY;
            if (x < workArea.Left) x = workArea.Left;
            if (y < workArea.Top) y = workArea.Top;
            if (x + size.Width > workArea.Right) x = workArea.Right - size.Width;
            if (y + size.Height > workArea.Bottom) y = workArea.Bottom - size.Height;
            return new Point(x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            try
            {
                PaintCard(e.Graphics);
            }
            catch (Exception ex)
            {
                Logger.Error("绘制复制提示失败", ex);
            }
        }

        private void PaintCard(Graphics g)
        {
            float scale = LayoutScale;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush bgBrush = new SolidBrush(_bg))
            {
                g.FillRectangle(bgBrush, ClientRectangle);
            }

            float inset = 0.5f * scale;
            using (GraphicsPath borderPath = CreateRoundedRect(
                Rectangle.Round(new RectangleF(inset, inset, Width - inset * 2, Height - inset * 2)),
                (int)Math.Round(DesignRadius * scale)))
            using (Pen borderPen = new Pen(_border, Math.Max(1f, scale)))
            {
                g.DrawPath(borderPen, borderPath);
            }

            float centerX = (DesignIconX + DesignIconSize / 2f) * scale;
            float centerY = (DesignIconY + DesignIconSize / 2f) * scale;
            float radius = DesignIconSize / 2f * scale;
            using (SolidBrush accentBrush = new SolidBrush(_accent))
            {
                g.FillEllipse(accentBrush, centerX - radius, centerY - radius, radius * 2f, radius * 2f);
            }
            using (Pen checkPen = new Pen(_check, Math.Max(1.6f, 2.0f * scale)))
            {
                checkPen.StartCap = LineCap.Round;
                checkPen.EndCap = LineCap.Round;
                checkPen.LineJoin = LineJoin.Round;
                g.DrawLine(checkPen, centerX - 4f * scale, centerY + 0.4f * scale, centerX - 1.2f * scale, centerY + 3.2f * scale);
                g.DrawLine(checkPen, centerX - 1.2f * scale, centerY + 3.2f * scale, centerX + 4.4f * scale, centerY - 3.4f * scale);
            }

            Rectangle headerRect = new Rectangle(
                (int)Math.Round(DesignHeaderX * scale),
                (int)Math.Round(DesignHeaderY * scale),
                Math.Max(10, Width - (int)Math.Round((DesignHeaderX + DesignPadding) * scale)),
                (int)Math.Round(20 * scale));
            TextRenderer.DrawText(g, "已复制", _headerFont, headerRect, _fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPadding);

            int previewWidth = Math.Max(10, Width - (int)Math.Round(DesignPadding * 2 * scale));
            string shown;
            bool truncated;
            FitText(_displayPreview, _previewFont, previewWidth, out shown, out truncated);
            Rectangle previewRect = new Rectangle(
                (int)Math.Round(DesignPadding * scale),
                (int)Math.Round(DesignPreviewY * scale),
                previewWidth,
                (int)Math.Round(20 * scale));
            TextRenderer.DrawText(g, shown, _previewFont, previewRect, _secondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private static void FitText(string text, Font font, int maxWidth, out string shown, out bool truncated)
        {
            shown = string.Empty;
            truncated = false;
            if (string.IsNullOrEmpty(text)) return;

            Size full = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (full.Width <= maxWidth)
            {
                shown = text;
                return;
            }

            truncated = true;
            const string ellipsis = "\u2026";
            int low = 0;
            int high = text.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                string candidate = text.Substring(0, mid) + ellipsis;
                Size size = TextRenderer.MeasureText(candidate, font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                if (size.Width <= maxWidth) low = mid;
                else high = mid - 1;
            }
            if (low <= 0)
            {
                shown = ellipsis;
                return;
            }
            // 避免把代理对（如 emoji）从中间截断
            if (low < text.Length && char.IsLowSurrogate(text[low]) && low > 0 && char.IsHighSurrogate(text[low - 1]))
            {
                low--;
            }
            shown = text.Substring(0, low) + ellipsis;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _hideTimer.Dispose();
                    _fadeTimer.Dispose();
                    _headerFont.Dispose();
                    _previewFont.Dispose();
                    if (_oldRegion != null)
                    {
                        _oldRegion.Dispose();
                        _oldRegion = null;
                    }
                }
                catch
                {
                }
            }
            base.Dispose(disposing);
        }
    }
}
