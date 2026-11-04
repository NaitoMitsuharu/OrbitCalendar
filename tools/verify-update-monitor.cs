using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using OrbitCalendar;

internal static class UpdateMonitorTests
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Pump()
    {
        var frame = new DispatcherFrame();
        var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        tick.Tick += delegate { tick.Stop(); frame.Continue = false; };
        tick.Start(); Dispatcher.PushFrame(frame);
    }
    static void Finish(Task task)
    {
        var end = DateTime.UtcNow.AddSeconds(3);
        while (!task.IsCompleted) { if (DateTime.UtcNow > end) throw new Exception("Task did not finish"); Pump(); }
    }
    [STAThread]
    static void Main()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        DateTime now = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);
        int calls = 0, changed = 0;
        CancellationToken token = CancellationToken.None;
        TaskCompletionSource<AppUpdates.Release> gate = null;
        var monitor = new UpdateMonitor(delegate(CancellationToken cancellation)
        {
            calls++; token = cancellation;
            gate = new TaskCompletionSource<AppUpdates.Release>(); return gate.Task;
        }, delegate { return now; });
        monitor.Changed += delegate { changed++; };
        monitor.Start(); monitor.Start(); Pump();
        Assert(calls == 1, "Startup must check once, even with repeated Start");
        var first = monitor.CheckIfDue();
        Assert(object.ReferenceEquals(first, monitor.CheckNow()) && calls == 1, "Automatic/manual checks must share a request");
        var update = new AppUpdates.Release { Version = new Version(9, 0, 0, 0) };
        gate.SetResult(update); Finish(first);
        Assert(monitor.Latest == update && changed == 1 && monitor.NextCheckUtc == now.AddHours(6), "Successful startup check must schedule six hours");
        now = now.AddHours(6).AddTicks(-1); monitor.CheckIfDue(); Assert(calls == 1, "Checked too soon");
        now = now.AddTicks(1); var fail = monitor.CheckIfDue(); Assert(calls == 2, "Did not check at six hours");
        gate.SetException(new IOException("offline")); Finish(fail);
        Assert(fail.IsFaulted && fail.Exception != null && monitor.Latest == update && changed == 1 && monitor.NextCheckUtc == now.AddHours(1), "Failure must preserve notice and retry in one hour");
        now = now.AddMinutes(59); monitor.CheckIfDue(); Assert(calls == 2, "Failure retried too soon");
        now = now.AddMinutes(1); var retry = monitor.CheckIfDue(); Assert(calls == 3, "Retry missing");
        var current = new AppUpdates.Release { Version = AppUpdates.CurrentVersion };
        gate.SetResult(current); Finish(retry);
        Assert(monitor.Latest == current && changed == 2 && monitor.NextCheckUtc == now.AddHours(6), "Retry success must clear obsolete notice and resume six hours");
        var manual = monitor.CheckNow(); Assert(calls == 4, "Manual check must bypass schedule");
        gate.SetResult(update); Finish(manual); now = now.AddDays(2);
        var resumed = monitor.CheckIfDue(); monitor.CheckIfDue();
        Assert(calls == 5 && object.ReferenceEquals(resumed, monitor.CheckNow()), "Resume must perform one check without a backlog");
        gate.SetResult(update); Finish(resumed);
        now = now.AddHours(6); var stopped = monitor.CheckIfDue();
        monitor.Dispose(); monitor.Dispose();
        Assert(token.IsCancellationRequested, "Shutdown must cancel the request");
        int previousChanges = changed; gate.SetResult(current); Finish(stopped);
        Assert(stopped.IsCanceled && changed == previousChanges && monitor.Latest == update, "Shutdown must ignore late results");
        monitor.Start(); monitor.CheckIfDue(); Assert(calls == 6, "Disposed monitor restarted");
        Console.WriteLine("PASS: startup, six-hour cadence, one-hour retry, manual/automatic request sharing, retained notice on failure, recovery, sleep resume, shutdown cancellation and late results");
    }
}
