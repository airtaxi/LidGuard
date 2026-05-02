using LidGuard.Processes;
using LidGuard.Results;
using LidGuard.Sessions;

namespace LidGuard.Services;

public interface ICommandLineProcessResolver
{
    LidGuardOperationResult<CommandLineProcessCandidate> FindForWorkingDirectory(string workingDirectory, AgentProvider provider = AgentProvider.Unknown);
}
