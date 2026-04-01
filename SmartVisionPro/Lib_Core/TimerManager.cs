using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Timers;

namespace Core
{
    // Base timer: polymorphic timer base class
    public abstract class TimerBase : IDisposable
    {
        private readonly object _lock = new object();

        public string Name { get; }
        public TimeSpan Interval { get; protected set; }
        // Lower value means higher priority.
        public int Priority { get; protected set; }
        public bool IsRunning { get; protected set; }

        // Elapsed event: consumer can subscribe
        public event Action<TimerBase> Elapsed;

        protected TimerBase(string name, TimeSpan interval, int priority = 0)
        {
            Name = name ?? string.Empty;
            Interval = interval;
            Priority = priority;
            IsRunning = false;
        }

        // Start and Stop must be implemented by derived types
        public abstract void Start();
        public abstract void Stop();

        protected void OnElapsed()
        {
            try
            {
                Elapsed?.Invoke(this);
            }
            catch (Exception ex)
            {
                try { LogManager.Inst.Write($"Timer '{Name}' Elapsed handler exception: {ex}"); } catch { }
            }
        }

        public virtual void Dispose()
        {
            try { Stop(); } catch { }
        }
    }

    // LoopTimer: periodic timer using System.Threading.Timer (thread pool)
    public class LoopTimer : TimerBase
    {
        private TimerCallback _callback;
        private System.Threading.Timer _timer;
        private readonly object _lock = new object();

        public LoopTimer(string name, TimeSpan interval, int priority = 0) : base(name, interval, priority)
        {
            _callback = state => OnElapsed();
        }

        public override void Start()
        {
            lock (_lock)
            {
                if (IsRunning) return;
                var ms = (int)Interval.TotalMilliseconds;
                if (ms < 1) ms = 1;
                _timer = new System.Threading.Timer(_callback, null, ms, ms);
                IsRunning = true;
            }
        }

        public override void Stop()
        {
            lock (_lock)
            {
                if (!IsRunning) return;
                try { _timer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
                try { _timer?.Dispose(); } catch { }
                _timer = null;
                IsRunning = false;
            }
        }
    }

    // PrecisionTimer: uses System.Timers.Timer for better accuracy and AutoReset
    public class PrecisionTimer : TimerBase
    {
        private System.Timers.Timer _timer;
        private readonly object _lock = new object();

        public PrecisionTimer(string name, TimeSpan interval, int priority = 0) : base(name, interval, priority)
        {
        }

        public override void Start()
        {
            lock (_lock)
            {
                if (IsRunning) return;
                _timer = new System.Timers.Timer(Math.Max(1, Interval.TotalMilliseconds)) { AutoReset = true };
                _timer.Elapsed += Timer_Elapsed;
                _timer.Start();
                IsRunning = true;
            }
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            OnElapsed();
        }

        public override void Stop()
        {
            lock (_lock)
            {
                if (!IsRunning) return;
                try { _timer?.Stop(); } catch { }
                if (_timer != null)
                {
                    _timer.Elapsed -= Timer_Elapsed;
                    try { _timer.Dispose(); } catch { }
                    _timer = null;
                }
                IsRunning = false;
            }
        }
    }

    // HighSpeedTimer: dedicated thread with high frequency loop. Use with caution (CPU usage).
    public class HighSpeedTimer : TimerBase
    {
        private Thread _thread;
        private CancellationTokenSource _cts;
        private readonly object _lock = new object();
        private readonly int _spinThresholdMs;
        private readonly int _maxSleepMs;

        public HighSpeedTimer(string name, TimeSpan interval, int priority = 0, int spinThresholdMs = 2, int maxSleepMs = 10) : base(name, interval, priority)
        {
            _spinThresholdMs = Math.Max(0, spinThresholdMs);
            _maxSleepMs = Math.Max(1, maxSleepMs);
        }

        public override void Start()
        {
            lock (_lock)
            {
                if (IsRunning) return;
                _cts = new CancellationTokenSource();
                _thread = new Thread(() => RunLoop(_cts.Token)) { IsBackground = true, Name = $"HS-Timer-{Name}" };
                _thread.Start();
                IsRunning = true;
            }
        }

        // Hybrid spin/sleep: busy-wait for short durations, otherwise Thread.Sleep to reduce CPU.
        private void RunLoop(CancellationToken ct)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                long nextTick = sw.ElapsedMilliseconds;
                var intervalMs = Math.Max(1, (long)Interval.TotalMilliseconds);
                // Parameters for hybrid strategy: use instance-configured threshold and maxSleep
                var spinThresholdMs = _spinThresholdMs;
                var maxSleepMs = _maxSleepMs;
                while (!ct.IsCancellationRequested)
                {
                    nextTick += intervalMs;
                    while (true)
                    {
                        if (ct.IsCancellationRequested) break;
                        var remaining = nextTick - sw.ElapsedMilliseconds;
                        if (remaining <= 0) break;
                        if (remaining > spinThresholdMs)
                        {
                            // Sleep for a short fraction to yield CPU
                            // Sleep for remaining - spinThresholdMs, but cap to avoid long sleeps
                            var sleepMs = (int)Math.Max(1, Math.Min(remaining - spinThresholdMs, maxSleepMs));
                            Thread.Sleep(sleepMs);
                        }
                        else
                        {
                            // short spin-wait for precise timing
                            Thread.SpinWait(20);
                        }
                    }

                    if (ct.IsCancellationRequested) break;
                    OnElapsed();
                }
            }
            catch (Exception ex)
            {
                try { LogManager.Inst.Write($"HighSpeedTimer '{Name}' exception: {ex}"); } catch { }
            }
        }

        public override void Stop()
        {
            lock (_lock)
            {
                if (!IsRunning) return;
                try { _cts?.Cancel(); } catch { }
                try { if (!_thread.Join(500)) _thread?.Abort(); } catch { }
                try { _cts?.Dispose(); } catch { }
                _cts = null;
                _thread = null;
                IsRunning = false;
            }
        }
    }

    // One-shot OnTimer: fires once after interval then stops
    public class OnTimer : TimerBase
    {
        private System.Threading.Timer _timer;
        private readonly object _lock = new object();

    public OnTimer(string name, TimeSpan interval, int priority = 0) : base(name, interval, priority) { }

        public override void Start()
        {
            lock (_lock)
            {
                if (IsRunning) return;
                var ms = (int)Interval.TotalMilliseconds;
                if (ms < 1) ms = 1;
                _timer = new System.Threading.Timer(state =>
                {
                    try { OnElapsed(); } finally { Stop(); }
                }, null, ms, Timeout.Infinite);
                IsRunning = true;
            }
        }

        public override void Stop()
        {
            lock (_lock)
            {
                if (!IsRunning) return;
                try { _timer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
                try { _timer?.Dispose(); } catch { }
                _timer = null;
                IsRunning = false;
            }
        }
    }

    // One-shot OffTimer: semantic difference can be captured by name/handler (fires once)
    public class OffTimer : TimerBase
    {
        private System.Threading.Timer _timer;
        private readonly object _lock = new object();

    public OffTimer(string name, TimeSpan interval, int priority = 0) : base(name, interval, priority) { }

        public override void Start()
        {
            lock (_lock)
            {
                if (IsRunning) return;
                var ms = (int)Interval.TotalMilliseconds;
                if (ms < 1) ms = 1;
                _timer = new System.Threading.Timer(state =>
                {
                    try { OnElapsed(); } finally { Stop(); }
                }, null, ms, Timeout.Infinite);
                IsRunning = true;
            }
        }

        public override void Stop()
        {
            lock (_lock)
            {
                if (!IsRunning) return;
                try { _timer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
                try { _timer?.Dispose(); } catch { }
                _timer = null;
                IsRunning = false;
            }
        }
    }

    // TimerManager: create/register/manage timers thread-safely
    [Manager(DependsOn = new[] { typeof(LogManager) }, Order = 40)]
    public class TimerManager : CSingleton<TimerManager>
    {
        private readonly ConcurrentDictionary<string, TimerBase> _timers = new ConcurrentDictionary<string, TimerBase>(StringComparer.OrdinalIgnoreCase);
        private bool _initialized = false;
        private readonly object _lock = new object();

        // HighSpeedTimer hybrid strategy defaults
        private int _hsSpinThresholdMs = 2;
        private int _hsMaxSleepMs = 10;

        public bool IsInitialized => _initialized;

        public override void Initialize()
        {
            lock (_lock)
            {
                if (_initialized) return;
                // nothing special to init now
                _initialized = true;
            }
        }

        // Configure global defaults for HighSpeedTimer hybrid spin/sleep behavior
        public void SetHighSpeedDefaults(int spinThresholdMs, int maxSleepMs)
        {
            if (spinThresholdMs < 0) throw new ArgumentOutOfRangeException(nameof(spinThresholdMs));
            if (maxSleepMs < 1) throw new ArgumentOutOfRangeException(nameof(maxSleepMs));
            lock (_lock)
            {
                _hsSpinThresholdMs = spinThresholdMs;
                _hsMaxSleepMs = maxSleepMs;
            }
        }

        public Tuple<int, int> GetHighSpeedDefaults()
        {
            lock (_lock)
            {
                return Tuple.Create(_hsSpinThresholdMs, _hsMaxSleepMs);
            }
        }

        public override void Shutdown()
        {
            lock (_lock)
            {
                if (!_initialized) return;

                // Stop timers in order: higher numeric priority first (so lower priority value = higher priority stops last)
                try
                {
                    var timers = new List<TimerBase>(_timers.Values);
                    timers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                    foreach (var t in timers)
                    {
                        try { t.Stop(); t.Dispose(); } catch { }
                    }
                }
                catch { }

                _timers.Clear();
                _initialized = false;
            }
        }

        // Register an existing timer
        public bool Register(string name, TimerBase timer)
        {
            if (timer == null) throw new ArgumentNullException(nameof(timer));
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (!_initialized) Initialize();
            if (_timers.TryAdd(name, timer)) return true;
            return false;
        }

        public bool Unregister(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (_timers.TryRemove(name, out var t))
            {
                try { t.Stop(); t.Dispose(); } catch { }
                return true;
            }
            return false;
        }

        public TimerBase CreateLoopTimer(string name, TimeSpan interval, Action<TimerBase> handler = null, int priority = 0)
        {
            var t = new LoopTimer(name, interval, priority);
            if (handler != null) t.Elapsed += handler;
            if (_timers.TryAdd(name, t)) return t;
            throw new InvalidOperationException($"Timer with name '{name}' already exists.");
        }

        public TimerBase CreatePrecisionTimer(string name, TimeSpan interval, Action<TimerBase> handler = null, int priority = 0)
        {
            var t = new PrecisionTimer(name, interval, priority);
            if (handler != null) t.Elapsed += handler;
            if (_timers.TryAdd(name, t)) return t;
            throw new InvalidOperationException($"Timer with name '{name}' already exists.");
        }

        public TimerBase CreateHighSpeedTimer(string name, TimeSpan interval, Action<TimerBase> handler = null, int priority = 0, int? spinThresholdMs = null, int? maxSleepMs = null)
        {
            int actualSpin, actualMaxSleep;
            lock (_lock)
            {
                actualSpin = spinThresholdMs ?? _hsSpinThresholdMs;
                actualMaxSleep = maxSleepMs ?? _hsMaxSleepMs;
            }

            var t = new HighSpeedTimer(name, interval, priority, actualSpin, actualMaxSleep);
            if (handler != null) t.Elapsed += handler;
            if (_timers.TryAdd(name, t)) return t;
            throw new InvalidOperationException($"Timer with name '{name}' already exists.");
        }

        public TimerBase CreateOnTimer(string name, TimeSpan interval, Action<TimerBase> handler = null, int priority = 0)
        {
            var t = new OnTimer(name, interval, priority);
            if (handler != null) t.Elapsed += handler;
            if (_timers.TryAdd(name, t)) return t;
            throw new InvalidOperationException($"Timer with name '{name}' already exists.");
        }

        public TimerBase CreateOffTimer(string name, TimeSpan interval, Action<TimerBase> handler = null, int priority = 0)
        {
            var t = new OffTimer(name, interval, priority);
            if (handler != null) t.Elapsed += handler;
            if (_timers.TryAdd(name, t)) return t;
            throw new InvalidOperationException($"Timer with name '{name}' already exists.");
        }

        public bool TryGetTimer(string name, out TimerBase timer)
        {
            if (string.IsNullOrEmpty(name)) { timer = null; return false; }
            return _timers.TryGetValue(name, out timer);
        }

        public IEnumerable<string> GetTimerNames()
        {
            return _timers.Keys;
        }
    }
}
