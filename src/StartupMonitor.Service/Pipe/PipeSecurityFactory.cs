using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace StartupMonitor.Service.Pipe;

/// <summary>
/// Фабрика DACL для named pipe'ов. Ограничивает доступ к каналам:
/// только SYSTEM, администраторы и интерактивные пользователи (под которыми работает UI).
/// Сетевой и анонимный доступ закрыты.
/// </summary>
internal static class PipeSecurityFactory
{
    /// <summary>
    /// Создаёт PipeSecurity с правилами:
    /// — SYSTEM: полный доступ
    /// — Администраторы: полный доступ
    /// — Интерактивные пользователи: чтение/запись
    /// </summary>
    /// <returns>Настроенный PipeSecurity</returns>
    public static PipeSecurity CreateDefault()
    {
        var security = new PipeSecurity();

        // SYSTEM — полный контроль (хост-процесс)
        security.AddAccessRule(new PipeAccessRule(
            /// <summary>SecurityIdentifier: Выполняет операцию SecurityIdentifier.</summary>
            /// <param name="WellKnownSidType.LocalSystemSid">Входные данные: значение параметра WellKnownSidType.LocalSystemSid ().</param>
            /// <param name="null">Входные данные: значение параметра null ().</param>
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        // Встроенные администраторы — полный контроль
        security.AddAccessRule(new PipeAccessRule(
            /// <summary>SecurityIdentifier: Выполняет операцию SecurityIdentifier.</summary>
            /// <param name="WellKnownSidType.BuiltinAdministratorsSid">Входные данные: значение параметра WellKnownSidType.BuiltinAdministratorsSid ().</param>
            /// <param name="null">Входные данные: значение параметра null ().</param>
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        // Интерактивные пользователи (UI) — чтение/запись
        security.AddAccessRule(new PipeAccessRule(
            /// <summary>SecurityIdentifier: Выполняет операцию SecurityIdentifier.</summary>
            /// <param name="WellKnownSidType.InteractiveSid">Входные данные: значение параметра WellKnownSidType.InteractiveSid ().</param>
            /// <param name="null">Входные данные: значение параметра null ().</param>
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));

        // Аутентифицированные пользователи (UI при входе через RDP / Fast User Switching) — чтение/запись
        security.AddAccessRule(new PipeAccessRule(
            /// <summary>SecurityIdentifier: Выполняет операцию SecurityIdentifier.</summary>
            /// <param name="WellKnownSidType.AuthenticatedUserSid">Входные данные: значение параметра WellKnownSidType.AuthenticatedUserSid ().</param>
            /// <param name="null">Входные данные: значение параметра null ().</param>
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));

        // Текущий пользователь процесса (для создания экземпляров пайпа при запуске в сессии пользователя)
        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
        {
            security.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        return security;
    }
}
