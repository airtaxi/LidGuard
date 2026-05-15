namespace LidGuard.Hooks;

public interface IHookInstaller
{
    HookInstallationInspection Inspect(HookInstallationRequest request);

    HookInstallationResult Install(HookInstallationRequest request);

    HookInstallationResult Remove(HookInstallationRequest request);

    HookInstallationRequest CreateDefaultRequest(string configurationFilePath = "", bool createBackup = true);
}
