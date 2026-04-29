using LidGuardLib.Commons.Processes;
using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Sessions;

namespace LidGuardLib.Commons.Services;

public interface ICommandLineProcessResolver
{
    LidGuardOperationResult<CommandLineProcessCandidate> FindForWorkingDirectory(string workingDirectory, AgentProvider provider = AgentProvider.Unknown);
}
