using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace OrbitCalendar
{
    internal sealed class OutsideClick : IDisposable
    {
        public volatile bool Enabled;
        readonly Dispatcher dispatcher;
        readonly Action dismiss;
        readonly int ownId = Process.GetCurrentProcess().Id;
        readonly Thread thread;
        readonly Hook callback;
        uint threadId;
        IntPtr handle;
        delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int type, Hook callback, IntPtr module, uint id);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out int id);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint message, IntPtr wp, IntPtr lp);

        public OutsideClick(Action dismiss)
        {
            this.dismiss = dismiss;
            dispatcher = Dispatcher.CurrentDispatcher;
            callback = Observe;
            using (var ready = new ManualResetEvent(false))
            {
                thread = new Thread(delegate()
                {
                    threadId = GetCurrentThreadId();
                    // Force the thread message queue into existence before publishing readiness.
                    var context = new Forms.ApplicationContext();
                    using (var parking = new Forms.Control())
                    {
                        var unused = parking.Handle;
                        handle = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
                        ready.Set();
                        try { Forms.Application.Run(context); }
                        finally { if (handle != IntPtr.Zero) UnhookWindowsHookEx(handle); context.Dispose(); }
                    }
                }) { IsBackground = true, Name = "Orbit outside click" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                ready.WaitOne();
            }
        }

        IntPtr Observe(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && Enabled && (message.ToInt32() == 0x201 || message.ToInt32() == 0x204))
            {
                var point = (Point)Marshal.PtrToStructure(data, typeof(Point));
                int id;
                GetWindowThreadProcessId(WindowFromPoint(point), out id);
                if (id != ownId && !dispatcher.HasShutdownStarted)
                    dispatcher.BeginInvoke(dismiss);
            }
            return CallNextHookEx(handle, code, message, data);
        }

        public void Dispose()
        {
            Enabled = false;
            PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero);
            thread.Join(500);
        }
    }
}
