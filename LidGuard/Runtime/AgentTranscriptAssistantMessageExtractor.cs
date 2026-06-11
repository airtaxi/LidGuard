using System.Text;
using System.Text.Json;
using LidGuard.Sessions;

namespace LidGuard.Runtime;

internal static class AgentTranscriptAssistantMessageExtractor
{
    private const int RecentTranscriptLineLimit = 1024;
    private const int RecentTranscriptByteLimit = 1_048_576;

    public static string CreateLastAssistantMessage(AgentProvider provider, string transcriptPath)
    {
        if (string.IsNullOrWhiteSpace(transcriptPath)) return null;

        foreach (var transcriptLine in ReadRecentTranscriptLines(transcriptPath, RecentTranscriptLineLimit, RecentTranscriptByteLimit).Reverse())
        {
            if (!TryExtractAssistantResponseText(provider, transcriptLine, out var responseText)) continue;
            if (!string.IsNullOrWhiteSpace(responseText)) return responseText.Trim();
        }

        return null;
    }

    private static bool TryExtractAssistantResponseText(AgentProvider provider, string transcriptLine, out string responseText)
    {
        responseText = string.Empty;
        if (string.IsNullOrWhiteSpace(transcriptLine)) return false;

        try
        {
            using var document = JsonDocument.Parse(transcriptLine);
            var rootElement = document.RootElement;
            if (!TryGetAssistantMessageElement(provider, rootElement, out var messageElement)) return false;

            return TryExtractText(messageElement, out responseText);
        }
        catch (JsonException) { return false; }
    }

    private static bool TryGetAssistantMessageElement(AgentProvider provider, JsonElement rootElement, out JsonElement messageElement)
    {
        messageElement = default;
        if (rootElement.ValueKind != JsonValueKind.Object) return false;

        if (provider == AgentProvider.Codex && TryGetStringProperty(rootElement, "type", out var recordType) && recordType.Equals("response_item", StringComparison.Ordinal) && rootElement.TryGetProperty("payload", out var codexPayloadElement) && IsAssistantMessageElement(codexPayloadElement))
        {
            messageElement = codexPayloadElement;
            return true;
        }

        if (IsAssistantMessageElement(rootElement))
        {
            messageElement = rootElement;
            return true;
        }

        if (rootElement.TryGetProperty("message", out var messagePropertyElement) && IsAssistantMessageElement(messagePropertyElement))
        {
            messageElement = messagePropertyElement;
            return true;
        }

        if (rootElement.TryGetProperty("payload", out var payloadElement) && IsAssistantMessageElement(payloadElement))
        {
            messageElement = payloadElement;
            return true;
        }

        if (rootElement.TryGetProperty("data", out var dataElement) && IsAssistantMessageElement(dataElement))
        {
            messageElement = dataElement;
            return true;
        }

        return false;
    }

    private static bool IsAssistantMessageElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;

        if (TryGetStringProperty(element, "role", out var role) && role.Equals("assistant", StringComparison.OrdinalIgnoreCase)) return true;
        if (TryGetStringProperty(element, "type", out var type) && (type.Equals("assistant", StringComparison.OrdinalIgnoreCase) || type.Equals("assistant_message", StringComparison.OrdinalIgnoreCase))) return true;

        return element.TryGetProperty("author", out var authorElement) && TryGetStringProperty(authorElement, "role", out var authorRole) && authorRole.Equals("assistant", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractText(JsonElement messageElement, out string text)
    {
        var textBuilder = new StringBuilder();
        if (messageElement.TryGetProperty("content", out var contentElement)) AppendText(contentElement, textBuilder);
        if (messageElement.TryGetProperty("text", out var textElement)) AppendText(textElement, textBuilder);
        if (messageElement.TryGetProperty("message", out var nestedMessageElement)) AppendText(nestedMessageElement, textBuilder);
        if (messageElement.TryGetProperty("output", out var outputElement)) AppendText(outputElement, textBuilder);
        if (messageElement.TryGetProperty("response", out var responseElement)) AppendText(responseElement, textBuilder);

        text = textBuilder.ToString();
        return !string.IsNullOrWhiteSpace(text);
    }

    private static void AppendText(JsonElement element, StringBuilder textBuilder)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            AppendText(textBuilder, element.GetString());
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var itemElement in element.EnumerateArray()) AppendText(itemElement, textBuilder);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object) return;

        if (element.TryGetProperty("text", out var textElement)) AppendText(textElement, textBuilder);
        if (element.TryGetProperty("content", out var contentElement)) AppendText(contentElement, textBuilder);
        if (element.TryGetProperty("message", out var messageElement)) AppendText(messageElement, textBuilder);
        if (element.TryGetProperty("output", out var outputElement)) AppendText(outputElement, textBuilder);
        if (element.TryGetProperty("response", out var responseElement)) AppendText(responseElement, textBuilder);
        if (element.TryGetProperty("delta", out var deltaElement)) AppendText(deltaElement, textBuilder);
    }

    private static void AppendText(StringBuilder textBuilder, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (textBuilder.Length > 0) textBuilder.Append(' ');
        textBuilder.Append(text.Trim());
    }

    private static string[] ReadRecentTranscriptLines(string transcriptPath, int lineLimit, int byteLimit)
    {
        try
        {
            using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length == 0) return [];

            var transcriptLength = stream.Length;
            var bytesToRead = (int)Math.Min(transcriptLength, byteLimit);
            var startsAtBeginning = transcriptLength <= bytesToRead;
            var buffer = new byte[bytesToRead];
            stream.Seek(-bytesToRead, SeekOrigin.End);
            var bytesRead = 0;
            while (bytesRead < bytesToRead)
            {
                var currentBytesRead = stream.Read(buffer, bytesRead, bytesToRead - bytesRead);
                if (currentBytesRead == 0) break;
                bytesRead += currentBytesRead;
            }

            var transcriptText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            if (!startsAtBeginning)
            {
                var firstNewLineIndex = transcriptText.IndexOf('\n');
                transcriptText = firstNewLineIndex >= 0 ? transcriptText[(firstNewLineIndex + 1)..] : string.Empty;
            }

            if (string.IsNullOrWhiteSpace(transcriptText)) return [];

            return transcriptText
                .Split('\n')
                .Select(transcriptLine => transcriptLine.TrimEnd('\r'))
                .Where(transcriptLine => !string.IsNullOrWhiteSpace(transcriptLine))
                .TakeLast(lineLimit)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException or PathTooLongException) { return []; }
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (!element.TryGetProperty(propertyName, out var propertyElement)) return false;
        if (propertyElement.ValueKind != JsonValueKind.String) return false;

        value = propertyElement.GetString() ?? string.Empty;
        return true;
    }
}
