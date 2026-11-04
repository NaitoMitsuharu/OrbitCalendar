using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace OrbitCalendar
{
    // Lives with the application rather than the popup. After sleep, the next
    // tick performs one due check without replaying missed checks.
    internal sealed class UpdateMonitor : IDisposable
    {
        internal static readonly TimeSpan Interval = TimeSpan.FromHours(6);
        internal static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);
        readonly Func<CancellationToken, Task<AppUpdates.Release>> fetch;
        readonly Func<DateTime> utcNow;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        Task<AppUpdates.Release> pending;
        bool started, disposed;
        public event Action<AppUpdates.Release> Changed;
        public DateTime NextCheckUtc { get; private set; }
        public AppUpdates.Release Latest { get; private set; }
        public UpdateMonitor() : this(AppUpdates.Check, delegate { return DateTime.UtcNow; }) { }
        internal UpdateMonitor(Func<CancellationToken, Task<AppUpdates.Release>> fetch, Func<DateTime> utcNow)
        {
            this.fetch = fetch; this.utcNow = utcNow;
            timer.Tick += delegate { Poll(); };
        }
        public void Start()
        {
            if (started || disposed) return;
            started = true; timer.Start();
            timer.Dispatcher.BeginInvoke(new Action(Poll), DispatcherPriority.ApplicationIdle);
        }
        async void Poll()
        {
            try { await CheckIfDue(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
        internal Task<AppUpdates.Release> CheckIfDue()
        {
            if (!started || disposed || utcNow() < NextCheckUtc) return Task.FromResult(Latest);
            return CheckNow();
        }
        public Task<AppUpdates.Release> CheckNow()
        {
            if (disposed) throw new ObjectDisposedException("UpdateMonitor");
            if (pending != null) return pending;
            var completion = new TaskCompletionSource<AppUpdates.Release>();
            pending = completion.Task;
            RunCheck(completion);
            return completion.Task;
        }
        async void RunCheck(TaskCompletionSource<AppUpdates.Release> completion)
        {
            try
            {
                var release = await fetch(lifetime.Token);
                if (disposed) { completion.TrySetCanceled(); return; }
                Latest = release; NextCheckUtc = utcNow().Add(Interval);
                if (Changed != null) Changed(release);
                completion.TrySetResult(release);
            }
            catch (Exception ex)
            {
                if (disposed) completion.TrySetCanceled();
                else
                {
                    // Preserve the confirmed update when the network is unavailable.
                    NextCheckUtc = utcNow().Add(RetryInterval);
                    completion.TrySetException(ex);
                }
            }
            finally { pending = null; }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; started = false;
            timer.Stop(); lifetime.Cancel(); lifetime.Dispose();
        }
    }
}
