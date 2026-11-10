using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace OrbitCalendar
{
    internal sealed class ParticleMenuEffect : ShaderEffect
    {
        static readonly PixelShader shader = LoadShader();
        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(ParticleMenuEffect), 0);
        public static readonly DependencyProperty PixelSizeProperty = DependencyProperty.Register("PixelSize", typeof(Point), typeof(ParticleMenuEffect), new UIPropertyMetadata(new Point(422, 444), PixelShaderConstantCallback(0)));
        public static readonly DependencyProperty PatternProperty = DependencyProperty.Register("Pattern", typeof(double), typeof(ParticleMenuEffect), new UIPropertyMetadata(0.0, PixelShaderConstantCallback(1)));
        public Brush Input { get { return (Brush)GetValue(InputProperty); } set { SetValue(InputProperty, value); } }
        public Point PixelSize { get { return (Point)GetValue(PixelSizeProperty); } set { SetValue(PixelSizeProperty, value); } }
        public double Pattern { get { return (double)GetValue(PatternProperty); } set { SetValue(PatternProperty, value); } }
        static PixelShader LoadShader()
        {
            var value = new PixelShader();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("menu-particles.ps")) value.SetStreamSource(stream);
            value.Freeze(); return value;
        }
        public ParticleMenuEffect()
        {
            PixelShader = shader;
            UpdateShaderValue(InputProperty); UpdateShaderValue(PixelSizeProperty); UpdateShaderValue(PatternProperty);
        }
    }
    internal partial class CalendarPanel
    {
        void InitializeParticleMenus()
        {
            foreach (var name in new[] { "PlaceConfirm", "WeatherPlacesPicker", "PlaceEditPicker", "PlaceSearchPicker", "MarketPicker", "UpdatePicker", "TodoEditor", "TimetableEditor" })
            {
                var picker = Get<Border>(name);
                var particles = new ParticleMenuEffect { PixelSize = new Point(picker.Width, picker.Height), Pattern = Preferences.Pattern };
                picker.Effect = particles;
                picker.SizeChanged += delegate(object sender, SizeChangedEventArgs e) { particles.PixelSize = new Point(Math.Max(1, e.NewSize.Width), Math.Max(1, e.NewSize.Height)); };
                picker.IsVisibleChanged += delegate { if (picker.IsVisible) particles.Pattern = Preferences.Pattern; };
            }
        }
    }
}
