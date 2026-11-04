using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.UI;

/// <summary>Проверяет реальную таблицу процессов без создания MainWindow и изменения пользовательских настроек.</summary>
public sealed partial class NetworkRenderingRegressionTests
{
    /// <summary>Имя процесса остаётся видимым, правые действия доступны прокруткой, список сохраняет виртуализацию.</summary>
    /// <param name="pageWidth">Ширина страницы после вычета боковой навигации из ширины окна.</param>
    /// <param name="height">Доступная высота таблицы.</param>
    [Theory]
    [InlineData(840, 320)]
    [InlineData(1196, 500)]
    public void ProcessTable_CompactWidthPreservesNamesAndVirtualizedRows(double pageWidth, double height)
        => _host.Run(() =>
        {
            var root = LoadProcessTable();
            var processes = Enumerable.Range(1, 500).Select(index => new ProcessInfo
            {
                ProcessId = index,
                ProcessName = $"Process-{index}.exe",
                Publisher = "Test publisher",
                WorkingSetBytes = 1024 * 1024,
                IsStartupEntry = true
            }).ToArray();
            root.DataContext = new { FilteredProcesses = processes };
            Layout(root, pageWidth, height);

            var list = Descendants<ListBox>(root).Single(control => control.Name == "ProcessList");
            Assert.Equal(processes.Length, list.Items.Count);
            var first = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
            var row = Descendants<Grid>(first).Single(grid => grid.ColumnDefinitions.Count == 9);
            Assert.True(row.ColumnDefinitions[1].ActualWidth >= 200,
                $"Имя процесса схлопнулось: ширина {row.ColumnDefinitions[1].ActualWidth:F1} DIP при странице {pageWidth} DIP.");
            var name = Descendants<TextBlock>(first).Single(text => text.Text == "Process-1.exe");
            var nameBounds = name.TransformToAncestor(root).TransformBounds(new Rect(name.RenderSize));
            Assert.True(name.ActualWidth > 30 && nameBounds.Left >= 0 && nameBounds.Right <= root.ActualWidth);

            var realized = Enumerable.Range(0, list.Items.Count)
                .Count(index => list.ItemContainerGenerator.ContainerFromIndex(index) is not null);
            Assert.InRange(realized, 1, 49);

            var horizontal = Descendants<ScrollViewer>(root).FirstOrDefault(viewer => viewer.ScrollableWidth > 0);
            if (pageWidth < 1000)
            {
                Assert.NotNull(horizontal);
                var header = Descendants<Grid>(root).First(grid => grid.ColumnDefinitions.Count == 9);
                var headerBefore = header.TransformToAncestor(root).Transform(new Point()).X;
                var rowBefore = row.TransformToAncestor(root).Transform(new Point()).X;
                horizontal.ScrollToRightEnd();
                Layout(root, pageWidth, height);
                var deltaHeader = header.TransformToAncestor(root).Transform(new Point()).X - headerBefore;
                var deltaRow = row.TransformToAncestor(root).Transform(new Point()).X - rowBefore;
                Assert.True(deltaHeader < -100);
                Assert.Equal(deltaHeader, deltaRow, 1);
                var actions = row.Children.OfType<StackPanel>().Single(panel => Grid.GetColumn(panel) == 8);
                var bounds = actions.TransformToAncestor(root).TransformBounds(new Rect(actions.RenderSize));
                Assert.True(bounds.Right <= root.ActualWidth && bounds.Left >= 0,
                    $"Правые действия недоступны после горизонтальной прокрутки: {bounds}.");
            }
        });

    /// <summary>Извлекает производственный XAML блока таблицы, используя настоящие ресурсы приложения из общего STA-host.</summary>
    /// <returns>Независимый Border с заголовком и ListBox, не запускающий IPC или lifecycle MainWindow.</returns>
    private static Border LoadProcessTable()
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "StartupMonitor.UI", "Views", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var list = document.Descendants(presentation + "ListBox").Single(element => (string?)element.Attribute(xaml + "Name") == "ProcessList");
        var table = new XElement(list.Ancestors(presentation + "Border").First());
        var window = document.Root ?? throw new InvalidDataException("Корневое окно отсутствует в MainWindow.xaml.");
        foreach (var attribute in window.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            table.SetAttributeValue(attribute.Name, attribute.Value);
        var context = new ParserContext { BaseUri = new Uri("pack://application:,,,/StartupMonitor.UI;component/") };
        return (Border)XamlReader.Parse(table.ToString(), context);
    }
}
