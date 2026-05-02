namespace LidGuard.Results;

public sealed class LidGuardOperationResult<TValue>
{
    private LidGuardOperationResult(bool succeeded, TValue value, string message, int nativeErrorCode)
    {
        Succeeded = succeeded;
        Value = value;
        Message = message;
        NativeErrorCode = nativeErrorCode;
    }

    public bool Succeeded { get; }

    public TValue Value { get; }

    public string Message { get; }

    public int NativeErrorCode { get; }

    public static LidGuardOperationResult<TValue> Success(TValue value) => new(true, value, string.Empty, 0);

    public static LidGuardOperationResult<TValue> Failure(string message, int nativeErrorCode = 0) => new(false, default, message, nativeErrorCode);
}
