using System;
using System.Windows.Forms;

namespace CopyFeedback
{
    /// <summary>
    /// 复制检测状态机。
    ///
    /// 关键设计：
    ///  - 用 GetClipboardSequenceNumber() 判断剪贴板是否被“写入”过。即使连续复制完全相同的内容，
    ///    序列号也会递增，因此不会漏掉重复复制；
    ///  - 触发后最多等待 1200ms 观察序列号变化，覆盖剪贴板更新延迟的情况；
    ///  - 观察到变化后读取剪贴板，若被其它程序占用则每隔约 25ms 重试，最多约 350ms；
    ///  - 如果超时仍没有变化，则不弹提示（宁可漏报，不误报）。
    /// </summary>
    internal sealed class CopyDetectionService : IDisposable
    {
        private const int TimeoutMs = 1200;
        private const int PollIntervalMs = 25;
        private const int MaxReadAttempts = 14; // 约 350ms

        private enum State
        {
            Idle,
            WaitChange,
            ReadRetry
        }

        private readonly Timer _timer;
        private readonly Action<CopyInfo> _onDetected;
        private State _state = State.Idle;
        private long _seq0;
        private DateTime _startTime;
        private DateTime _deadline;
        private int _readAttempts;

        public CopyDetectionService(Action<CopyInfo> onDetected)
        {
            _onDetected = onDetected;
            _timer = new Timer();
            _timer.Interval = PollIntervalMs;
            _timer.Tick += OnTick;
        }

        public void Trigger(KeyTrigger trigger)
        {
            try
            {
                // 优先使用钩子回调瞬间采样的序列号（见 KeyboardHook），避免竞态漏报
                long seqAtHook = trigger == null ? -1 : trigger.SeqAtHook;
                _seq0 = seqAtHook >= 0 ? seqAtHook : NativeMethods.GetClipboardSequenceNumber();
                _startTime = DateTime.UtcNow;
                _deadline = _startTime.AddMilliseconds(TimeoutMs);
                _readAttempts = 0;
                _state = State.WaitChange;
                if (!_timer.Enabled) _timer.Start();

                Logger.Debug(string.Format(
                    "TRIGGER vk={0} ctrl={1} shift={2} alt={3} repeat=0 seq0={4}",
                    trigger == null ? 0 : trigger.Vk,
                    trigger != null && trigger.Ctrl ? 1 : 0,
                    trigger != null && trigger.Shift ? 1 : 0,
                    trigger != null && trigger.Alt ? 1 : 0,
                    _seq0));
            }
            catch (Exception ex)
            {
                Logger.Error("启动复制检测失败", ex);
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                if (_state == State.Idle)
                {
                    _timer.Stop();
                    return;
                }

                DateTime now = DateTime.UtcNow;
                if (_state == State.WaitChange)
                {
                    long seq = NativeMethods.GetClipboardSequenceNumber();
                    if (seq != _seq0)
                    {
                        Logger.Debug(string.Format(
                            "CLIP_CHANGE seq={0} elapsed_ms={1}",
                            seq,
                            (int)(now - _startTime).TotalMilliseconds));
                        _state = State.ReadRetry;
                        _readAttempts = 0;
                        TryReadOnce(now);
                    }
                    else if (now >= _deadline)
                    {
                        Fail("timeout");
                    }
                }
                else if (_state == State.ReadRetry)
                {
                    TryReadOnce(now);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("复制检测轮询异常", ex);
                Reset();
            }
        }

        private void TryReadOnce(DateTime now)
        {
            _readAttempts++;
            ClipboardReadResult result = ClipboardReader.TryRead();

            if (result.ReadOk && result.HasContent)
            {
                Logger.Debug(string.Format(
                    "CLIP_READ ok=1 kind={0} chars={1} shown={2} truncated={3} attempts={4}",
                    KindName(result.Info.Kind),
                    result.Info.RawChars,
                    result.Info.ShownChars,
                    result.Info.Truncated ? 1 : 0,
                    _readAttempts));
                Reset();
                try
                {
                    if (_onDetected != null) _onDetected(result.Info);
                }
                catch (Exception ex)
                {
                    Logger.Error("处理复制结果失败", ex);
                }
                return;
            }

            if (_readAttempts >= MaxReadAttempts || now >= _deadline)
            {
                Logger.Debug(string.Format(
                    "CLIP_READ ok=0 attempts={0} error=\"{1}\"",
                    _readAttempts,
                    result.Error == null ? "无法读取剪贴板" : result.Error.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ")));
                Reset();
            }
        }

        private void Fail(string reason)
        {
            Logger.Debug(string.Format(
                "DETECT timeout=1 elapsed_ms={0} reason={1}",
                (int)(DateTime.UtcNow - _startTime).TotalMilliseconds,
                reason));
            Reset();
        }

        private void Reset()
        {
            _state = State.Idle;
            _timer.Stop();
        }

        private static string KindName(CopyKind kind)
        {
            switch (kind)
            {
                case CopyKind.Text: return "text";
                case CopyKind.Image: return "image";
                case CopyKind.Files: return "files";
                default: return "other";
            }
        }

        public void Dispose()
        {
            try
            {
                _timer.Stop();
                _timer.Dispose();
            }
            catch
            {
            }
        }
    }
}
