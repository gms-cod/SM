using System.Windows;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.UI.Views.Dialogs;

/// <summary>
/// Защитный барьер: проверяет допустимость блокировки системных процессов Windows
/// и запрашивает подтверждение при блокировке компонентов подсистем ОС.
/// </summary>
public static class SystemProtectionDialogHelper
{
    /// <summary>
    /// Проверяет допустимость блокировки процесса и отображает предупреждающий диалог.
    /// </summary>
    /// <param name="owner">Окно-владелец диалога.</param>
    /// <param name="processName">Имя исполняемого процесса.</param>
    /// <param name="level">Уровень системной защиты процесса.</param>
    /// <returns>True, если блокировка разрешена пользователем; иначе false.</returns>
    public static bool ConfirmBlocking(Window? owner, string processName, ProcessProtectionLevel level)
    {
        if (level == ProcessProtectionLevel.Critical)
        {
            var msg = $"Процесс «{processName}» является критическим компонентом ядра Windows (DNS, DHCP, RPC).\n\n" +
                      "Его блокировка нарушит работу сети и категорически запрещена политикой безопасности.";

            if (owner != null)
                MessageBox.Show(owner, msg, "Защита ядра Windows", MessageBoxButton.OK, MessageBoxImage.Stop);
            else
                MessageBox.Show(msg, "Защита ядра Windows", MessageBoxButton.OK, MessageBoxImage.Stop);

            return false;
        }

        if (level == ProcessProtectionLevel.HighRisk)
        {
            var msg = $"ВНИМАНИЕ! «{processName}» является защищенным системным процессом Windows.\n\n" +
                      "Блокировка его сетевого доступа может нарушить работу проводника, сетевых папок или печати.\n\n" +
                      "Вы действительно уверены, что хотите заблокировать сетевой доступ для этого процесса?";

            var res = owner != null
                ? MessageBox.Show(owner, msg, "Подтверждение блокировки системного процесса", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                : MessageBox.Show(msg, "Подтверждение блокировки системного процесса", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            return res == MessageBoxResult.Yes;
        }

        return true;
    }
}

