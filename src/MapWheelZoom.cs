using System;

namespace OrbitCalendar
{
    // Use wheel detents rather than Windows' text-scroll line count. High-
    // resolution deltas accumulate; duplicate/momentum events cannot jump levels.
    internal sealed class MapWheelZoom
    {
        int remainder, direction;
        DateTime lastInput, nextAllowed;
        public int Step(int delta, DateTime now)
        {
            if (delta == 0) return 0;
            int sign = Math.Sign(delta);
            if (sign != direction || now - lastInput > TimeSpan.FromMilliseconds(350)) remainder = 0;
            if (sign != direction) nextAllowed = DateTime.MinValue;
            direction = sign; lastInput = now;
            if (now < nextAllowed) return 0;
            remainder += Math.Max(-120, Math.Min(120, delta));
            if (Math.Abs(remainder) < 120) return 0;
            remainder = 0;
            nextAllowed = now.AddMilliseconds(180);
            return sign;
        }
        public void Reset() { remainder = direction = 0; lastInput = nextAllowed = DateTime.MinValue; }
    }
}
