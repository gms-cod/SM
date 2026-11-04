using System.Reflection;
using System.Windows.Controls;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.Views;

namespace StartupMonitor.Tests.UI;

/// <summary>Проверяет реальные XAML-события во время создания сетевой истории.</summary>
public sealed partial class NetworkRenderingRegressionTests
{
    /// <summary>Конструктор не пишет ошибки ранних фильтров, а готовая таблица получает исходный пустой снимок.</summary>
    [Fact]
    public void HistoryConstructor_InitializesTableWithoutFilterErrors()
        => _host.Run(() =>
        {
            var log = Path.Combine(AppContext.BaseDirectory, "logs", "ui-debug.log");
            var previousLength = File.Exists(log) ? new FileInfo(log).Length : 0;
            using var client = new PipeClient();
            var view = new NetworkSecurityHubView(client);
            try
            {
                Assert.Empty(((DataGrid)view.FindName("GridHubHistory")).Items.Cast<object>());
                Assert.NotNull(((DataGrid)view.FindName("GridHubHistory")).ItemsSource);
                using var stream = new FileStream(log, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);
                stream.Position = Math.Min(previousLength, stream.Length);
                using var reader = new StreamReader(stream);
                Assert.DoesNotContain("ошибка фильтрации истории", reader.ReadToEnd(), StringComparison.Ordinal);
            }
            finally { view.Deactivate(); }
        });

    /// <summary>Фильтр и выделение безопасно обрабатывают ещё не созданные в XAML кнопки.</summary>
    [Fact]
    public void HistoryActions_IgnoreEarlyEventsUntilAllControlsExist()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var view = new NetworkSecurityHubView(client);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var button = typeof(NetworkSecurityHubView).GetField("BtnHubHistCopyIp", flags)
                ?? throw new MissingFieldException("Отсутствует действие копирования адреса.");
            var saved = button.GetValue(view);
            try
            {
                button.SetValue(view, null);
                foreach (var methodName in new[] { "ApplyHubHistoryFilter", "UpdateHubHistButtons" })
                {
                    var method = typeof(NetworkSecurityHubView).GetMethod(methodName, flags)
                        ?? throw new MissingMethodException(methodName);
                    method.Invoke(view, null);
                }
            }
            finally { button.SetValue(view, saved); view.Deactivate(); }
        });
}
