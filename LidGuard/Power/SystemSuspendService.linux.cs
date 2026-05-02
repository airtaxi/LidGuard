using LidGuard.Platform;
using LidGuard.Results;
using LidGuard.Services;

namespace LidGuard.Power;

public sealed class SystemSuspendService : ISystemSuspendService
{
    private static readonly TimeSpan s_systemctlTimeout = TimeSpan.FromSeconds(30);

    public LidGuardOperationResult Suspend(SystemSuspendMode suspendMode)
    {
        if (!LinuxCommandPathResolver.TryFindExecutable("systemctl", out var systemctlPath))
            return LidGuardOperationResult.Failure("systemctl was not found on PATH. LidGuard Linux suspend support requires systemd/logind.");

        var commandName = suspendMode == SystemSuspendMode.Hibernate ? "hibernate" : "suspend";
        var commandResult = LinuxCommandRunner.Run(systemctlPath, [commandName], s_systemctlTimeout);
        if (commandResult.Succeeded) return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure(commandResult.CreateFailureMessage($"systemctl {commandName}"), commandResult.ExitCode);
    }
}
