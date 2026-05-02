using System.Runtime.ExceptionServices;
using System.Text;
using LidGuard.Settings;

namespace LidGuard.Diagnostics;

internal static class LidGuardExceptionLog
{
    private const string LogDirectoryName = "log";
    private const string LogFileName = "exceptions.log";
    private static readonly object s_subscriptionGate = new();
    private static readonly object s_writeGate = new();
    private static bool s_handlersSubscribed;

    [ThreadStatic]
    private static bool s_isWriting;

    public static string GetDefaultLogFilePath()
        => Path.Combine(LidGuardSettingsStore.GetApplicationDataDirectoryPath(), LogDirectoryName, LogFileName);

    public static void SubscribeGlobalHandlers()
    {
        lock (s_subscriptionGate)
        {
            if (s_handlersSubscribed) return;

            AppDomain.CurrentDomain.FirstChanceException += HandleFirstChanceException;
            AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
            TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
            s_handlersSubscribed = true;
        }
    }

    private static void HandleFirstChanceException(object sender, FirstChanceExceptionEventArgs eventArguments)
        => AppendException("first-chance-exception", eventArguments.Exception, string.Empty);

    private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs eventArguments)
    {
        var details = $"isTerminating={eventArguments.IsTerminating}";
        if (eventArguments.ExceptionObject is Exception exception)
        {
            AppendException("unhandled-exception", exception, details);
            return;
        }

        AppendMessage("unhandled-non-exception", $"{details}{Environment.NewLine}object={eventArguments.ExceptionObject}");
    }

    private static void HandleUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs eventArguments)
    {
        var observedBefore = eventArguments.Observed;
        eventArguments.SetObserved();
        AppendException(
            "unobserved-task-exception",
            eventArguments.Exception,
            $"observedBefore={observedBefore}{Environment.NewLine}observed=true");
    }

    private static void AppendException(string eventName, Exception exception, string details)
    {
        if (s_isWriting) return;

        try
        {
            s_isWriting = true;
            var stringBuilder = CreateEntryHeader(eventName, details);
            AppendExceptionDetails(stringBuilder, exception, "exception", 0, []);
            AppendEntry(stringBuilder);
        }
        catch (Exception) { }
        finally
        {
            s_isWriting = false;
        }
    }

    private static void AppendMessage(string eventName, string message)
    {
        if (s_isWriting) return;

        try
        {
            s_isWriting = true;
            var stringBuilder = CreateEntryHeader(eventName, string.Empty);
            stringBuilder.AppendLine(message);
            AppendEntry(stringBuilder);
        }
        catch (Exception) { }
        finally
        {
            s_isWriting = false;
        }
    }

    private static StringBuilder CreateEntryHeader(string eventName, string details)
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine("-----");
        stringBuilder.AppendLine($"timestampUtc={DateTimeOffset.UtcNow:O}");
        stringBuilder.AppendLine($"event={eventName}");
        stringBuilder.AppendLine($"processId={Environment.ProcessId}");
        if (!string.IsNullOrWhiteSpace(details)) stringBuilder.AppendLine(details);
        return stringBuilder;
    }

    private static void AppendExceptionDetails(
        StringBuilder stringBuilder,
        Exception exception,
        string label,
        int depth,
        HashSet<Exception> visitedExceptions)
    {
        var indent = new string(' ', depth * 2);
        if (!visitedExceptions.Add(exception))
        {
            stringBuilder.AppendLine($"{indent}{label}.cycle=true");
            return;
        }

        stringBuilder.AppendLine($"{indent}{label}.type={exception.GetType().FullName}");
        stringBuilder.AppendLine($"{indent}{label}.message={NormalizeLine(exception.Message)}");
        stringBuilder.AppendLine($"{indent}{label}.hresult=0x{exception.HResult:X8}");
        if (!string.IsNullOrWhiteSpace(exception.Source)) stringBuilder.AppendLine($"{indent}{label}.source={NormalizeLine(exception.Source)}");
        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            stringBuilder.AppendLine($"{indent}{label}.stackTrace:");
            AppendIndentedBlock(stringBuilder, exception.StackTrace, depth + 1);
        }

        if (exception.InnerException is not null) AppendExceptionDetails(stringBuilder, exception.InnerException, $"{label}.innerException", depth + 1, visitedExceptions);

        if (exception is not AggregateException aggregateException) return;

        for (var innerExceptionIndex = 0; innerExceptionIndex < aggregateException.InnerExceptions.Count; innerExceptionIndex++)
        {
            var innerException = aggregateException.InnerExceptions[innerExceptionIndex];
            if (ReferenceEquals(innerException, exception.InnerException)) continue;
            AppendExceptionDetails(
                stringBuilder,
                innerException,
                $"{label}.innerExceptions[{innerExceptionIndex}]",
                depth + 1,
                visitedExceptions);
        }
    }

    private static void AppendIndentedBlock(StringBuilder stringBuilder, string value, int depth)
    {
        var indent = new string(' ', depth * 2);
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        foreach (var line in lines) stringBuilder.AppendLine($"{indent}{line}");
    }

    private static void AppendEntry(StringBuilder stringBuilder)
    {
        stringBuilder.AppendLine();
        lock (s_writeGate)
        {
            var logFilePath = GetDefaultLogFilePath();
            var logDirectoryPath = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrWhiteSpace(logDirectoryPath)) Directory.CreateDirectory(logDirectoryPath);
            File.AppendAllText(logFilePath, stringBuilder.ToString(), Encoding.UTF8);
        }
    }

    private static string NormalizeLine(string value)
        => value.Replace("\r\n", "\\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\n", StringComparison.Ordinal);
}
