namespace LidGuardLib.Commons.Results;

public sealed class LidGuardOperationResult
{
    private LidGuardOperationResult(bool succeeded, string message, int nativeErrorCode)
    {
        Succeeded = succeeded;
        Message = message;
        NativeErrorCode = nativeErrorCode;
    }

    public bool Succeeded { get; }

    public string Message { get; }

    public int NativeErrorCode { get; }

    public static LidGuardOperationResult Success() => new(true, string.Empty, 0);

    public static LidGuardOperationResult Failure(string message, int nativeErrorCode = 0) => new(false, message, nativeErrorCode);
}
