using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Maple.WindowsHost.Preview;

public sealed class PreviewWindowHost
{
    private Window? window;

    public void Show()
    {
        if (window is { IsVisible: true })
        {
            window.Activate();
            return;
        }

        window = new Window
        {
            Title = "Maple Product 实时预览",
            Width = 900,
            Height = 560,
            MinWidth = 640,
            MinHeight = 420,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 31, 30)),
            Content = new Grid
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "等待 Windows.Graphics.Capture 采集画面",
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 18,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "FPS 0   Frame age -   Dropped frames 0",
                        Foreground = System.Windows.Media.Brushes.LightGray,
                        Margin = new Thickness(16),
                        VerticalAlignment = VerticalAlignment.Bottom
                    }
                }
            }
        };
        window.Closed += (_, _) => window = null;
        window.Show();
    }
}
