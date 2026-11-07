using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Xml.Linq;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Converters;
using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.UI;

/// <summary>Проверяет обновление производственной привязки Boot Impact без изменения самой записи автозапуска.</summary>
public sealed partial class NetworkRenderingRegressionTests
{
    /// <summary>Настоящие TextBlock и заголовок из XAML переводятся RU→EN→RU при неизменных Driver/Service/RegistryRun.</summary>
    [Fact]
    public void BootImpactLanguageSwitch_UpdatesExistingBadgesAndHeaderWithoutLocationChange()
        => _host.Run(() =>
        {
            var localization = LocalizationService.Instance;
            var previousCulture = CultureInfo.CurrentUICulture.Name;
            var previousFormattingCulture = CultureInfo.CurrentCulture;
            var document = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "StartupMonitor.UI", "Views", "MainWindow.xaml"));
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var badgeMarkup = document.Descendants(presentation + "TextBlock")
                .Single(element => element.ToString().Contains("LocationToImpactTextConverter", StringComparison.Ordinal));
            var headerMarkup = document.Descendants(presentation + "Run")
                .Single(element => ((string?)element.Attribute("Text"))?.Contains("ColumnBootImpact", StringComparison.Ordinal) == true);
            var entries = new[]
            {
                new StartupEntry { Location = EntryLocation.Driver },
                new StartupEntry { Location = EntryLocation.Service },
                new StartupEntry { Location = EntryLocation.RegistryRun }
            };
            var header = new TextBlock();
            var headerRun = (Run)XamlReader.Parse(headerMarkup.ToString());
            header.Inlines.Add(headerRun);
            var panel = new StackPanel();
            panel.Children.Add(header);
            var badges = entries.Select(entry =>
            {
                var badge = (TextBlock)XamlReader.Parse(badgeMarkup.ToString());
                badge.DataContext = entry;
                panel.Children.Add(badge);
                return badge;
            }).ToArray();
            try
            {
                foreach (var language in new[] { "ru", "en", "ru" })
                {
                    localization.ChangeLanguage(language);
                    Layout(panel, 600, 240);
                    Assert.Equal(language == "en" ? "Boot Impact" : "Влияние", headerRun.Text);
                    Assert.Equal(language == "en" ? new[] { "High", "Medium", "Low" } : new[] { "Высокое", "Среднее", "Низкое" }, badges.Select(badge => badge.Text));
                    Assert.Equal(new[] { EntryLocation.Driver, EntryLocation.Service, EntryLocation.RegistryRun }, entries.Select(entry => entry.Location));
                }
            }
            finally
            {
                localization.ChangeLanguage(previousCulture);
                CultureInfo.CurrentCulture = previousFormattingCulture;
            }
        });

    /// <summary>Неполный MultiBinding безопасен, а связанные подписи имеют приоритет над устаревшей культурой привязки.</summary>
    [Fact]
    public void BootImpactMultiBinding_HandlesIncompleteValuesAndUsesBoundTranslations()
        => _host.Run(() =>
        {
            var localization = LocalizationService.Instance;
            var previousCulture = CultureInfo.CurrentUICulture.Name;
            var previousFormattingCulture = CultureInfo.CurrentCulture;
            var converter = new LocationToImpactTextConverter();
            try
            {
                localization.ChangeLanguage("en");
                Assert.Equal("Low", converter.Convert((object[]?)null, typeof(string), null, CultureInfo.InvariantCulture));
                Assert.Equal("Low", converter.Convert(Array.Empty<object>(), typeof(string), null, CultureInfo.InvariantCulture));
                Assert.Equal("High", converter.Convert(new object[] { EntryLocation.Driver }, typeof(string), null, CultureInfo.InvariantCulture));
                Assert.Equal("Высокое", converter.Convert(new object[] { EntryLocation.Driver, "Низкое", "Среднее", "Высокое" }, typeof(string), null, CultureInfo.GetCultureInfo("en")));
                Assert.Equal("Low", converter.Convert(new object[] { DependencyProperty.UnsetValue }, typeof(string), null, CultureInfo.InvariantCulture));
                Assert.Throws<NotSupportedException>(() => converter.ConvertBack("High", new[] { typeof(EntryLocation) }, null, CultureInfo.InvariantCulture));
            }
            finally
            {
                localization.ChangeLanguage(previousCulture);
                CultureInfo.CurrentCulture = previousFormattingCulture;
            }
        });
}
