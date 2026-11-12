using System.Windows;
using System.Windows.Media;
using StartupMonitor.UI.Converters;
using Wpf.Ui.Controls;

namespace StartupMonitor.Tests.UI;

/// <summary>Проверяет адаптивный контраст акцентных кнопок на реальных WPF-привязках.</summary>
public sealed partial class NetworkRenderingRegressionTests
{
    /// <summary>Тёмный и светлый фон выбирают противоположные цвета, смена кисти обновляет и обычное, и нажатое состояние.</summary>
    [Fact]
    public void PrimaryButton_UpdatesContrastWhenBackgroundChanges()
        => _host.Run(() =>
        {
            var button = new Wpf.Ui.Controls.Button { Appearance = ControlAppearance.Primary, Content = "Test" };
            button.Style = (Style)Application.Current.FindResource(typeof(Wpf.Ui.Controls.Button));
            button.Background = new SolidColorBrush(Color.FromRgb(139, 20, 167));
            button.MouseOverBackground = button.Background;
            button.PressedBackground = button.Background;
            button.ApplyTemplate();
            button.UpdateLayout();
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(button.Foreground).Color);
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(button.PressedForeground).Color);

            var light = new SolidColorBrush(Color.FromRgb(245, 230, 170));
            button.Background = light;
            button.MouseOverBackground = light;
            button.PressedBackground = light;
            button.UpdateLayout();
            Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(button.Foreground).Color);
            Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(button.PressedForeground).Color);

            button.Appearance = ControlAppearance.Secondary;
            Assert.Equal(Application.Current.FindResource("ButtonForeground"), button.Foreground);
        });

    /// <summary>Замена динамического ресурса перекрашивает кнопку без повторного создания или смены темы.</summary>
    [Fact]
    public void PrimaryButton_TracksDynamicAccentResourceReplacement()
        => _host.Run(() =>
        {
            const string key = "TestAccentButtonBackground";
            var panel = new System.Windows.Controls.Grid();
            var resources = panel.Resources;
            try
            {
                resources[key] = new SolidColorBrush(Color.FromRgb(139, 20, 167));
                var button = new Wpf.Ui.Controls.Button { Appearance = ControlAppearance.Primary };
                button.Style = (Style)Application.Current.FindResource(typeof(Wpf.Ui.Controls.Button));
                button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, key);
                button.SetResourceReference(Wpf.Ui.Controls.Button.MouseOverBackgroundProperty, key);
                button.SetResourceReference(Wpf.Ui.Controls.Button.PressedBackgroundProperty, key);
                panel.Children.Add(button);
                button.ApplyTemplate();
                Layout(panel, 300, 100);
                Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(button.Foreground).Color);
                resources[key] = new SolidColorBrush(Color.FromRgb(245, 230, 170));
                Layout(panel, 300, 100);
                Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(button.Foreground).Color);
                Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(button.PressedForeground).Color);
            }
            finally { resources.Remove(key); }
        });

    /// <summary>Полупрозрачный фон оценивается по обеим крайним подложкам, некорректный ресурс не создаёт ложный цвет.</summary>
    [Fact]
    public void Converter_AccountsForOpacityAndUnavailableResources()
    {
        var converter = new AccentButtonContrastConverter();
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var transparentPurple = new SolidColorBrush(Color.FromRgb(139, 20, 167)) { Opacity = 0.9 };
        var result = converter.Convert(new object[] { transparentPurple }, typeof(Brush), null!, culture);
        Assert.Same(Brushes.White, result);
        Assert.Same(DependencyProperty.UnsetValue,
            converter.Convert(new object[] { DependencyProperty.UnsetValue }, typeof(Brush), null!, culture));
    }
}
