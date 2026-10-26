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
    /// — Интерактивные пользователи: чтение/запись без создания новых инстансов (предотвращение LPE)
    /// </summary>
    /// <returns>Настроенный PipeSecurity</returns>
    public static PipeSecurity CreateDefault()
    {
        var security = new PipeSecurity();

        // Отключаем наследование правил доступа от родительских контейнеров ядра
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        // SYSTEM — полный контроль (хост-процесс службы)
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        // Встроенные администраторы — полный контроль
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        // Интерактивные пользователи (UI) — строго чтение и запись без права создания экземпляров канала (LPE & Pipe Squatting защита)
        var clientRights = (PipeAccessRights.Read | PipeAccessRights.Write) & ~PipeAccessRights.CreateNewInstance;
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            clientRights, AccessControlType.Allow));

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
