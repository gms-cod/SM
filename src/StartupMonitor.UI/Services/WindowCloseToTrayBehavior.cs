using System.ComponentModel;
using System.Windows;

namespace StartupMonitor.UI.Services;

/// <summary>Сохраняет жизненный цикл главного окна при нажатии крестика или Alt+F4.</summary>
internal static class WindowCloseToTrayBehavior
{
    /// <summary>
    /// Отменяет обычное закрытие и скрывает окно штатным WPF Hide без изменения Normal/Maximized.
    /// Явный Application.Shutdown из меню трея завершает приложение: WPF игнорирует Cancel во время shutdown.
    /// Системная служба не участвует в обработке видимости пользовательского окна.
    /// </summary>
    /// <param name="window">Главное окно, принадлежащее текущему Dispatcher.</param>
    /// <returns>Идемпотентное действие отсоединения, если владелец прекращает поведение до закрытия окна.</returns>
    public static Action Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.VerifyAccess();
        window.Closing += OnClosing;
        window.Closed += OnClosed;
        return Detach;

        /// <summary>Отсоединяет обработчики при завершении жизненного цикла либо явном прекращении поведения.</summary>
        void Detach()
        {
            window.Closing -= OnClosing;
            window.Closed -= OnClosed;
        }

        /// <summary>Удерживает окно и его содержимое живыми до следующего восстановления из трея.</summary>
        /// <param name="sender">Окно, получившее запрос обычного закрытия.</param>
        /// <param name="args">Отмена разрушения окна; при явном shutdown обрабатывается самим WPF.</param>
        void OnClosing(object? sender, CancelEventArgs args)
        {
            args.Cancel = true;
            window.Hide();
        }

        /// <summary>Снимает обработчики после настоящего закрытия при окончательном выходе из приложения.</summary>
        /// <param name="sender">Окончательно закрытое окно.</param>
        /// <param name="args">Аргументы завершения оконного жизненного цикла.</param>
        void OnClosed(object? sender, EventArgs args)
        {
            Detach();
        }
    }
}
