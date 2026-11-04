using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        void InitializeRainLegend()
        {
            // JMA's normal palette and boundaries match the unmodified radar tiles.
            // Both products use these bins, with their respective mm/h or mm unit.
            string[] colors = { "#F2F2FF", "#A0D2FF", "#218CFF", "#0041FF", "#FFF500", "#FF9900", "#FF2800", "#B40068" };
            string[] numbers = { "<1", "1", "5", "10", "20", "30", "50", "80+" };
            var canvas = Get<Canvas>("RainLegendBar");
            const double width = (LiveData.MapWidth - 68) / 8.0;
            for (int i = 0; i < colors.Length; i++)
            {
                var color = new Rectangle { Width = width, Height = 8, Fill = Art.Ink(colors[i]),
                    Stroke = Art.Ink("#D7E6E9"), StrokeThickness = .7 };
                Canvas.SetLeft(color, i * width); Canvas.SetTop(color, 2); canvas.Children.Add(color);
                var number = new TextBlock { Text = numbers[i], Width = width, TextAlignment = TextAlignment.Center,
                    Foreground = Art.Ink("#D5EAEC"), FontSize = 9, FontFamily = new FontFamily("Segoe UI") };
                Canvas.SetLeft(number, i * width); Canvas.SetTop(number, 12); canvas.Children.Add(number);
            }
        }
    }
}
