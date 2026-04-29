using LidGuardLib.Commons.Results;

namespace LidGuardLib.Commons.Platform;

public interface ILidGuardRuntimePlatform
{
    bool IsSupported { get; }

    string UnsupportedMessage { get; }

    LidGuardOperationResult<LidGuardRuntimeServiceSet> CreateRuntimeServiceSet();
}
