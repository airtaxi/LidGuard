using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class Program
{
    private static readonly HashSet<string> s_excludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "bin", "obj" };
    private static readonly Regex s_gitDiffHunkHeaderRegex = new(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@", RegexOptions.Compiled);

    public static int Main(string[] commandLineArguments)
    {
        var commandLineOptions = CommandLineOptions.Parse(commandLineArguments);
        if (commandLineOptions.ShowHelp)
        {
            WriteUsage();
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(commandLineOptions.ErrorMessage))
        {
            Console.Error.WriteLine(commandLineOptions.ErrorMessage);
            WriteUsage();
            return 2;
        }

        var sourceFilePaths = EnumerateSourceFilePaths(commandLineOptions.InputPaths).ToImmutableArray();
        var fixPlan = commandLineOptions.FixFiles ? FixPlan.Create(sourceFilePaths, commandLineOptions.FixAllFiles) : FixPlan.CheckOnly();
        if (sourceFilePaths.Length == 0)
        {
            Console.Error.WriteLine("No C# source files were found.");
            return 2;
        }

        foreach (var warningMessage in fixPlan.WarningMessages) Console.WriteLine($"WARNING: {warningMessage}");

        var totalDiagnosticCount = 0;
        var totalModifiedCount = 0;
        var totalUnsafeFixCount = 0;

        foreach (var sourceFilePath in sourceFilePaths)
        {
            if (!fixPlan.ShouldProcessFile(sourceFilePath)) continue;

            var fileResult = ProcessSourceFile(sourceFilePath, commandLineOptions.FixFiles, fixPlan.GetChangedLineRanges(sourceFilePath));
            totalDiagnosticCount += fileResult.Diagnostics.Count;
            totalModifiedCount += fileResult.Modified ? 1 : 0;
            totalUnsafeFixCount += fileResult.UnsafeFixCount;

            foreach (var styleDiagnostic in fileResult.Diagnostics) Console.WriteLine(styleDiagnostic.ToDisplayString());
        }

        if (commandLineOptions.FixFiles) Console.WriteLine($"CSharpStyleGuard fixed {totalModifiedCount} file(s), reported {totalDiagnosticCount} diagnostic(s), and skipped {totalUnsafeFixCount} unsafe span(s).");
        else Console.WriteLine($"CSharpStyleGuard reported {totalDiagnosticCount} diagnostic(s).");

        if (commandLineOptions.FixFiles && totalUnsafeFixCount > 0) return 1;
        return totalDiagnosticCount == 0 || commandLineOptions.FixFiles && totalUnsafeFixCount == 0 ? 0 : 1;
    }

    private static void WriteUsage()
    {
        Console.WriteLine("Usage: CSharpStyleGuard (--check|--fix [--all]) <file-or-directory> [more paths]");
        Console.WriteLine("Checks or safely rewrites multiline ternary conditional expressions, logical/null-coalescing binary expressions, and parameter/argument lists so they stay on one physical line. Lines over 220 characters are allowed for these rules.");
        Console.WriteLine("--fix rewrites only spans that intersect staged or unstaged git diff lines by default. Use --fix --all to rewrite every matching span in the input paths.");
        Console.WriteLine("When --fix runs outside a git repository, it rewrites every matching span in the input paths and reports a warning.");
    }

    private static IEnumerable<string> EnumerateSourceFilePaths(IReadOnlyList<string> inputPaths)
    {
        foreach (var inputPath in inputPaths)
        {
            var fullPath = Path.GetFullPath(inputPath);
            if (File.Exists(fullPath) && Path.GetExtension(fullPath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                yield return fullPath;
                continue;
            }

            if (!Directory.Exists(fullPath)) continue;

            foreach (var sourceFilePath in Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories))
            {
                if (IsExcludedSourceFilePath(sourceFilePath)) continue;
                yield return sourceFilePath;
            }
        }
    }

    private static bool IsExcludedSourceFilePath(string sourceFilePath)
    {
        var directoryInfo = Directory.GetParent(sourceFilePath);
        while (directoryInfo is not null)
        {
            if (s_excludedDirectoryNames.Contains(directoryInfo.Name)) return true;
            directoryInfo = directoryInfo.Parent;
        }

        return false;
    }

    private static FileResult ProcessSourceFile(string sourceFilePath, bool fixFile, IReadOnlyList<LineRange>? changedLineRanges)
    {
        var sourceText = SourceText.From(File.ReadAllText(sourceFilePath));
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse, SourceCodeKind.Regular), sourceFilePath);
        var syntaxRoot = syntaxTree.GetRoot();
        var ruleWalker = new SingleLineRuleWalker(sourceText, sourceFilePath);
        ruleWalker.Visit(syntaxRoot);
        var diagnostics = SelectDiagnosticsInChangedLineRanges(ruleWalker.Diagnostics, sourceText, changedLineRanges);

        if (!fixFile || diagnostics.Count == 0) return new FileResult(diagnostics, false, 0);

        var safeFixes = diagnostics.Where(styleDiagnostic => styleDiagnostic.CanFixAutomatically).OrderBy(styleDiagnostic => styleDiagnostic.Span.Start).ThenByDescending(styleDiagnostic => styleDiagnostic.Span.Length).ToList();
        var selectedFixes = SelectNonOverlappingFixes(safeFixes);
        var textChanges = selectedFixes.Select(styleDiagnostic => new TextChange(styleDiagnostic.Span, CreateSingleLineText(styleDiagnostic.Node))).ToImmutableArray();
        var unsafeFixCount = diagnostics.Count(styleDiagnostic => !styleDiagnostic.CanFixAutomatically);

        if (textChanges.Length == 0) return new FileResult(diagnostics, false, unsafeFixCount);

        var changedSourceText = sourceText.WithChanges(textChanges);
        File.WriteAllText(sourceFilePath, changedSourceText.ToString());
        return new FileResult(diagnostics, true, unsafeFixCount);
    }

    private static List<StyleDiagnostic> SelectDiagnosticsInChangedLineRanges(IReadOnlyList<StyleDiagnostic> diagnostics, SourceText sourceText, IReadOnlyList<LineRange>? changedLineRanges)
    {
        if (changedLineRanges is null) return [.. diagnostics];
        if (changedLineRanges.Count == 0) return [];

        return diagnostics.Where(styleDiagnostic => DiagnosticIntersectsChangedLineRanges(styleDiagnostic, sourceText, changedLineRanges)).ToList();
    }

    private static bool DiagnosticIntersectsChangedLineRanges(StyleDiagnostic styleDiagnostic, SourceText sourceText, IReadOnlyList<LineRange> changedLineRanges)
    {
        var linePositionSpan = sourceText.Lines.GetLinePositionSpan(styleDiagnostic.Span);
        var diagnosticStartLine = linePositionSpan.Start.Line + 1;
        var diagnosticEndLine = linePositionSpan.End.Line + 1;
        return changedLineRanges.Any(changedLineRange => changedLineRange.Intersects(diagnosticStartLine, diagnosticEndLine));
    }

    private static List<StyleDiagnostic> SelectNonOverlappingFixes(IReadOnlyList<StyleDiagnostic> safeFixes)
    {
        var selectedFixes = new List<StyleDiagnostic>();
        var occupiedSpans = new List<TextSpan>();

        foreach (var styleDiagnostic in safeFixes)
        {
            if (occupiedSpans.Any(occupiedSpan => occupiedSpan.OverlapsWith(styleDiagnostic.Span))) continue;
            selectedFixes.Add(styleDiagnostic);
            occupiedSpans.Add(styleDiagnostic.Span);
        }

        return selectedFixes;
    }

    private static string CreateSingleLineText(SyntaxNode syntaxNode)
    {
        var normalizedNode = syntaxNode.NormalizeWhitespace(indentation: string.Empty, eol: " ", elasticTrivia: false);
        return normalizedNode.ToString();
    }

    internal static void AddGitDiffLineRanges(string repositoryRootPath, string gitDiffOutput, ISet<string> sourceFilePathSet, Dictionary<string, List<LineRange>> changedLineRangesByFilePath)
    {
        string? currentSourceFilePath = null;

        foreach (var outputLine in EnumerateLines(gitDiffOutput))
        {
            if (outputLine.StartsWith("+++ ", StringComparison.Ordinal))
            {
                currentSourceFilePath = TryGetSourceFilePathFromGitDiffOutputLine(repositoryRootPath, outputLine);
                if (currentSourceFilePath is not null && !sourceFilePathSet.Contains(currentSourceFilePath)) currentSourceFilePath = null;
                continue;
            }

            if (currentSourceFilePath is null) continue;

            var hunkHeaderMatch = s_gitDiffHunkHeaderRegex.Match(outputLine);
            if (!hunkHeaderMatch.Success) continue;

            var startLine = int.Parse(hunkHeaderMatch.Groups[1].Value);
            var lineCount = hunkHeaderMatch.Groups[2].Success ? int.Parse(hunkHeaderMatch.Groups[2].Value) : 1;
            if (lineCount == 0) continue;

            AddLineRange(changedLineRangesByFilePath, currentSourceFilePath, new LineRange(startLine, startLine + lineCount - 1));
        }
    }

    private static void AddLineRange(Dictionary<string, List<LineRange>> changedLineRangesByFilePath, string sourceFilePath, LineRange lineRange)
    {
        if (!changedLineRangesByFilePath.TryGetValue(sourceFilePath, out var lineRanges))
        {
            lineRanges = [];
            changedLineRangesByFilePath.Add(sourceFilePath, lineRanges);
        }

        lineRanges.Add(lineRange);
    }

    private static string? TryGetSourceFilePathFromGitDiffOutputLine(string repositoryRootPath, string outputLine)
    {
        var gitOutputPath = outputLine["+++ ".Length..];
        if (gitOutputPath == "/dev/null") return null;
        if (gitOutputPath.StartsWith("b/", StringComparison.Ordinal)) gitOutputPath = gitOutputPath["b/".Length..];
        return Path.GetFullPath(Path.Combine(repositoryRootPath, gitOutputPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    internal static string? TryFindRepositoryRootPath(string sourceFilePath)
    {
        var directoryInfo = Directory.GetParent(sourceFilePath);
        while (directoryInfo is not null)
        {
            var gitMetadataPath = Path.Combine(directoryInfo.FullName, ".git");
            if (Directory.Exists(gitMetadataPath) || File.Exists(gitMetadataPath)) return directoryInfo.FullName;
            directoryInfo = directoryInfo.Parent;
        }

        return null;
    }

    internal static GitCommandResult RunGit(string repositoryRootPath, params string[] gitArguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "git";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.ArgumentList.Add("-C");
            process.StartInfo.ArgumentList.Add(repositoryRootPath);
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("core.quotePath=false");

            foreach (var gitArgument in gitArguments) process.StartInfo.ArgumentList.Add(gitArgument);

            process.Start();
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return new GitCommandResult(process.ExitCode, standardOutputTask.GetAwaiter().GetResult(), standardErrorTask.GetAwaiter().GetResult());
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new GitCommandResult(-1, string.Empty, exception.Message);
        }
    }

    internal static IEnumerable<string> EnumerateLines(string text)
    {
        using var stringReader = new StringReader(text);
        while (stringReader.ReadLine() is { } line) yield return line;
    }
}

internal sealed class SingleLineRuleWalker(SourceText sourceText, string sourceFilePath) : CSharpSyntaxWalker
{
    private readonly SourceText _sourceText = sourceText;
    private readonly string _sourceFilePath = sourceFilePath;

    public List<StyleDiagnostic> Diagnostics { get; } = [];

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        ReportIfMultiline(node, "CSG0001", "Ternary conditional expressions must stay on one physical line; lines over 220 characters are allowed for this rule.");
        base.VisitConditionalExpression(node);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        var diagnosticMessage = "Logical AND, logical OR, and null-coalescing expressions must stay on one physical line; lines over 220 characters are allowed for this rule.";
        if (IsSingleLineBinaryExpression(node) && !IsNestedSingleLineBinaryExpression(node)) ReportIfMultiline(node, "CSG0003", diagnosticMessage);

        base.VisitBinaryExpression(node);
    }

    public override void VisitArgumentList(ArgumentListSyntax node)
    {
        ReportIfMultiline(node, "CSG0002", "Parameter and argument lists must stay on one physical line; lines over 220 characters are allowed for this rule.");
        base.VisitArgumentList(node);
    }

    public override void VisitParameterList(ParameterListSyntax node)
    {
        ReportIfMultiline(node, "CSG0002", "Parameter and argument lists must stay on one physical line; declarations must remain one physical line even over 220 characters.");
        base.VisitParameterList(node);
    }

    private void ReportIfMultiline(SyntaxNode node, string diagnosticId, string message)
    {
        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(node.Span);
        if (linePositionSpan.Start.Line == linePositionSpan.End.Line) return;

        var canFixAutomatically = CanRewriteAutomatically(node);
        var fullMessage = canFixAutomatically ? message : $"{message} Automatic rewriting was skipped because the span contains comments, directives, disabled text, or multiline braced syntax.";
        Diagnostics.Add(new StyleDiagnostic(_sourceFilePath, node.Span, linePositionSpan.Start.Line + 1, linePositionSpan.Start.Character + 1, diagnosticId, fullMessage, canFixAutomatically, node));
    }

    private bool CanRewriteAutomatically(SyntaxNode node) => !ContainsUnsafeTrivia(node) && !ContainsMultilineBracedSyntax(node);

    private static bool IsNestedSingleLineBinaryExpression(BinaryExpressionSyntax node) => node.Parent is BinaryExpressionSyntax parentNode && IsSingleLineBinaryExpression(parentNode);

    private static bool IsSingleLineBinaryExpression(BinaryExpressionSyntax node) => node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression) || node.IsKind(SyntaxKind.CoalesceExpression);

    private bool ContainsMultilineBracedSyntax(SyntaxNode node)
    {
        foreach (var descendantNode in node.DescendantNodes(descendIntoTrivia: false))
        {
            if (!IsBracedSyntax(descendantNode)) continue;
            var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(descendantNode.Span);
            if (linePositionSpan.Start.Line != linePositionSpan.End.Line) return true;
        }

        return false;
    }

    private static bool IsBracedSyntax(SyntaxNode node) => node is BlockSyntax or InitializerExpressionSyntax or AnonymousObjectCreationExpressionSyntax or SwitchExpressionSyntax;

    private static bool ContainsUnsafeTrivia(SyntaxNode node)
    {
        foreach (var trivia in node.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.IsDirective) return true;
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)) return true;
            if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)) return true;
            if (trivia.IsKind(SyntaxKind.DisabledTextTrivia)) return true;
        }

        return false;
    }
}

internal sealed class FixPlan(bool fixEveryFile, ImmutableHashSet<string> fullFileFixPaths, ImmutableDictionary<string, ImmutableArray<LineRange>> changedLineRangesByFilePath, ImmutableArray<string> warningMessages)
{
    private readonly bool _fixEveryFile = fixEveryFile;
    private readonly ImmutableHashSet<string> _fullFileFixPaths = fullFileFixPaths;
    private readonly ImmutableDictionary<string, ImmutableArray<LineRange>> _changedLineRangesByFilePath = changedLineRangesByFilePath;

    public ImmutableArray<string> WarningMessages { get; } = warningMessages;

    public static FixPlan CheckOnly() => new(true, ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase), ImmutableDictionary<string, ImmutableArray<LineRange>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase), ImmutableArray<string>.Empty);

    public static FixPlan Create(IReadOnlyList<string> sourceFilePaths, bool fixAllFiles)
    {
        if (fixAllFiles) return CheckOnly();

        var sourceFilePathSet = sourceFilePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fullFileFixPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warningMessages = new List<string>();
        var sourceFilePathsByRepositoryRootPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceFilePath in sourceFilePaths)
        {
            var repositoryRootPath = Program.TryFindRepositoryRootPath(sourceFilePath);
            if (repositoryRootPath is null)
            {
                fullFileFixPaths.Add(sourceFilePath);
                continue;
            }

            if (!sourceFilePathsByRepositoryRootPath.TryGetValue(repositoryRootPath, out var repositorySourceFilePaths))
            {
                repositorySourceFilePaths = [];
                sourceFilePathsByRepositoryRootPath.Add(repositoryRootPath, repositorySourceFilePaths);
            }

            repositorySourceFilePaths.Add(sourceFilePath);
        }

        if (fullFileFixPaths.Count == sourceFilePaths.Count && sourceFilePaths.Count > 0) warningMessages.Add("Git repository was not found; --fix processed all input files fully.");
        else if (fullFileFixPaths.Count > 0) warningMessages.Add($"Git repository was not found for {fullFileFixPaths.Count} input file(s); --fix processed those file(s) fully.");

        var changedLineRangesByFilePath = new Dictionary<string, List<LineRange>>(StringComparer.OrdinalIgnoreCase);

        foreach (var repositorySourceFilePathsByRootPath in sourceFilePathsByRepositoryRootPath)
        {
            var repositoryRootPath = repositorySourceFilePathsByRootPath.Key;
            var repositorySourceFilePathSet = repositorySourceFilePathsByRootPath.Value.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unstagedGitDiffResult = Program.RunGit(repositoryRootPath, "diff", "--unified=0", "--no-ext-diff", "--");
            var stagedGitDiffResult = Program.RunGit(repositoryRootPath, "diff", "--cached", "--unified=0", "--no-ext-diff", "--");
            var untrackedSourceFileResult = Program.RunGit(repositoryRootPath, "ls-files", "--others", "--exclude-standard", "--");

            if (!unstagedGitDiffResult.Succeeded || !stagedGitDiffResult.Succeeded || !untrackedSourceFileResult.Succeeded)
            {
                foreach (var sourceFilePath in repositorySourceFilePathsByRootPath.Value) fullFileFixPaths.Add(sourceFilePath);
                warningMessages.Add($"Unable to read git status for {repositoryRootPath}; --fix processed {repositorySourceFilePathsByRootPath.Value.Count} file(s) fully.");
                continue;
            }

            Program.AddGitDiffLineRanges(repositoryRootPath, unstagedGitDiffResult.StandardOutput, sourceFilePathSet, changedLineRangesByFilePath);
            Program.AddGitDiffLineRanges(repositoryRootPath, stagedGitDiffResult.StandardOutput, sourceFilePathSet, changedLineRangesByFilePath);

            foreach (var untrackedSourceFileRelativePath in Program.EnumerateLines(untrackedSourceFileResult.StandardOutput))
            {
                var untrackedSourceFilePath = Path.GetFullPath(Path.Combine(repositoryRootPath, untrackedSourceFileRelativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (repositorySourceFilePathSet.Contains(untrackedSourceFilePath)) fullFileFixPaths.Add(untrackedSourceFilePath);
            }
        }

        var immutableChangedLineRangesByFilePath = changedLineRangesByFilePath.ToImmutableDictionary(changedLineRangesByFilePathEntry => changedLineRangesByFilePathEntry.Key, changedLineRangesByFilePathEntry => MergeLineRanges(changedLineRangesByFilePathEntry.Value), StringComparer.OrdinalIgnoreCase);
        return new FixPlan(false, fullFileFixPaths.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase), immutableChangedLineRangesByFilePath, [.. warningMessages]);
    }

    public bool ShouldProcessFile(string sourceFilePath) => _fixEveryFile || _fullFileFixPaths.Contains(sourceFilePath) || _changedLineRangesByFilePath.ContainsKey(sourceFilePath);

    public IReadOnlyList<LineRange>? GetChangedLineRanges(string sourceFilePath)
    {
        if (_fixEveryFile || _fullFileFixPaths.Contains(sourceFilePath)) return null;
        if (_changedLineRangesByFilePath.TryGetValue(sourceFilePath, out var changedLineRanges)) return changedLineRanges;
        return [];
    }

    private static ImmutableArray<LineRange> MergeLineRanges(IReadOnlyList<LineRange> lineRanges)
    {
        var sortedLineRanges = lineRanges.OrderBy(lineRange => lineRange.StartLine).ThenBy(lineRange => lineRange.EndLine).ToList();
        if (sortedLineRanges.Count == 0) return [];

        var mergedLineRanges = new List<LineRange>();
        var currentLineRange = sortedLineRanges[0];

        for (var index = 1; index < sortedLineRanges.Count; index++)
        {
            var nextLineRange = sortedLineRanges[index];
            if (nextLineRange.StartLine <= currentLineRange.EndLine + 1)
            {
                currentLineRange = new LineRange(currentLineRange.StartLine, Math.Max(currentLineRange.EndLine, nextLineRange.EndLine));
                continue;
            }

            mergedLineRanges.Add(currentLineRange);
            currentLineRange = nextLineRange;
        }

        mergedLineRanges.Add(currentLineRange);
        return [.. mergedLineRanges];
    }
}

internal sealed record CommandLineOptions(bool FixFiles, bool FixAllFiles, bool ShowHelp, string? ErrorMessage, IReadOnlyList<string> InputPaths)
{
    public static CommandLineOptions Parse(IReadOnlyList<string> commandLineArguments)
    {
        var fixFiles = false;
        var fixAllFiles = false;
        var checkFiles = false;
        var showHelp = false;
        var inputPaths = new List<string>();

        foreach (var commandLineArgument in commandLineArguments)
        {
            switch (commandLineArgument)
            {
                case "--fix":
                    fixFiles = true;
                    break;
                case "--all":
                    fixAllFiles = true;
                    break;
                case "--check":
                    checkFiles = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    inputPaths.Add(commandLineArgument);
                    break;
            }
        }

        if (showHelp) return new CommandLineOptions(false, false, true, null, inputPaths);
        if (fixFiles == checkFiles) return new CommandLineOptions(false, false, false, "Specify exactly one mode: --check or --fix.", inputPaths);
        if (fixAllFiles && !fixFiles) return new CommandLineOptions(false, false, false, "Specify --all only with --fix.", inputPaths);
        if (inputPaths.Count == 0) return new CommandLineOptions(fixFiles, fixAllFiles, false, "Specify at least one file or directory.", inputPaths);
        return new CommandLineOptions(fixFiles, fixAllFiles, false, null, inputPaths);
    }
}

internal readonly record struct LineRange(int StartLine, int EndLine)
{
    public bool Intersects(int startLine, int endLine) => StartLine <= endLine && startLine <= EndLine;
}

internal sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

internal sealed record StyleDiagnostic(string SourceFilePath, TextSpan Span, int LineNumber, int CharacterNumber, string DiagnosticId, string Message, bool CanFixAutomatically, SyntaxNode Node)
{
    public string ToDisplayString() => $"{SourceFilePath}({LineNumber},{CharacterNumber}): {DiagnosticId} {Message}";
}

internal sealed record FileResult(IReadOnlyList<StyleDiagnostic> Diagnostics, bool Modified, int UnsafeFixCount);
