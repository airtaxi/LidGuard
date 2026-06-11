using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class Program
{
    internal const int LineLengthThreshold = 320;

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
        Console.WriteLine($"Checks or safely rewrites multiline ternary conditional expressions, logical/null-coalescing binary expressions, parameter/argument lists, collection-expression keyword spacing, object-creation argument-list spacing, top-level single-statement control flow, nested control-statement blocks, constructor initializers, expression-bodied members, and single-statement try/catch/finally blocks. The line-length threshold for newly compressed lines is {LineLengthThreshold} characters.");
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
        var diagnostics = AnalyzeSourceText(sourceText, sourceFilePath, changedLineRanges);
        if (!fixFile || diagnostics.Count == 0) return new FileResult(diagnostics, false, 0);

        var allDiagnostics = new List<StyleDiagnostic>();
        var totalUnsafeFixCount = 0;
        var modified = false;

        for (var fixPassIndex = 0; fixPassIndex < 8; fixPassIndex++)
        {
            diagnostics = AnalyzeSourceText(sourceText, sourceFilePath, changedLineRanges);
            if (diagnostics.Count == 0)
            {
                if (modified) File.WriteAllText(sourceFilePath, sourceText.ToString());

                return new FileResult(allDiagnostics, modified, totalUnsafeFixCount);
            }

            allDiagnostics.AddRange(diagnostics);
            totalUnsafeFixCount += diagnostics.Count(styleDiagnostic => !styleDiagnostic.CanFixAutomatically);

            var safeFixes = diagnostics.Where(styleDiagnostic => styleDiagnostic.CanFixAutomatically).OrderBy(styleDiagnostic => styleDiagnostic.Span.Start).ThenByDescending(styleDiagnostic => styleDiagnostic.Span.Length).ToList();
            var selectedFixes = SelectNonOverlappingFixes(safeFixes);
            var textChanges = selectedFixes.Select(styleDiagnostic => new TextChange(styleDiagnostic.Span, styleDiagnostic.ReplacementText ?? CreateSingleLineText(styleDiagnostic.Node))).ToImmutableArray();
            if (textChanges.Length == 0)
            {
                if (modified) File.WriteAllText(sourceFilePath, sourceText.ToString());

                return new FileResult(allDiagnostics, modified, totalUnsafeFixCount);
            }

            sourceText = sourceText.WithChanges(textChanges);
            modified = true;
        }

        var remainingDiagnostics = AnalyzeSourceText(sourceText, sourceFilePath, changedLineRanges);
        allDiagnostics.AddRange(remainingDiagnostics);
        totalUnsafeFixCount += remainingDiagnostics.Count;
        if (modified) File.WriteAllText(sourceFilePath, sourceText.ToString());
        return new FileResult(allDiagnostics, modified, totalUnsafeFixCount);
    }

    private static List<StyleDiagnostic> AnalyzeSourceText(SourceText sourceText, string sourceFilePath, IReadOnlyList<LineRange>? changedLineRanges)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse, SourceCodeKind.Regular), sourceFilePath);
        var syntaxRoot = syntaxTree.GetRoot();
        var ruleWalker = new SingleLineRuleWalker(sourceText, sourceFilePath);
        ruleWalker.Visit(syntaxRoot);
        return SelectDiagnosticsInChangedLineRanges(ruleWalker.Diagnostics, sourceText, changedLineRanges);
    }

    private static List<StyleDiagnostic> SelectDiagnosticsInChangedLineRanges(IReadOnlyList<StyleDiagnostic> diagnostics, SourceText sourceText, IReadOnlyList<LineRange>? changedLineRanges)
    {
        if (changedLineRanges is null) return [..diagnostics];
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

    internal static string CreateSingleLineText(SyntaxNode syntaxNode)
    {
        var normalizedNode = syntaxNode.NormalizeWhitespace(indentation: string.Empty, eol: " ", elasticTrivia: false);
        var spacedNode = ApplySpacingRewriters(normalizedNode);
        return spacedNode?.ToString() ?? normalizedNode.ToString();
    }

    internal static SyntaxNode? ApplySpacingRewriters(SyntaxNode syntaxNode)
    {
        var spacedNode = IsPatternSpacingRewriter.Shared.Visit(syntaxNode);
        spacedNode = RelationalPatternSpacingRewriter.Shared.Visit(spacedNode);
        spacedNode = CatchFilterSpacingRewriter.Shared.Visit(spacedNode);
        spacedNode = SwitchExpressionSpacingRewriter.Shared.Visit(spacedNode);
        spacedNode = CollectionExpressionSpacingRewriter.Shared.Visit(spacedNode);
        spacedNode = ObjectCreationArgumentListSpacingRewriter.Shared.Visit(spacedNode);
        return spacedNode;
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
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { return new GitCommandResult(-1, string.Empty, exception.Message); }
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
    private readonly string _endOfLine = GetEndOfLine(sourceText);

    public List<StyleDiagnostic> Diagnostics { get; } = [];

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.Body is not null) ReportMemberNestedBlockExpansionIfNeeded(node, node.Body);
        if (node.ExpressionBody is not null) ReportExpressionBodiedMemberIfNeeded(node, node.ExpressionBody, node.SemicolonToken);
        base.VisitMethodDeclaration(node);
    }

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        if (node.Body is not null) ReportMemberNestedBlockExpansionIfNeeded(node, node.Body);
        if (node.ExpressionBody is not null) ReportExpressionBodiedMemberIfNeeded(node, node.ExpressionBody, node.SemicolonToken);
        base.VisitLocalFunctionStatement(node);
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        if (node.Body is not null) ReportConstructorInitializerIfNeeded(node);
        if (node.Body is not null) ReportMemberNestedBlockExpansionIfNeeded(node, node.Body);
        if (node.ExpressionBody is not null) ReportExpressionBodiedMemberIfNeeded(node, node.ExpressionBody, node.SemicolonToken);
        base.VisitConstructorDeclaration(node);
    }

    public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node)
    {
        if (node.Body is not null) ReportMemberNestedBlockExpansionIfNeeded(node, node.Body);
        if (node.ExpressionBody is not null) ReportExpressionBodiedMemberIfNeeded(node, node.ExpressionBody, node.SemicolonToken);
        base.VisitDestructorDeclaration(node);
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        if (node.ExpressionBody is not null) ReportExpressionBodiedMemberIfNeeded(node, node.ExpressionBody, node.SemicolonToken);
        base.VisitPropertyDeclaration(node);
    }

    public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
    {
        if (node.ExpressionBody is not null) ReportExpressionBodiedMemberIfNeeded(node, node.ExpressionBody, node.SemicolonToken);
        base.VisitIndexerDeclaration(node);
    }

    public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        if (node.Body is not null) ReportMemberNestedBlockExpansionIfNeeded(node, node.Body);
        if (node.ExpressionBody is not null) ReportExpressionBodiedMemberIfNeeded(node, node.ExpressionBody, node.SemicolonToken);
        base.VisitOperatorDeclaration(node);
    }

    public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        if (node.Body is not null) ReportMemberNestedBlockExpansionIfNeeded(node, node.Body);
        if (node.ExpressionBody is not null) ReportExpressionBodiedMemberIfNeeded(node, node.ExpressionBody, node.SemicolonToken);
        base.VisitConversionOperatorDeclaration(node);
    }

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        ReportIfMultiline(node, "CSG0001", $"Ternary conditional expressions must stay on one physical line; lines over {Program.LineLengthThreshold} characters are allowed for this rule.");
        base.VisitConditionalExpression(node);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        var diagnosticMessage = $"Logical AND, logical OR, and null-coalescing expressions must stay on one physical line; lines over {Program.LineLengthThreshold} characters are allowed for this rule.";
        if (IsSingleLineBinaryExpression(node) && !IsNestedSingleLineBinaryExpression(node)) ReportIfMultiline(node, "CSG0003", diagnosticMessage);

        base.VisitBinaryExpression(node);
    }

    public override void VisitIsPatternExpression(IsPatternExpressionSyntax node)
    {
        ReportIsPatternSpacingIfNeeded(node);
        base.VisitIsPatternExpression(node);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        ReportObjectCreationArgumentListSpacingIfNeeded(node);
        base.VisitObjectCreationExpression(node);
    }

    public override void VisitReturnStatement(ReturnStatementSyntax node)
    {
        ReportCollectionExpressionKeywordSpacingIfNeeded(node, node.ReturnKeyword, node.Expression);
        base.VisitReturnStatement(node);
    }

    public override void VisitYieldStatement(YieldStatementSyntax node)
    {
        ReportCollectionExpressionKeywordSpacingIfNeeded(node, node.ReturnOrBreakKeyword, node.Expression);
        base.VisitYieldStatement(node);
    }

    public override void VisitArgumentList(ArgumentListSyntax node)
    {
        ReportParameterOrArgumentListIfNeeded(node, $"Simple parameter and argument lists must stay on one physical line; lines over {Program.LineLengthThreshold} characters are allowed for this rule.");
        base.VisitArgumentList(node);
    }

    public override void VisitParameterList(ParameterListSyntax node)
    {
        ReportParameterOrArgumentListIfNeeded(node, $"Simple parameter and argument lists must stay on one physical line; declarations must remain one physical line even over {Program.LineLengthThreshold} characters.");
        base.VisitParameterList(node);
    }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        ReportNestedControlStatementIfNeeded(node);
        ReportIfChainDirectControlBranchExpansionIfNeeded(node);
        ReportNestedBlockExpansionIfNeeded(node, node.Statement, node.Span, true);
        ReportSingleStatementIfNeeded(node);
        ReportSingleLineElseClauseIfNeeded(node);
        base.VisitIfStatement(node);
    }

    public override void VisitForStatement(ForStatementSyntax node)
    {
        ReportNestedControlStatementIfNeeded(node);
        ReportNestedBlockExpansionIfNeeded(node, node.Statement, node.Span, true);
        ReportSingleStatementControlIfNeeded(node, node.Statement, statement => node.WithStatement(statement));
        base.VisitForStatement(node);
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        ReportNestedControlStatementIfNeeded(node);
        ReportNestedBlockExpansionIfNeeded(node, node.Statement, node.Span, true);
        ReportSingleStatementControlIfNeeded(node, node.Statement, statement => node.WithStatement(statement));
        base.VisitForEachStatement(node);
    }

    public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        ReportNestedControlStatementIfNeeded(node);
        ReportNestedBlockExpansionIfNeeded(node, node.Statement, node.Span, true);
        ReportSingleStatementControlIfNeeded(node, node.Statement, statement => node.WithStatement(statement));
        base.VisitForEachVariableStatement(node);
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        ReportNestedControlStatementIfNeeded(node);
        ReportNestedBlockExpansionIfNeeded(node, node.Statement, node.Span, true);
        ReportSingleStatementControlIfNeeded(node, node.Statement, statement => node.WithStatement(statement));
        base.VisitWhileStatement(node);
    }

    public override void VisitDoStatement(DoStatementSyntax node)
    {
        ReportNestedControlStatementIfNeeded(node);
        ReportNestedBlockExpansionIfNeeded(node, node.Statement, node.Span, true);
        ReportSingleStatementControlIfNeeded(node, node.Statement, statement => node.WithStatement(statement));
        base.VisitDoStatement(node);
    }

    public override void VisitUsingStatement(UsingStatementSyntax node)
    {
        ReportNestedControlStatementIfNeeded(node);
        ReportNestedBlockExpansionIfNeeded(node, node.Statement, node.Span, true);
        ReportSingleStatementControlIfNeeded(node, node.Statement, statement => node.WithStatement(statement));
        base.VisitUsingStatement(node);
    }

    public override void VisitLockStatement(LockStatementSyntax node)
    {
        ReportNestedControlStatementIfNeeded(node);
        ReportNestedBlockExpansionIfNeeded(node, node.Statement, node.Span, true);
        ReportSingleStatementControlIfNeeded(node, node.Statement, statement => node.WithStatement(statement));
        base.VisitLockStatement(node);
    }

    public override void VisitFixedStatement(FixedStatementSyntax node)
    {
        ReportNestedControlStatementIfNeeded(node);
        ReportNestedBlockExpansionIfNeeded(node, node.Statement, node.Span, true);
        ReportSingleStatementControlIfNeeded(node, node.Statement, statement => node.WithStatement(statement));
        base.VisitFixedStatement(node);
    }

    public override void VisitTryStatement(TryStatementSyntax node)
    {
        ReportTryBlockNestedExpansionIfNeeded(node);
        ReportTryBlockIfMultiline(node);
        foreach (var catchClause in node.Catches)
        {
            ReportNestedBlockExpansionIfNeeded(catchClause, catchClause.Block, catchClause.Span, false);
            ReportExceptionHandlingClauseIfMultiline(catchClause, catchClause.Block, catchClause.Span, Program.CreateSingleLineText(catchClause));
        }

        if (node.Finally is not null)
        {
            ReportNestedBlockExpansionIfNeeded(node.Finally, node.Finally.Block, node.Finally.Span, false);
            ReportExceptionHandlingClauseIfMultiline(node.Finally, node.Finally.Block, node.Finally.Span, Program.CreateSingleLineText(node.Finally));
        }

        base.VisitTryStatement(node);
    }

    private void ReportIfMultiline(SyntaxNode node, string diagnosticId, string message)
    {
        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(node.Span);
        if (linePositionSpan.Start.Line == linePositionSpan.End.Line) return;
        if (ContainsMultilineSwitchExpression(node)) return;

        var canFixAutomatically = CanRewriteAutomatically(node);
        var fullMessage = canFixAutomatically ? message : $"{message} Automatic rewriting was skipped because the span contains comments, directives, disabled text, or multiline braced syntax.";
        Diagnostics.Add(new StyleDiagnostic(_sourceFilePath, node.Span, linePositionSpan.Start.Line + 1, linePositionSpan.Start.Character + 1, diagnosticId, fullMessage, canFixAutomatically, node));
    }

    private void ReportParameterOrArgumentListIfNeeded(SyntaxNode node, string message)
    {
        var replacementText = TryCreateParameterOrArgumentListReplacementText(node);
        if (replacementText is null) return;

        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(node.Span);
        Diagnostics.Add(new StyleDiagnostic(_sourceFilePath, node.Span, linePositionSpan.Start.Line + 1, linePositionSpan.Start.Character + 1, "CSG0002", message, true, node, replacementText));
    }

    private string? TryCreateParameterOrArgumentListReplacementText(SyntaxNode node)
    {
        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(node.Span);
        if (linePositionSpan.Start.Line == linePositionSpan.End.Line) return null;
        if (ContainsMultilineSwitchExpression(node)) return null;
        if (ContainsUnsafeTrivia(node)) return null;

        var rewrittenNode = SimpleLambdaBlockExpressionRewriter.Shared.Visit(node) ?? node;
        if (ContainsMultilineBracedSyntax(rewrittenNode)) return null;

        var replacementText = Program.CreateSingleLineText(rewrittenNode);
        if (replacementText.Contains('\r') || replacementText.Contains('\n')) return null;
        if (replacementText == _sourceText.ToString(node.Span)) return null;

        return replacementText;
    }

    private void ReportIsPatternSpacingIfNeeded(IsPatternExpressionSyntax node)
    {
        var isKeyword = node.IsKeyword;
        var expressionLastToken = node.Expression.GetLastToken();
        if (HasEndOfLineTrivia(expressionLastToken.TrailingTrivia) || HasEndOfLineTrivia(isKeyword.LeadingTrivia)) return;
        if (expressionLastToken.TrailingTrivia.ToFullString() + isKeyword.LeadingTrivia.ToFullString() == " ") return;

        var spacedNode = IsPatternSpacingRewriter.Shared.Visit(node) ?? node;
        var replacementText = spacedNode.ToString();
        if (replacementText == _sourceText.ToString(node.Span)) return;

        var canFixAutomatically = !ContainsUnsafeTrivia(node);
        var message = "Pattern matching is-expressions must use exactly one space before the is keyword.";
        var fullMessage = canFixAutomatically ? message : $"{message} Automatic rewriting was skipped because the span contains comments, directives, or disabled text.";
        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(node.Span);
        Diagnostics.Add(new StyleDiagnostic(_sourceFilePath, node.Span, linePositionSpan.Start.Line + 1, linePositionSpan.Start.Character + 1, "CSG0009", fullMessage, canFixAutomatically, node, replacementText));
    }

    private void ReportCollectionExpressionKeywordSpacingIfNeeded(SyntaxNode node, SyntaxToken keywordToken, ExpressionSyntax? expression)
    {
        if (expression is not CollectionExpressionSyntax) return;
        if (keywordToken.TrailingTrivia.Any(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia) || trivia.IsKind(SyntaxKind.EndOfLineTrivia))) return;

        var canFixAutomatically = !ContainsUnsafeTrivia(node);
        var message = "Collection expressions returned by return or yield return must have one space after the keyword.";
        var fullMessage = canFixAutomatically ? message : $"{message} Automatic rewriting was skipped because the span contains comments, directives, or disabled text.";
        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(keywordToken.Span);
        Diagnostics.Add(new StyleDiagnostic(_sourceFilePath, keywordToken.Span, linePositionSpan.Start.Line + 1, linePositionSpan.Start.Character + 1, "CSG0011", fullMessage, canFixAutomatically, node, keywordToken.Text + " "));
    }

    private void ReportObjectCreationArgumentListSpacingIfNeeded(ObjectCreationExpressionSyntax node)
    {
        if (node.ArgumentList is null) return;

        var typeLastToken = node.Type.GetLastToken();
        var openParenthesisToken = node.ArgumentList.OpenParenToken;
        if (typeLastToken.RawKind == 0 || openParenthesisToken.RawKind == 0) return;

        var spacingSpan = TextSpan.FromBounds(typeLastToken.Span.End, openParenthesisToken.SpanStart);
        if (spacingSpan.Length == 0) return;

        var spacingText = _sourceText.ToString(spacingSpan);
        if (spacingText.Contains('\r') || spacingText.Contains('\n') || !spacingText.All(char.IsWhiteSpace)) return;

        var message = "Object creation argument lists must not have whitespace between the type and opening parenthesis.";
        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(spacingSpan);
        Diagnostics.Add(new StyleDiagnostic(_sourceFilePath, spacingSpan, linePositionSpan.Start.Line + 1, linePositionSpan.Start.Character + 1, "CSG0012", message, true, node, string.Empty));
    }

    private void ReportTryBlockIfMultiline(TryStatementSyntax tryStatement)
    {
        var tryBlockSpan = TextSpan.FromBounds(tryStatement.TryKeyword.SpanStart, tryStatement.Block.Span.End);
        var tryOnlyStatement = tryStatement.WithCatches(default).WithFinally(null);
        ReportExceptionHandlingClauseIfMultiline(tryStatement, tryStatement.Block, tryBlockSpan, Program.CreateSingleLineText(tryOnlyStatement));
    }

    private void ReportExceptionHandlingClauseIfMultiline(SyntaxNode node, BlockSyntax block, TextSpan span, string replacementText)
    {
        if (block.Statements.Count > 1) return;
        if (block.Statements.Count == 1 && IsNestedControlOrBracedStatement(block.Statements[0])) return;

        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(span);
        if (linePositionSpan.Start.Line == linePositionSpan.End.Line) return;
        if (ContainsNestedMultilineBracedSyntax(node, block, span)) return;
        if (!FitsSinglePhysicalLine(span, replacementText)) return;

        var canFixAutomatically = CanRewriteExceptionHandlingBlockAutomatically(node, span);
        var message = $"Empty and simple single-statement try, catch, and finally blocks must stay on one physical line when the resulting line is {Program.LineLengthThreshold} characters or shorter.";
        var fullMessage = canFixAutomatically ? message : $"{message} Automatic rewriting was skipped because the span contains comments, directives, or disabled text.";
        Diagnostics.Add(new StyleDiagnostic(_sourceFilePath, span, linePositionSpan.Start.Line + 1, linePositionSpan.Start.Character + 1, "CSG0004", fullMessage, canFixAutomatically, node, replacementText));
    }

    private void ReportTryBlockNestedExpansionIfNeeded(TryStatementSyntax tryStatement)
    {
        var tryBlockSpan = TextSpan.FromBounds(tryStatement.TryKeyword.SpanStart, tryStatement.Block.Span.End);
        var tryOnlyStatement = tryStatement.WithCatches(default).WithFinally(null);
        ReportNestedBlockExpansionIfNeeded(tryOnlyStatement, tryStatement.Block, tryBlockSpan, false);
    }

    private void ReportSingleStatementIfNeeded(IfStatementSyntax node)
    {
        if (node.Parent is ElseClauseSyntax) return;
        if (ShouldExpandNestedControlStatement(node) && !ShouldFormatNestedIfChainAsSingleLine(node)) return;

        var replacementText = TryCreateIfStatementReplacementText(node);
        if (replacementText is null) return;

        ReportControlReplacementIfNeeded(node, node.Span, replacementText, "CSG0005", CreateSingleStatementControlMessage(), true);
    }

    private void ReportSingleLineElseClauseIfNeeded(IfStatementSyntax node)
    {
        if (node.Parent is ElseClauseSyntax) return;
        if (node.Else is null) return;
        if (node.Else.Statement is IfStatementSyntax) return;
        if (ShouldExpandNestedControlStatement(node) && !ShouldFormatNestedIfChainAsSingleLine(node)) return;

        var elseStatement = TryCreateSingleLineElseBranchStatement(node.Else.Statement);
        if (elseStatement is null) return;

        var replacementText = $"else {Program.CreateSingleLineText(elseStatement)}";
        ReportSingleLineReplacementIfNeeded(node.Else, node.Else.Span, replacementText, "CSG0005", CreateSingleStatementControlMessage(), true);
    }

    private void ReportSingleStatementControlIfNeeded<TNode>(TNode node, StatementSyntax statement, Func<StatementSyntax, TNode> createReplacementNode)
        where TNode : SyntaxNode
    {
        if (node is StatementSyntax statementNode && ShouldExpandNestedControlStatement(statementNode)) return;

        var bodyStatement = TryCreateSingleLineControlBodyStatement(statement);
        if (bodyStatement is null) return;

        var replacementNode = createReplacementNode(bodyStatement);
        var replacementText = Program.CreateSingleLineText(replacementNode);
        ReportSingleLineReplacementIfNeeded(node, node.Span, replacementText, "CSG0005", CreateSingleStatementControlMessage(), true);
    }

    private void ReportNestedControlStatementIfNeeded(StatementSyntax node)
    {
        if (node.Parent is ElseClauseSyntax) return;
        if (node is IfStatementSyntax ifStatement && ShouldFormatNestedIfChainAsSingleLine(ifStatement)) return;
        if (!ShouldExpandNestedControlStatement(node)) return;
        if (!ControlStatementNeedsBlockExpansion(node)) return;

        ReportNestedBlockExpansion(node, node.Span, "CSG0010", "A nested control statement that is the only statement inside a braced block must use expanded block formatting with braces.");
    }

    private void ReportIfChainDirectControlBranchExpansionIfNeeded(IfStatementSyntax ifStatement)
    {
        if (ifStatement.Parent is ElseClauseSyntax) return;
        if (!IfChainHasDirectControlBranch(ifStatement)) return;

        ReportNestedBlockExpansion(ifStatement, ifStatement.Span, "CSG0010", "If, else if, and else branches whose body is another control statement must use expanded block formatting with braces.");
    }

    private static string CreateSingleStatementControlMessage() => $"Top-level single-statement if, else if, else, for, foreach, while, do, lock, using, and fixed statements must omit braces and stay on one physical line when the resulting line is {Program.LineLengthThreshold} characters or shorter.";

    private static bool ShouldFormatNestedIfChainAsSingleLine(IfStatementSyntax ifStatement) => ifStatement.Else is not null && ShouldExpandNestedControlStatement(ifStatement);

    private static bool IfChainHasDirectControlBranch(IfStatementSyntax ifStatement)
    {
        var currentIfStatement = ifStatement;
        while (true)
        {
            if (IsDirectControlBranch(currentIfStatement.Statement)) return true;
            if (currentIfStatement.Else is null) return false;
            if (currentIfStatement.Else.Statement is IfStatementSyntax elseIfStatement)
            {
                currentIfStatement = elseIfStatement;
                continue;
            }

            return IsDirectControlBranch(currentIfStatement.Else.Statement);
        }
    }

    private static bool IsDirectControlBranch(StatementSyntax statement) => statement is not BlockSyntax && IsNestedControlOrBracedStatement(statement);

    private string? TryCreateIfStatementReplacementText(IfStatementSyntax ifStatement)
    {
        var outputLines = new List<string>();
        var currentIfStatement = ifStatement;
        var isFirstBranch = true;

        while (true)
        {
            var statement = TryCreateSingleLineIfBranchStatement(currentIfStatement.Statement);
            if (statement is null) return null;

            var branchText = Program.CreateSingleLineText(currentIfStatement.WithStatement(statement).WithElse(null));
            outputLines.Add(isFirstBranch ? branchText : $"else {branchText}");

            if (currentIfStatement.Else is null) break;
            if (currentIfStatement.Else.Statement is IfStatementSyntax elseIfStatement)
            {
                currentIfStatement = elseIfStatement;
                isFirstBranch = false;
                continue;
            }

            var elseStatement = TryCreateSingleLineElseBranchStatement(currentIfStatement.Else.Statement);
            if (elseStatement is null) return null;

            outputLines.Add($"else {Program.CreateSingleLineText(elseStatement)}");
            break;
        }

        var replacementText = string.Join(_endOfLine, outputLines);
        return IndentContinuationLines(replacementText, GetLineIndentation(ifStatement.SpanStart));
    }

    private static IfStatementSyntax? TryCreateSingleLineIfStatement(IfStatementSyntax ifStatement)
    {
        var statement = TryCreateSingleLineIfBranchStatement(ifStatement.Statement);
        if (statement is null) return null;

        var replacementIfStatement = ifStatement.WithStatement(statement);
        if (ifStatement.Else is null) return replacementIfStatement;

        var elseStatement = TryCreateSingleLineElseBranchStatement(ifStatement.Else.Statement);
        if (elseStatement is null) return null;

        return replacementIfStatement.WithElse(ifStatement.Else.WithStatement(elseStatement));
    }

    private static StatementSyntax? TryCreateSingleLineIfBranchStatement(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            if (block.Statements.Count != 1) return null;

            return TryCreateSingleLineIfBranchStatement(block.Statements[0]);
        }

        return IsNestedControlOrBracedStatement(statement) ? null : statement;
    }

    private static StatementSyntax? TryCreateSingleLineElseBranchStatement(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            if (block.Statements.Count != 1) return null;

            return TryCreateSingleLineElseBranchStatement(block.Statements[0]);
        }

        if (statement is IfStatementSyntax elseIfStatement) return TryCreateSingleLineIfStatement(elseIfStatement);

        return TryCreateSingleLineControlBodyStatement(statement);
    }

    private static StatementSyntax? TryCreateSingleLineControlBodyStatement(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            if (block.Statements.Count != 1) return null;

            return TryCreateSingleLineControlBodyStatement(block.Statements[0]);
        }

        return IsNestedControlOrBracedStatement(statement) ? null : statement;
    }

    private static bool ControlStatementNeedsBlockExpansion(StatementSyntax statement)
        => statement switch
        {
            IfStatementSyntax ifStatement => IfStatementNeedsBlockExpansion(ifStatement),
            ForStatementSyntax forStatement => forStatement.Statement is not BlockSyntax,
            ForEachStatementSyntax forEachStatement => forEachStatement.Statement is not BlockSyntax,
            ForEachVariableStatementSyntax forEachVariableStatement => forEachVariableStatement.Statement is not BlockSyntax,
            WhileStatementSyntax whileStatement => whileStatement.Statement is not BlockSyntax,
            DoStatementSyntax doStatement => doStatement.Statement is not BlockSyntax,
            UsingStatementSyntax usingStatement => usingStatement.Statement is not BlockSyntax,
            LockStatementSyntax lockStatement => lockStatement.Statement is not BlockSyntax,
            FixedStatementSyntax fixedStatement => fixedStatement.Statement is not BlockSyntax,
            _ => false
        };

    private static bool IfStatementNeedsBlockExpansion(IfStatementSyntax ifStatement)
    {
        var currentIfStatement = ifStatement;
        while (true)
        {
            if (currentIfStatement.Statement is not BlockSyntax) return true;
            if (currentIfStatement.Else is null) return false;
            if (currentIfStatement.Else.Statement is IfStatementSyntax elseIfStatement)
            {
                currentIfStatement = elseIfStatement;
                continue;
            }

            return currentIfStatement.Else.Statement is not BlockSyntax;
        }
    }

    private void ReportConstructorInitializerIfNeeded(ConstructorDeclarationSyntax node)
    {
        if (node.Initializer is null || node.Body is null || node.Body.Statements.Count != 0) return;

        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(node.Span);
        if (linePositionSpan.Start.Line == linePositionSpan.End.Line) return;

        var replacementText = Program.CreateSingleLineText(node);
        ReportSingleLineReplacementIfNeeded(node, node.Span, replacementText, "CSG0006", $"Empty constructors with this/base initializers must stay on one physical line when the resulting line is {Program.LineLengthThreshold} characters or shorter.", true);
    }

    private void ReportMemberNestedBlockExpansionIfNeeded(SyntaxNode node, BlockSyntax block)
    {
        if (!ContainsDirectNestedControlOrBracedStatement(block)) return;

        var blockLinePositionSpan = _sourceText.Lines.GetLinePositionSpan(block.Span);
        if (blockLinePositionSpan.Start.Line != blockLinePositionSpan.End.Line) return;

        ReportNestedBlockExpansion(node, node.Span, "CSG0007", "Braced members that contain nested control flow or nested braced logic must use expanded block formatting.");
    }

    private void ReportNestedBlockExpansionIfNeeded(SyntaxNode node, StatementSyntax statement, TextSpan span, bool expandMultilineParent)
    {
        if (statement is not BlockSyntax block) return;
        ReportNestedBlockExpansionIfNeeded(node, block, span, expandMultilineParent);
    }

    private void ReportNestedBlockExpansionIfNeeded(SyntaxNode node, BlockSyntax block, TextSpan span, bool expandMultilineParent)
    {
        if (!ContainsDirectNestedControlOrBracedStatement(block)) return;

        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(span);
        var isSingleLineParent = linePositionSpan.Start.Line == linePositionSpan.End.Line;
        if (!isSingleLineParent && (!expandMultilineParent || !DirectNestedStatementNeedsExpansion(block))) return;

        ReportNestedBlockExpansion(node, span, "CSG0007", "Blocks that contain nested control flow or nested braced logic must use expanded block formatting.");
    }

    private void ReportNestedBlockExpansion(SyntaxNode node, TextSpan span, string diagnosticId, string message)
    {
        var replacementText = CreateExpandedText(node, span);
        if (replacementText == _sourceText.ToString(span)) return;

        var canFixAutomatically = CanRewriteExceptionHandlingBlockAutomatically(node, span);
        var fullMessage = canFixAutomatically ? message : $"{message} Automatic rewriting was skipped because the span contains comments, directives, or disabled text.";
        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(span);
        Diagnostics.Add(new StyleDiagnostic(_sourceFilePath, span, linePositionSpan.Start.Line + 1, linePositionSpan.Start.Character + 1, diagnosticId, fullMessage, canFixAutomatically, node, replacementText));
    }

    private void ReportExpressionBodiedMemberIfNeeded(SyntaxNode memberNode, ArrowExpressionClauseSyntax expressionBody, SyntaxToken semicolonToken)
    {
        if (ContainsMultilineSwitchExpression(expressionBody.Expression)) return;

        var replacementText = TryCreateExpressionBodiedMemberReplacement(memberNode, expressionBody, semicolonToken);
        if (replacementText is null || replacementText == _sourceText.ToString(memberNode.Span)) return;

        var canFixAutomatically = !ContainsUnsafeTrivia(memberNode);
        var message = $"Expression-bodied member arrows must stay on the declaration line when the resulting line is {Program.LineLengthThreshold} characters or shorter, with object initializer and collection expression delimiters aligned to the member declaration.";
        var fullMessage = canFixAutomatically ? message : $"{message} Automatic rewriting was skipped because the span contains comments, directives, or disabled text.";
        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(memberNode.Span);
        Diagnostics.Add(new StyleDiagnostic(_sourceFilePath, memberNode.Span, linePositionSpan.Start.Line + 1, linePositionSpan.Start.Character + 1, "CSG0008", fullMessage, canFixAutomatically, memberNode, replacementText));
    }

    private void ReportSingleLineReplacementIfNeeded(SyntaxNode node, TextSpan span, string replacementText, string diagnosticId, string message, bool allowOwnBracedSyntax = false)
    {
        if (ContainsMultilineSwitchExpression(node)) return;
        if (!FitsSinglePhysicalLine(span, replacementText)) return;
        if (replacementText == _sourceText.ToString(span)) return;

        var canFixAutomatically = allowOwnBracedSyntax ? !ContainsUnsafeTrivia(node) : CanRewriteAutomatically(node);
        var fullMessage = canFixAutomatically ? message : $"{message} Automatic rewriting was skipped because the span contains comments, directives, disabled text, or multiline braced syntax.";
        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(span);
        Diagnostics.Add(new StyleDiagnostic(_sourceFilePath, span, linePositionSpan.Start.Line + 1, linePositionSpan.Start.Character + 1, diagnosticId, fullMessage, canFixAutomatically, node, replacementText));
    }

    private void ReportControlReplacementIfNeeded(SyntaxNode node, TextSpan span, string replacementText, string diagnosticId, string message, bool allowOwnBracedSyntax = false)
    {
        if (ContainsMultilineSwitchExpression(node)) return;
        if (!FitsReplacementPhysicalLines(span, replacementText)) return;
        if (replacementText == _sourceText.ToString(span)) return;

        var canFixAutomatically = allowOwnBracedSyntax ? !ContainsUnsafeTrivia(node) : CanRewriteAutomatically(node);
        var fullMessage = canFixAutomatically ? message : $"{message} Automatic rewriting was skipped because the span contains comments, directives, disabled text, or multiline braced syntax.";
        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(span);
        Diagnostics.Add(new StyleDiagnostic(_sourceFilePath, span, linePositionSpan.Start.Line + 1, linePositionSpan.Start.Character + 1, diagnosticId, fullMessage, canFixAutomatically, node, replacementText));
    }

    private string? TryCreateExpressionBodiedMemberReplacement(SyntaxNode memberNode, ArrowExpressionClauseSyntax expressionBody, SyntaxToken semicolonToken)
    {
        var headerText = TryCreateExpressionBodiedMemberHeaderText(memberNode);
        if (headerText is null) return null;

        var expression = expressionBody.Expression;
        var baseIndentation = GetLineIndentation(memberNode.SpanStart);
        var semicolonText = semicolonToken.IsKind(SyntaxKind.SemicolonToken) ? ";" : string.Empty;

        if (TryGetObjectInitializer(expression) is { } objectInitializer)
        {
            var expressionHeadText = CreateExpressionHeadText(expression);
            var firstLine = $"{headerText} => {expressionHeadText}";
            if (!FitsPhysicalLine(memberNode.SpanStart, firstLine)) return null;

            var replacementText = CreateExpressionWithAlignedDelimiterReplacement(firstLine, objectInitializer, baseIndentation, semicolonText);
            return AddAttributeLinesToExpressionBodiedMemberReplacement(memberNode, replacementText, baseIndentation);
        }

        if (expression is CollectionExpressionSyntax collectionExpression)
        {
            var collectionExpressionText = CreateAlignedCollectionExpressionText(collectionExpression, baseIndentation, semicolonText);
            if (collectionExpressionText.Length == 0)
            {
                var emptyCollectionLine = $"{headerText} => []{semicolonText}";
                return FitsPhysicalLine(memberNode.SpanStart, emptyCollectionLine) ? AddAttributeLinesToExpressionBodiedMemberReplacement(memberNode, emptyCollectionLine, baseIndentation) : null;
            }

            var firstLine = $"{headerText} =>";
            if (!FitsPhysicalLine(memberNode.SpanStart, firstLine)) return null;

            var outputLines = new List<string> { firstLine };
            outputLines.AddRange(SplitLines(collectionExpressionText));

            return AddAttributeLinesToExpressionBodiedMemberReplacement(memberNode, string.Join(_endOfLine, outputLines), baseIndentation);
        }

        if (IsExpressionBodiedMemberArrowOnDeclarationLine(expressionBody)) return null;

        var singleLineReplacementText = $"{headerText} => {Program.CreateSingleLineText(expression)}{semicolonText}";
        return FitsPhysicalLine(memberNode.SpanStart, singleLineReplacementText) ? AddAttributeLinesToExpressionBodiedMemberReplacement(memberNode, singleLineReplacementText, baseIndentation) : null;
    }

    private bool IsExpressionBodiedMemberArrowOnDeclarationLine(ArrowExpressionClauseSyntax expressionBody)
    {
        var previousToken = expressionBody.ArrowToken.GetPreviousToken();
        if (previousToken.RawKind == 0) return false;

        var previousTokenLine = _sourceText.Lines.GetLinePosition(previousToken.Span.End).Line;
        var arrowLine = _sourceText.Lines.GetLinePosition(expressionBody.ArrowToken.SpanStart).Line;
        return previousTokenLine == arrowLine;
    }

    private string CreateExpressionWithAlignedDelimiterReplacement(string firstLine, InitializerExpressionSyntax initializer, string baseIndentation, string semicolonText)
    {
        var normalizedInitializer = initializer.NormalizeWhitespace(indentation: "    ", eol: _endOfLine, elasticTrivia: false);
        var spacedInitializer = Program.ApplySpacingRewriters(normalizedInitializer) ?? normalizedInitializer;
        var normalizedInitializerLines = SplitLines(spacedInitializer.ToString());
        if (normalizedInitializerLines.Count == 0) return firstLine + semicolonText;
        if (normalizedInitializerLines.Count == 1) return string.Join(_endOfLine, firstLine, baseIndentation + normalizedInitializerLines[0] + semicolonText);

        var outputLines = new List<string> { firstLine };
        for (var index = 0; index < normalizedInitializerLines.Count; index++)
        {
            var line = normalizedInitializerLines[index];
            if (index == normalizedInitializerLines.Count - 1) line += semicolonText;

            outputLines.Add(baseIndentation + line);
        }

        return string.Join(_endOfLine, outputLines);
    }

    private string AddAttributeLinesToExpressionBodiedMemberReplacement(SyntaxNode memberNode, string replacementText, string baseIndentation)
    {
        var attributeLines = CreateExpressionBodiedMemberAttributeLines(memberNode);
        if (attributeLines.Count == 0) return replacementText;

        var replacementLines = SplitLines(replacementText);
        if (replacementLines.Count == 0) return replacementText;

        var outputLines = new List<string> { attributeLines[0] };
        for (var index = 1; index < attributeLines.Count; index++) outputLines.Add(baseIndentation + attributeLines[index]);

        outputLines.Add(baseIndentation + replacementLines[0]);
        outputLines.AddRange(replacementLines.Skip(1));

        return string.Join(_endOfLine, outputLines);
    }

    private bool CanRewriteAutomatically(SyntaxNode node) => !ContainsUnsafeTrivia(node) && !ContainsMultilineBracedSyntax(node);

    private static bool CanRewriteExceptionHandlingBlockAutomatically(SyntaxNode node, TextSpan span) => !ContainsUnsafeTrivia(node, span);

    private static bool IsNestedSingleLineBinaryExpression(BinaryExpressionSyntax node) => node.Parent is BinaryExpressionSyntax parentNode && IsSingleLineBinaryExpression(parentNode);

    private static bool IsSingleLineBinaryExpression(BinaryExpressionSyntax node) => node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression) || node.IsKind(SyntaxKind.CoalesceExpression);

    private bool FitsSinglePhysicalLine(TextSpan span, string replacementText)
    {
        if (replacementText.Contains('\r') || replacementText.Contains('\n')) return false;
        return FitsPhysicalLine(span.Start, replacementText);
    }

    private bool FitsReplacementPhysicalLines(TextSpan span, string replacementText)
    {
        var replacementLines = SplitLines(replacementText);
        if (replacementLines.Count == 0) return true;
        if (!FitsPhysicalLine(span.Start, replacementLines[0])) return false;

        for (var index = 1; index < replacementLines.Count; index++)
        {
            if (replacementLines[index].Length > Program.LineLengthThreshold)
            {
                return false;
            }
        }

        return true;
    }

    private bool FitsPhysicalLine(int spanStart, string lineText)
    {
        var linePosition = _sourceText.Lines.GetLinePosition(spanStart);
        return linePosition.Character + lineText.Length <= Program.LineLengthThreshold;
    }

    private string CreateExpandedText(SyntaxNode node, TextSpan span)
    {
        var rewrittenNode = NestedControlBlockRewriter.Shared.Visit(node) ?? node;
        var normalizedNode = rewrittenNode.NormalizeWhitespace(indentation: "    ", eol: _endOfLine, elasticTrivia: false);
        var spacedNode = Program.ApplySpacingRewriters(normalizedNode);
        var normalizedText = (spacedNode ?? normalizedNode).ToString();
        return IndentContinuationLines(normalizedText, GetLineIndentation(span.Start));
    }

    private string GetLineIndentation(int position)
    {
        var textLine = _sourceText.Lines.GetLineFromPosition(position);
        return _sourceText.ToString(TextSpan.FromBounds(textLine.Start, position));
    }

    private string IndentContinuationLines(string text, string baseIndentation)
    {
        var lines = SplitLines(text);
        if (lines.Count <= 1) return text;

        for (var index = 1; index < lines.Count; index++)
        {
            if (lines[index].Length > 0)
            {
                lines[index] = baseIndentation + lines[index];
            }
        }

        return string.Join(_endOfLine, lines);
    }

    private bool DirectNestedStatementNeedsExpansion(BlockSyntax block)
    {
        if (block.Statements.Count != 1) return false;

        var statement = block.Statements[0];
        if (statement is TryStatementSyntax) return false;

        return IsNestedControlOrBracedStatement(statement) && TryGetDirectNestedBodyBlock(statement) is { } nestedBlock && IsSingleLine(nestedBlock.Span);
    }

    private static bool ShouldExpandNestedControlStatement(StatementSyntax statement)
    {
        if (statement.Parent is not BlockSyntax block) return false;
        if (!IsControlStatementBodyBlock(block)) return false;
        if (statement is IfStatementSyntax && IsMethodLikeBodyBlock(block)) return false;
        return block.Statements.Count == 1 && IsSameNode(block.Statements[0], statement);
    }

    private static bool IsMethodLikeBodyBlock(BlockSyntax block)
        => block.Parent switch
        {
            MethodDeclarationSyntax methodDeclaration => methodDeclaration.Body is not null && IsSameNode(methodDeclaration.Body, block),
            LocalFunctionStatementSyntax localFunctionStatement => localFunctionStatement.Body is not null && IsSameNode(localFunctionStatement.Body, block),
            ConstructorDeclarationSyntax constructorDeclaration => constructorDeclaration.Body is not null && IsSameNode(constructorDeclaration.Body, block),
            DestructorDeclarationSyntax destructorDeclaration => destructorDeclaration.Body is not null && IsSameNode(destructorDeclaration.Body, block),
            OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.Body is not null && IsSameNode(operatorDeclaration.Body, block),
            ConversionOperatorDeclarationSyntax conversionOperatorDeclaration => conversionOperatorDeclaration.Body is not null && IsSameNode(conversionOperatorDeclaration.Body, block),
            AccessorDeclarationSyntax accessorDeclaration => accessorDeclaration.Body is not null && IsSameNode(accessorDeclaration.Body, block),
            ParenthesizedLambdaExpressionSyntax parenthesizedLambdaExpression => parenthesizedLambdaExpression.Block is not null && IsSameNode(parenthesizedLambdaExpression.Block, block),
            SimpleLambdaExpressionSyntax simpleLambdaExpression => simpleLambdaExpression.Block is not null && IsSameNode(simpleLambdaExpression.Block, block),
            AnonymousMethodExpressionSyntax anonymousMethodExpression => anonymousMethodExpression.Block is not null && IsSameNode(anonymousMethodExpression.Block, block),
            _ => false
        };

    private static bool IsControlStatementBodyBlock(BlockSyntax block)
        => block.Parent switch
        {
            _ when IsMethodLikeBodyBlock(block) => true,
            IfStatementSyntax ifStatement => IsSameNode(ifStatement.Statement, block),
            ElseClauseSyntax elseClause => IsSameNode(elseClause.Statement, block),
            ForStatementSyntax forStatement => IsSameNode(forStatement.Statement, block),
            ForEachStatementSyntax forEachStatement => IsSameNode(forEachStatement.Statement, block),
            ForEachVariableStatementSyntax forEachVariableStatement => IsSameNode(forEachVariableStatement.Statement, block),
            WhileStatementSyntax whileStatement => IsSameNode(whileStatement.Statement, block),
            DoStatementSyntax doStatement => IsSameNode(doStatement.Statement, block),
            UsingStatementSyntax usingStatement => IsSameNode(usingStatement.Statement, block),
            LockStatementSyntax lockStatement => IsSameNode(lockStatement.Statement, block),
            FixedStatementSyntax fixedStatement => IsSameNode(fixedStatement.Statement, block),
            TryStatementSyntax tryStatement => IsSameNode(tryStatement.Block, block),
            CatchClauseSyntax catchClause => IsSameNode(catchClause.Block, block),
            FinallyClauseSyntax finallyClause => IsSameNode(finallyClause.Block, block),
            CheckedStatementSyntax checkedStatement => IsSameNode(checkedStatement.Block, block),
            UnsafeStatementSyntax unsafeStatement => IsSameNode(unsafeStatement.Block, block),
            _ => false
        };

    private static bool IsSameNode(SyntaxNode firstNode, SyntaxNode secondNode) => firstNode.RawKind == secondNode.RawKind && firstNode.Span == secondNode.Span;

    private bool ContainsDirectNestedControlOrBracedStatement(BlockSyntax block) => block.Statements.Any(IsNestedControlOrBracedStatement);

    private bool IsSingleLine(TextSpan span)
    {
        var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(span);
        return linePositionSpan.Start.Line == linePositionSpan.End.Line;
    }

    private static BlockSyntax? TryGetDirectNestedBodyBlock(StatementSyntax statement)
        => statement switch
        {
            IfStatementSyntax ifStatement => ifStatement.Statement as BlockSyntax ?? ifStatement.Else?.Statement as BlockSyntax,
            ForStatementSyntax forStatement => forStatement.Statement as BlockSyntax,
            ForEachStatementSyntax forEachStatement => forEachStatement.Statement as BlockSyntax,
            ForEachVariableStatementSyntax forEachVariableStatement => forEachVariableStatement.Statement as BlockSyntax,
            WhileStatementSyntax whileStatement => whileStatement.Statement as BlockSyntax,
            DoStatementSyntax doStatement => doStatement.Statement as BlockSyntax,
            UsingStatementSyntax usingStatement => usingStatement.Statement as BlockSyntax,
            LockStatementSyntax lockStatement => lockStatement.Statement as BlockSyntax,
            TryStatementSyntax tryStatement => tryStatement.Block,
            LocalFunctionStatementSyntax localFunctionStatement => localFunctionStatement.Body,
            CheckedStatementSyntax checkedStatement => checkedStatement.Block,
            UnsafeStatementSyntax unsafeStatement => unsafeStatement.Block,
            FixedStatementSyntax fixedStatement => fixedStatement.Statement as BlockSyntax,
            _ => null
        };

    private static bool IsNestedControlOrBracedStatement(StatementSyntax statement)
        => statement is IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax or WhileStatementSyntax or DoStatementSyntax or UsingStatementSyntax or LockStatementSyntax or TryStatementSyntax or SwitchStatementSyntax or LocalFunctionStatementSyntax or CheckedStatementSyntax or UnsafeStatementSyntax or FixedStatementSyntax;

    private static string? TryCreateExpressionBodiedMemberHeaderText(SyntaxNode memberNode)
        => memberNode switch
        {
            MethodDeclarationSyntax methodDeclaration => Program.CreateSingleLineText(methodDeclaration.WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>()).WithBody(null).WithExpressionBody(null).WithSemicolonToken(default)),
            LocalFunctionStatementSyntax localFunctionStatement => Program.CreateSingleLineText(localFunctionStatement.WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>()).WithBody(null).WithExpressionBody(null).WithSemicolonToken(default)),
            ConstructorDeclarationSyntax constructorDeclaration => Program.CreateSingleLineText(constructorDeclaration.WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>()).WithBody(null).WithExpressionBody(null).WithSemicolonToken(default)),
            DestructorDeclarationSyntax destructorDeclaration => Program.CreateSingleLineText(destructorDeclaration.WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>()).WithBody(null).WithExpressionBody(null).WithSemicolonToken(default)),
            PropertyDeclarationSyntax propertyDeclaration => Program.CreateSingleLineText(propertyDeclaration.WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>()).WithExpressionBody(null).WithSemicolonToken(default)),
            IndexerDeclarationSyntax indexerDeclaration => Program.CreateSingleLineText(indexerDeclaration.WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>()).WithExpressionBody(null).WithSemicolonToken(default)),
            OperatorDeclarationSyntax operatorDeclaration => Program.CreateSingleLineText(operatorDeclaration.WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>()).WithBody(null).WithExpressionBody(null).WithSemicolonToken(default)),
            ConversionOperatorDeclarationSyntax conversionOperatorDeclaration => Program.CreateSingleLineText(conversionOperatorDeclaration.WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>()).WithBody(null).WithExpressionBody(null).WithSemicolonToken(default)),
            _ => null
        };

    private static IReadOnlyList<string> CreateExpressionBodiedMemberAttributeLines(SyntaxNode memberNode)
    {
        var attributeLists = memberNode switch
        {
            MethodDeclarationSyntax methodDeclaration => methodDeclaration.AttributeLists,
            LocalFunctionStatementSyntax localFunctionStatement => localFunctionStatement.AttributeLists,
            ConstructorDeclarationSyntax constructorDeclaration => constructorDeclaration.AttributeLists,
            DestructorDeclarationSyntax destructorDeclaration => destructorDeclaration.AttributeLists,
            PropertyDeclarationSyntax propertyDeclaration => propertyDeclaration.AttributeLists,
            IndexerDeclarationSyntax indexerDeclaration => indexerDeclaration.AttributeLists,
            OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.AttributeLists,
            ConversionOperatorDeclarationSyntax conversionOperatorDeclaration => conversionOperatorDeclaration.AttributeLists,
            _ => SyntaxFactory.List<AttributeListSyntax>()
        };

        return attributeLists.Count == 0 ? [] : attributeLists.Select(Program.CreateSingleLineText).ToList();
    }

    private static InitializerExpressionSyntax? TryGetObjectInitializer(ExpressionSyntax expression)
        => expression switch
        {
            ObjectCreationExpressionSyntax objectCreationExpression => objectCreationExpression.Initializer,
            ImplicitObjectCreationExpressionSyntax implicitObjectCreationExpression => implicitObjectCreationExpression.Initializer,
            _ => null
        };

    private static string CreateExpressionHeadText(ExpressionSyntax expression)
        => expression switch
        {
            ObjectCreationExpressionSyntax objectCreationExpression => Program.CreateSingleLineText(objectCreationExpression.WithInitializer(null)),
            ImplicitObjectCreationExpressionSyntax implicitObjectCreationExpression => Program.CreateSingleLineText(implicitObjectCreationExpression.WithInitializer(null)),
            _ => Program.CreateSingleLineText(expression)
        };

    private string CreateAlignedCollectionExpressionText(CollectionExpressionSyntax collectionExpression, string baseIndentation, string semicolonText)
    {
        if (collectionExpression.Elements.Count == 0) return string.Empty;

        var delimiterIndentation = GetLineIndentation(collectionExpression.SpanStart);
        var outputLines = SplitLines(collectionExpression.ToString());
        if (outputLines.Count == 0) return string.Empty;

        for (var index = 0; index < outputLines.Count; index++)
        {
            var line = outputLines[index];
            if (index == outputLines.Count - 1) line += semicolonText;

            outputLines[index] = line.Length == 0 ? string.Empty : baseIndentation + RemovePrefix(line, delimiterIndentation);
        }

        return string.Join(_endOfLine, outputLines);
    }

    private static string RemovePrefix(string text, string prefix) => prefix.Length > 0 && text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : text;

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

    private bool ContainsNestedMultilineBracedSyntax(SyntaxNode node, BlockSyntax allowedBlock, TextSpan span)
    {
        foreach (var descendantNode in node.DescendantNodes(descendIntoTrivia: false))
        {
            if (!span.Contains(descendantNode.Span)) continue;

            if (ReferenceEquals(descendantNode, allowedBlock)) continue;

            if (!IsBracedSyntax(descendantNode)) continue;

            var linePositionSpan = _sourceText.Lines.GetLinePositionSpan(descendantNode.Span);
            if (linePositionSpan.Start.Line != linePositionSpan.End.Line) return true;
        }

        return false;
    }

    private bool ContainsMultilineSwitchExpression(SyntaxNode node)
    {
        foreach (var switchExpression in node.DescendantNodesAndSelf(descendIntoTrivia: false).OfType<SwitchExpressionSyntax>())
        {
            if (!IsSingleLine(switchExpression.Span))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBracedSyntax(SyntaxNode node) => node is BlockSyntax or InitializerExpressionSyntax or AnonymousObjectCreationExpressionSyntax or SwitchExpressionSyntax;

    private static List<string> SplitLines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Select(line => string.IsNullOrWhiteSpace(line) ? string.Empty : line).ToList();

    private static bool HasEndOfLineTrivia(SyntaxTriviaList syntaxTriviaList) => syntaxTriviaList.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));

    private static string GetEndOfLine(SourceText sourceText)
    {
        for (var index = 0; index < sourceText.Lines.Count; index++)
        {
            var textLine = sourceText.Lines[index];
            if (textLine.EndIncludingLineBreak == textLine.End) continue;

            return sourceText.ToString(TextSpan.FromBounds(textLine.End, textLine.EndIncludingLineBreak));
        }

        return Environment.NewLine;
    }

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

    private static bool ContainsUnsafeTrivia(SyntaxNode node, TextSpan span)
    {
        foreach (var trivia in node.DescendantTrivia(descendIntoTrivia: true))
        {
            if (!span.Contains(trivia.SpanStart)) continue;

            if (trivia.IsDirective) return true;

            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)) return true;

            if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)) return true;

            if (trivia.IsKind(SyntaxKind.DisabledTextTrivia)) return true;
        }

        return false;
    }
}

internal sealed class NestedControlBlockRewriter : CSharpSyntaxRewriter
{
    public static NestedControlBlockRewriter Shared { get; } = new();

    public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
    {
        var ifStatement = (IfStatementSyntax?)base.VisitIfStatement(node) ?? node;
        var statement = EnsureBlock(ifStatement.Statement);
        var elseClause = ifStatement.Else;
        if (elseClause?.Statement is { } elseStatement && elseStatement is not IfStatementSyntax) elseClause = elseClause.WithStatement(EnsureBlock(elseStatement));
        return ifStatement.WithStatement(statement).WithElse(elseClause);
    }

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    {
        var forStatement = (ForStatementSyntax?)base.VisitForStatement(node) ?? node;
        return forStatement.WithStatement(EnsureBlock(forStatement.Statement));
    }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        var forEachStatement = (ForEachStatementSyntax?)base.VisitForEachStatement(node) ?? node;
        return forEachStatement.WithStatement(EnsureBlock(forEachStatement.Statement));
    }

    public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        var forEachVariableStatement = (ForEachVariableStatementSyntax?)base.VisitForEachVariableStatement(node) ?? node;
        return forEachVariableStatement.WithStatement(EnsureBlock(forEachVariableStatement.Statement));
    }

    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
    {
        var whileStatement = (WhileStatementSyntax?)base.VisitWhileStatement(node) ?? node;
        return whileStatement.WithStatement(EnsureBlock(whileStatement.Statement));
    }

    public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
    {
        var doStatement = (DoStatementSyntax?)base.VisitDoStatement(node) ?? node;
        return doStatement.WithStatement(EnsureBlock(doStatement.Statement));
    }

    public override SyntaxNode? VisitUsingStatement(UsingStatementSyntax node)
    {
        var usingStatement = (UsingStatementSyntax?)base.VisitUsingStatement(node) ?? node;
        return usingStatement.WithStatement(EnsureBlock(usingStatement.Statement));
    }

    public override SyntaxNode? VisitLockStatement(LockStatementSyntax node)
    {
        var lockStatement = (LockStatementSyntax?)base.VisitLockStatement(node) ?? node;
        return lockStatement.WithStatement(EnsureBlock(lockStatement.Statement));
    }

    public override SyntaxNode? VisitFixedStatement(FixedStatementSyntax node)
    {
        var fixedStatement = (FixedStatementSyntax?)base.VisitFixedStatement(node) ?? node;
        return fixedStatement.WithStatement(EnsureBlock(fixedStatement.Statement));
    }

    private static StatementSyntax EnsureBlock(StatementSyntax statement)
    {
        if (statement is BlockSyntax) return statement;
        return SyntaxFactory.Block(statement.WithoutTrivia());
    }
}

internal sealed class IsPatternSpacingRewriter : CSharpSyntaxRewriter
{
    public static IsPatternSpacingRewriter Shared { get; } = new();

    public override SyntaxNode? VisitIsPatternExpression(IsPatternExpressionSyntax node)
    {
        var isPatternExpression = (IsPatternExpressionSyntax?)base.VisitIsPatternExpression(node) ?? node;
        var isKeyword = isPatternExpression.IsKeyword;
        var expressionLastToken = isPatternExpression.Expression.GetLastToken();
        if (HasEndOfLineTrivia(expressionLastToken.TrailingTrivia) || HasEndOfLineTrivia(isKeyword.LeadingTrivia)) return isPatternExpression;
        if (expressionLastToken.TrailingTrivia.ToFullString() + isKeyword.LeadingTrivia.ToFullString() == " ") return isPatternExpression;

        var expression = isPatternExpression.Expression.ReplaceToken(expressionLastToken, expressionLastToken.WithTrailingTrivia(SyntaxFactory.Space));
        return isPatternExpression.WithExpression(expression).WithIsKeyword(isKeyword.WithLeadingTrivia());
    }

    private static bool HasEndOfLineTrivia(SyntaxTriviaList syntaxTriviaList) => syntaxTriviaList.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
}

internal sealed class RelationalPatternSpacingRewriter : CSharpSyntaxRewriter
{
    public static RelationalPatternSpacingRewriter Shared { get; } = new();

    public override SyntaxNode? VisitRelationalPattern(RelationalPatternSyntax node)
    {
        var relationalPatternSyntax = (RelationalPatternSyntax?)base.VisitRelationalPattern(node) ?? node;
        var operatorToken = relationalPatternSyntax.OperatorToken;
        var previousToken = operatorToken.GetPreviousToken();
        if (!NeedsSpaceBeforeRelationalPatternOperator(previousToken, operatorToken)) return relationalPatternSyntax;

        return relationalPatternSyntax.WithOperatorToken(operatorToken.WithLeadingTrivia(SyntaxFactory.Space));
    }

    private static bool NeedsSpaceBeforeRelationalPatternOperator(SyntaxToken previousToken, SyntaxToken operatorToken)
        => IsPatternKeyword(previousToken) && (operatorToken.IsKind(SyntaxKind.GreaterThanToken) || operatorToken.IsKind(SyntaxKind.LessThanToken) || operatorToken.IsKind(SyntaxKind.GreaterThanEqualsToken) || operatorToken.IsKind(SyntaxKind.LessThanEqualsToken)) && !HasWhitespaceTrivia(previousToken.TrailingTrivia) && !HasWhitespaceTrivia(operatorToken.LeadingTrivia);

    private static bool IsPatternKeyword(SyntaxToken syntaxToken) => syntaxToken.IsKind(SyntaxKind.IsKeyword) || syntaxToken.IsKind(SyntaxKind.NotKeyword) || syntaxToken.IsKind(SyntaxKind.AndKeyword) || syntaxToken.IsKind(SyntaxKind.OrKeyword);

    private static bool HasWhitespaceTrivia(SyntaxTriviaList syntaxTriviaList) => syntaxTriviaList.Any(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia));
}

internal sealed class CatchFilterSpacingRewriter : CSharpSyntaxRewriter
{
    public static CatchFilterSpacingRewriter Shared { get; } = new();

    public override SyntaxNode? VisitCatchFilterClause(CatchFilterClauseSyntax node)
    {
        var catchFilterClauseSyntax = (CatchFilterClauseSyntax?)base.VisitCatchFilterClause(node) ?? node;
        var whenKeyword = catchFilterClauseSyntax.WhenKeyword;
        if (HasWhitespaceTrivia(whenKeyword.LeadingTrivia)) return catchFilterClauseSyntax;

        return catchFilterClauseSyntax.WithWhenKeyword(whenKeyword.WithLeadingTrivia(SyntaxFactory.Space));
    }

    private static bool HasWhitespaceTrivia(SyntaxTriviaList syntaxTriviaList) => syntaxTriviaList.Any(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia));
}

internal sealed class SwitchExpressionSpacingRewriter : CSharpSyntaxRewriter
{
    public static SwitchExpressionSpacingRewriter Shared { get; } = new();

    public override SyntaxNode? VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        var switchExpression = (SwitchExpressionSyntax?)base.VisitSwitchExpression(node) ?? node;
        var closeBraceToken = switchExpression.CloseBraceToken;
        if (HasEndOfLineTrivia(closeBraceToken.LeadingTrivia) || closeBraceToken.LeadingTrivia.ToFullString() == " ") return switchExpression;

        return switchExpression.WithCloseBraceToken(closeBraceToken.WithLeadingTrivia(SyntaxFactory.Space));
    }

    private static bool HasEndOfLineTrivia(SyntaxTriviaList syntaxTriviaList) => syntaxTriviaList.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
}

internal sealed class CollectionExpressionSpacingRewriter : CSharpSyntaxRewriter
{
    public static CollectionExpressionSpacingRewriter Shared { get; } = new();

    public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
    {
        var returnStatement = (ReturnStatementSyntax?)base.VisitReturnStatement(node) ?? node;
        return returnStatement.Expression is CollectionExpressionSyntax ? returnStatement.WithReturnKeyword(EnsureTrailingSpace(returnStatement.ReturnKeyword)) : returnStatement;
    }

    public override SyntaxNode? VisitYieldStatement(YieldStatementSyntax node)
    {
        var yieldStatement = (YieldStatementSyntax?)base.VisitYieldStatement(node) ?? node;
        return yieldStatement.Expression is CollectionExpressionSyntax ? yieldStatement.WithReturnOrBreakKeyword(EnsureTrailingSpace(yieldStatement.ReturnOrBreakKeyword)) : yieldStatement;
    }

    private static SyntaxToken EnsureTrailingSpace(SyntaxToken syntaxToken)
    {
        if (syntaxToken.TrailingTrivia.Any(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia) || trivia.IsKind(SyntaxKind.EndOfLineTrivia))) return syntaxToken;
        return syntaxToken.WithTrailingTrivia(SyntaxFactory.Space);
    }
}

internal sealed class ObjectCreationArgumentListSpacingRewriter : CSharpSyntaxRewriter
{
    public static ObjectCreationArgumentListSpacingRewriter Shared { get; } = new();

    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var objectCreationExpression = (ObjectCreationExpressionSyntax?)base.VisitObjectCreationExpression(node) ?? node;
        if (objectCreationExpression.ArgumentList is null) return objectCreationExpression;

        var typeLastToken = objectCreationExpression.Type.GetLastToken();
        var openParenthesisToken = objectCreationExpression.ArgumentList.OpenParenToken;
        if (typeLastToken.RawKind == 0 || openParenthesisToken.RawKind == 0) return objectCreationExpression;
        if (!CanRemoveSpacing(typeLastToken.TrailingTrivia, openParenthesisToken.LeadingTrivia)) return objectCreationExpression;
        if (typeLastToken.TrailingTrivia.Count == 0 && openParenthesisToken.LeadingTrivia.Count == 0) return objectCreationExpression;

        var type = objectCreationExpression.Type.ReplaceToken(typeLastToken, typeLastToken.WithTrailingTrivia());
        var argumentList = objectCreationExpression.ArgumentList.WithOpenParenToken(openParenthesisToken.WithLeadingTrivia());
        return objectCreationExpression.WithType(type).WithArgumentList(argumentList);
    }

    private static bool CanRemoveSpacing(SyntaxTriviaList trailingTrivia, SyntaxTriviaList leadingTrivia) => ContainsOnlyWhitespaceTrivia(trailingTrivia) && ContainsOnlyWhitespaceTrivia(leadingTrivia);

    private static bool ContainsOnlyWhitespaceTrivia(SyntaxTriviaList triviaList) => triviaList.All(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia));
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

internal sealed record StyleDiagnostic(string SourceFilePath, TextSpan Span, int LineNumber, int CharacterNumber, string DiagnosticId, string Message, bool CanFixAutomatically, SyntaxNode Node, string? ReplacementText = null)
{
    public string ToDisplayString() => $"{SourceFilePath}({LineNumber},{CharacterNumber}): {DiagnosticId} {Message}";
}

internal sealed record FileResult(IReadOnlyList<StyleDiagnostic> Diagnostics, bool Modified, int UnsafeFixCount);

internal sealed class SimpleLambdaBlockExpressionRewriter : CSharpSyntaxRewriter
{
    public static SimpleLambdaBlockExpressionRewriter Shared { get; } = new();

    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        var lambdaExpression = (ParenthesizedLambdaExpressionSyntax?)base.VisitParenthesizedLambdaExpression(node) ?? node;
        if (lambdaExpression.Block is not { } block) return lambdaExpression;

        var expression = TryGetSingleLambdaBlockExpression(block);
        return expression is null ? lambdaExpression : lambdaExpression.WithBlock(null).WithExpressionBody(expression.WithoutTrivia());
    }

    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        var lambdaExpression = (SimpleLambdaExpressionSyntax?)base.VisitSimpleLambdaExpression(node) ?? node;
        if (lambdaExpression.Block is not { } block) return lambdaExpression;

        var expression = TryGetSingleLambdaBlockExpression(block);
        return expression is null ? lambdaExpression : lambdaExpression.WithBlock(null).WithExpressionBody(expression.WithoutTrivia());
    }

    private static ExpressionSyntax? TryGetSingleLambdaBlockExpression(BlockSyntax block)
    {
        if (block.Statements.Count != 1) return null;

        return block.Statements[0] switch
        {
            ReturnStatementSyntax { Expression: { } expression } => expression,
            ExpressionStatementSyntax expressionStatement => expressionStatement.Expression,
            _ => null
        };
    }
}
