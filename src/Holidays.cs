using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace OrbitCalendar
{
    internal static class Holidays
    {
        static readonly Dictionary<DateTime, string> dates = new Dictionary<DateTime, string>();

        static Holidays()
        {
            using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("holidays.csv")))
            {
                reader.ReadLine();
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var fields = line.Split(',');
                    dates.Add(DateTime.ParseExact(fields[0], "yyyy/M/d", CultureInfo.InvariantCulture), fields[1]);
                }
            }
        }

        public static string Name(DateTime day)
        {
            string name;
            return dates.TryGetValue(day.Date, out name) ? name : null;
        }
    }
}
