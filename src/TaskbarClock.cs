using System;
using System.Drawing;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using Forms = System.Windows.Forms;

namespace OrbitCalendar
{
    // This control lives in the taskbar's client area. Explorer clips and moves it
    // together with the taskbar, including when the user enables auto-hide.
    internal sealed class TaskbarClock : Forms.Control
    {
        readonly IntPtr taskbar;
        readonly Forms.ContextMenuStrip menu;
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
        protected override Forms.CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Parent = taskbar;
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                cp.Style |= 0x40000000; // WS_CHILD
                return cp;
            }
        }

        internal TaskbarClock(IntPtr taskbar, Action toggle, Forms.ContextMenuStrip menu)
        {
            this.taskbar = taskbar;
            this.menu = menu;
            AccessibleName = "Orbit Calendar タスクバー時計";
            AccessibleRole = Forms.AccessibleRole.PushButton;
            Cursor = Forms.Cursors.Hand;
            SetStyle(Forms.ControlStyles.StandardDoubleClick, false);
            MouseClick += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) toggle(); };
        }

        // Explorer is the native parent, so WinForms' implicit ContextMenuStrip
        // routing cannot find an owning Form. Handle the right-button release
        // directly and show an application-owned popup in screen coordinates.
        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == 0x205) // RBUTTONUP
            {
                // Complete MouseUp first so the clock releases mouse capture;
                // otherwise the first menu click is swallowed by the clock.
                base.WndProc(ref message);
                BeginInvoke(new Action(ShowClockMenu));
                message.Result = IntPtr.Zero; return;
            }
            if (message.Msg == 0x7B) // CONTEXTMENU, including keyboard invocation
            {
                BeginInvoke(new Action(ShowClockMenu)); message.Result = IntPtr.Zero; return;
            }
            base.WndProc(ref message);
        }
        internal void ShowClockMenu()
        {
            if (IsDisposed || Disposing || !IsHandleCreated || menu == null || menu.IsDisposed || menu.Visible) return;
            menu.Show(Forms.Cursor.Position);
        }

        // Let Explorer draw the date, time, and theme. Alpha 1 still accepts clicks.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetLayeredWindowAttributes(Handle, 0, 1, 2);
        }

    }

    internal sealed class ClockHost : IDisposable
    {
        readonly System.Windows.Threading.Dispatcher dispatcher;
        readonly Action toggle;
        readonly Forms.ContextMenuStrip menu;
        readonly Timer refresh;
        TaskbarClock clock;
        IntPtr owner;
        int probing;
        volatile bool disposed;

        public ClockHost(Action toggle, Forms.ContextMenuStrip menu)
        {
            this.toggle = toggle;
            this.menu = menu;
            dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            refresh = new Timer(Probe, null, 0, 1500);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string name, string title);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }

        static Rectangle ClockBounds(IntPtr bar)
        {
            if (bar == IntPtr.Zero) return Rectangle.Empty;
            var root = AutomationElement.FromHandle(bar);
            // The inner text is in Explorer's raw accessibility tree. Its AutomationId
            // is independent of the language, time format and adjacent bell button.
            var buttons = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"));
            var raw = TreeWalker.RawViewWalker;
            foreach (AutomationElement button in buttons)
            {
                if (!button.Current.ClassName.StartsWith("SystemTray.OmniButton", StringComparison.Ordinal)) continue;
                var bounds = System.Windows.Rect.Empty;
                bool hasTime = false, hasDate = false;
                for (var child = raw.GetFirstChild(button); child != null; child = raw.GetNextSibling(child))
                {
                    string id = child.Current.AutomationId;
                    if (id != "TimeInnerTextBlock" && id != "DateInnerTextBlock") continue;
                    var box = child.Current.BoundingRectangle;
                    if (box.IsEmpty || box.Width < 1 || box.Height < 1) continue;
                    bounds.Union(box);
                    hasTime |= id == "TimeInnerTextBlock";
                    hasDate |= id == "DateInnerTextBlock";
                }
                if (!hasTime) continue;
                var available = button.Current.BoundingRectangle;
                if (available.IsEmpty || available.Width < 1 || available.Height < 1) continue;
                if (!hasDate) { bounds.Y = available.Y; bounds.Height = available.Height; }
                // Use the native text column only, even when the same button also owns a bell.
                bounds.Inflate(2, 2);
                bounds.Intersect(available);
                if (bounds.IsEmpty || bounds.Width < 30 || bounds.Height < 18) continue;
                var point = new Point((int)Math.Ceiling(bounds.Left), (int)Math.Ceiling(bounds.Top));
                if (!ScreenToClient(bar, ref point)) continue;
                return new Rectangle(point.X, point.Y, (int)Math.Floor(bounds.Width), (int)Math.Floor(bounds.Height));
            }
            return Rectangle.Empty;
        }

        void Probe(object state)
        {
            if (disposed || Interlocked.Exchange(ref probing, 1) != 0) return;
            try
            {
                var bar = FindWindow("Shell_TrayWnd", null);
                Rectangle bounds = Rectangle.Empty;
                try { bounds = ClockBounds(bar); }
                catch (ElementNotAvailableException) { }
                catch (COMException) { }
                catch (InvalidOperationException) { }
                catch (ArgumentException) { }
                catch (TimeoutException) { }
                if (!disposed && !dispatcher.HasShutdownStarted)
                    dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (disposed) return;
                        try { Attach(bar, bounds); }
                        catch (Win32Exception) { RemoveClock(); } // Explorer restarted during attachment.
                    }));
            }
            finally { Interlocked.Exchange(ref probing, 0); }
        }

        void Attach(IntPtr bar, Rectangle bounds)
        {
            NativeRect client;
            if (bar == IntPtr.Zero || !GetClientRect(bar, out client) ||
                !new Rectangle(0, 0, client.Right, client.Bottom).Contains(bounds) || bounds.IsEmpty)
            { RemoveClock(); return; }
            if (clock == null || !clock.IsHandleCreated || owner != bar)
            {
                RemoveClock();
                owner = bar;
                clock = new TaskbarClock(bar, toggle, menu);
            }
            SetWindowPos(clock.Handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0050); // SHOWWINDOW | NOACTIVATE
        }

        void RemoveClock()
        {
            if (clock != null) { clock.Dispose(); clock = null; }
        }

        public void Dispose()
        {
            disposed = true;
            refresh.Dispose();
            RemoveClock();
        }
    }
}
