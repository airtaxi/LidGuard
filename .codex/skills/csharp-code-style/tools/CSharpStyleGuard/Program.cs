using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class Program
{
    private static readonly HashSet<string> s_excludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "bin", "obj" };

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
        if (sourceFilePaths.Length == 0)
        {
            Console.Error.WriteLine("No C# source files were found.");
            return 2;
        }

        var totalDiagnosticCount = 0;
        var totalModifiedCount = 0;
        var totalUnsafeFixCount = 0;

        foreach (var sourceFilePath in sourceFilePaths)
        {
            var fileResult = ProcessSourceFile(sourceFilePath, commandLineOptions.FixFiles);
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
        Console.WriteLine("Usage: CSharpStyleGuard (--check|--fix) <file-or-directory> [more paths]");
        Console.WriteLine("Checks or safely rewrites multiline ternary conditional expressions, logical/null-coalescing binary expressions, and parameter/argument lists so they stay on one physical line. Lines over 220 characters are allowed for these rules.");
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

    private static FileResult ProcessSourceFile(string sourceFilePath, bool fixFile)
    {
        var sourceText = SourceText.From(File.ReadAllText(sourceFilePath));
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse, SourceCodeKind.Regular), sourceFilePath);
        var syntaxRoot = syntaxTree.GetRoot();
        var ruleWalker = new SingleLineRuleWalker(sourceText, sourceFilePath);
        ruleWalker.Visit(syntaxRoot);

        if (!fixFile || ruleWalker.Diagnostics.Count == 0) return new FileResult(ruleWalker.Diagnostics, false, 0);

        var safeFixes = ruleWalker.Diagnostics.Where(styleDiagnostic => styleDiagnostic.CanFixAutomatically).OrderBy(styleDiagnostic => styleDiagnostic.Span.Start).ThenByDescending(styleDiagnostic => styleDiagnostic.Span.Length).ToList();
        var selectedFixes = SelectNonOverlappingFixes(safeFixes);
        var textChanges = selectedFixes.Select(styleDiagnostic => new TextChange(styleDiagnostic.Span, CreateSingleLineText(styleDiagnostic.Node))).ToImmutableArray();
        var unsafeFixCount = ruleWalker.Diagnostics.Count(styleDiagnostic => !styleDiagnostic.CanFixAutomatically);

        if (textChanges.Length == 0) return new FileResult(ruleWalker.Diagnostics, false, unsafeFixCount);

        var changedSourceText = sourceText.WithChanges(textChanges);
        File.WriteAllText(sourceFilePath, changedSourceText.ToString());
        return new FileResult(ruleWalker.Diagnostics, true, unsafeFixCount);
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

internal sealed record CommandLineOptions(bool FixFiles, bool ShowHelp, string? ErrorMessage, IReadOnlyList<string> InputPaths)
{
    public static CommandLineOptions Parse(IReadOnlyList<string> commandLineArguments)
    {
        var fixFiles = false;
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

        if (showHelp) return new CommandLineOptions(false, true, null, inputPaths);
        if (fixFiles == checkFiles) return new CommandLineOptions(false, false, "Specify exactly one mode: --check or --fix.", inputPaths);
        if (inputPaths.Count == 0) return new CommandLineOptions(fixFiles, false, "Specify at least one file or directory.", inputPaths);
        return new CommandLineOptions(fixFiles, false, null, inputPaths);
    }
}

internal sealed record StyleDiagnostic(string SourceFilePath, TextSpan Span, int LineNumber, int CharacterNumber, string DiagnosticId, string Message, bool CanFixAutomatically, SyntaxNode Node)
{
    public string ToDisplayString() => $"{SourceFilePath}({LineNumber},{CharacterNumber}): {DiagnosticId} {Message}";
}

internal sealed record FileResult(IReadOnlyList<StyleDiagnostic> Diagnostics, bool Modified, int UnsafeFixCount);
