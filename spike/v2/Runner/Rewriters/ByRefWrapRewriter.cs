// ByRefWrapRewriter — performs the only mechanical transformation that BC's own
// service tier applies to AL-emitter C# at extension install time, and that
// `--dump-csharp` does NOT include in its output.
//
// Microsoft's pre-compiled DLLs (e.g. System Application 27.5) prove the convention:
// every AL `var` parameter becomes `ByRef<T>` in BOTH the parameter type AND the
// backing field (8,373 occurrences of `ByRef<>` in System Application; ZERO uses of
// `[NavByReferenceAttribute]`). The AL-compiler-emitted intermediate form decorates
// the parameter with `[NavByReferenceAttribute] T x` and stores it in a plain `T`
// field — relying on a downstream pass (BC's own, or v1's RoslynRewriter) to wrap.
//
// Wrap rule, distilled from Microsoft's pre-compiled System Application 27.5 DLL:
//
//   • Parameter type ends with `Handle` (NavCodeunitHandle, INavRecordHandle,
//     NavRecordHandle, NavInterfaceHandle, NavTestPageHandle, NavStream, …):
//        → keep the parameter as `[NavByReferenceAttribute] T x`; do NOT wrap.
//        Handles are already indirect references (Tree.GetReferenceTarget); wrapping
//        them in `ByRef<>` would be redundant. Microsoft's DLL has 1,166 occurrences
//        of `[NavByReference] *Handle` and ZERO occurrences of `ByRef<*Handle>`.
//   • Every other type (NavCode, NavText, bool, int, NavDate, NavRecordRef,
//     NavFieldRef, NavList<T>, NavDictionary<…>, NavOption, …):
//        → replace parameter type `T` with `ByRef<T>`, strip `[NavByReferenceAttribute]`.
//        Microsoft's DLL has 8,373+ `ByRef<T>` occurrences for these types.
//   • Method modifier `override` overrides every other rule — base method signature
//     is fixed; the AL compiler preserves `[NavByReference] T` even on value-typed
//     overrides because the override has to match the base.
//
// What this rewriter does:
//   1. Parameters that meet the wrap criteria above:
//        - replace type `T` with `ByRef<T>`
//        - strip the `[NavByReferenceAttribute]` annotation
//   2. Fields whose name matches such a wrapped parameter (within the same class):
//        - replace type `T` with `ByRef<T>`.
//   3. `OnInvoke` dispatch sites, where the AL emitter wrote
//        `ALCompiler.ObjectToExactXxx(args[i])`
//      for a now-wrapped parameter position:
//        - rewrite to `(ByRef<T>)ALCompiler.SafeCastCheck<ByRef<T>>(args[i])`
//        - matches Microsoft's compiled-DLL convention.
//
// No identifier renames (no MockX), no injection of v1-runtime types. Every
// transformation is what BC's service tier applies at extension install time.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AlRunnerV2.Rewriters;

public sealed class ByRefWrapRewriter : CSharpSyntaxRewriter
{
    private readonly HashSet<string> _fieldsToWrap;
    // (className, methodName) → list of (paramPosition, innerType) for wrapped params.
    // Used by VisitInvocationExpression to rewrite OnInvoke dispatch arg unboxing.
    private readonly Dictionary<string, List<(int pos, TypeSyntax innerType)>> _wrappedMethods;

    private ByRefWrapRewriter(
        HashSet<string> fieldsToWrap,
        Dictionary<string, List<(int, TypeSyntax)>> wrappedMethods)
    {
        _fieldsToWrap = fieldsToWrap;
        _wrappedMethods = wrappedMethods;
    }

    /// <summary>
    /// Apply the wrap to one parsed C# syntax tree. Two passes: collect (className,
    /// fieldName) pairs and (className+methodName, position+innerType) tuples from
    /// `[NavByReferenceAttribute]`-marked parameters, then rewrite parameter
    /// declarations, matching field declarations, and OnInvoke dispatch args.
    /// </summary>
    public static SyntaxTree Rewrite(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var collector = new ByRefCollector();
        collector.Visit(root);
        if (collector.FieldsToWrap.Count == 0 && collector.WrappedMethods.Count == 0)
            return tree;
        var rewriter = new ByRefWrapRewriter(collector.FieldsToWrap, collector.WrappedMethods);
        var newRoot = rewriter.Visit(root);
        return tree.WithRootAndOptions(newRoot, tree.Options);
    }

    public override SyntaxNode? VisitParameter(ParameterSyntax node)
    {
        if (ShouldWrap(node) && node.Type != null)
        {
            var newType = WrapInByRef(node.Type);
            // Strip the NavByReferenceAttribute, keep any others (e.g. NavObjectId).
            var newAttrLists = node.AttributeLists
                .Select(a => a.WithAttributes(SyntaxFactory.SeparatedList(
                    a.Attributes.Where(at => !IsNavByRefName(at.Name.ToString())))))
                .Where(a => a.Attributes.Count > 0)
                .ToList();
            return node
                .WithAttributeLists(SyntaxFactory.List(newAttrLists))
                .WithType(newType.WithTriviaFrom(node.Type));
        }
        return base.VisitParameter(node);
    }

    /// <summary>
    /// Wrap iff:
    ///   1. The parameter is annotated with [NavByReferenceAttribute].
    ///   2. The enclosing method is NOT an `override` (overrides preserve the base
    ///      method's signature exactly — including the [NavByReference] attribute).
    ///   3. The parameter type's name does NOT end with `Handle`. Handle types
    ///      (NavCodeunitHandle, INavRecordHandle, NavInterfaceHandle, NavStream-
    ///      Handle, etc.) are already indirect references; Microsoft's compiled
    ///      DLLs never wrap them — they keep [NavByReference] on the parameter and
    ///      use the type as-is.
    /// </summary>
    private static bool ShouldWrap(ParameterSyntax node)
    {
        if (!HasNavByRef(node) || node.Type == null) return false;
        var enclosingMethod = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (enclosingMethod != null
            && enclosingMethod.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword)))
            return false;
        if (IsHandleType(node.Type)) return false;
        return true;
    }

    /// <summary>
    /// Returns true when the type's simple name ends with `Handle` — treating it as a
    /// BC indirect-reference type that Microsoft's DLLs never wrap in ByRef&lt;&gt;.
    /// Generic type arguments are NOT recursed into; only the outer type's name matters.
    /// </summary>
    private static bool IsHandleType(TypeSyntax type)
    {
        string? name = type switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax q   => q.Right.Identifier.Text,
            GenericNameSyntax g     => g.Identifier.Text,
            _                       => null,
        };
        return name != null && name.EndsWith("Handle", StringComparison.Ordinal);
    }

    public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        var classDecl = node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl == null) return base.VisitFieldDeclaration(node);
        // Field declarations can declare multiple variables in one statement; if any
        // of them matches our wrap-set, rewrite the whole statement's type. (AL emits
        // one variable per field declaration, so this is fine in practice.)
        foreach (var v in node.Declaration.Variables)
        {
            var key = $"{classDecl.Identifier.Text}.{v.Identifier.Text}";
            if (_fieldsToWrap.Contains(key))
            {
                var newType = WrapInByRef(node.Declaration.Type);
                var newDecl = node.Declaration.WithType(newType.WithTriviaFrom(node.Declaration.Type));
                return node.WithDeclaration(newDecl);
            }
        }
        return base.VisitFieldDeclaration(node);
    }

    /// <summary>
    /// In `OnInvoke` dispatch sites, when calling one of the methods we wrapped, swap
    /// the unboxing of args at wrapped-parameter positions from
    ///   `ALCompiler.ObjectToExactXxx(args[i])`
    /// to
    ///   `(ByRef&lt;T&gt;)ALCompiler.SafeCastCheck&lt;ByRef&lt;T&gt;&gt;(args[i])`
    /// matching Microsoft's compiled-DLL convention.
    /// </summary>
    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var processed = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
        // Only rewrite calls whose target is `MethodName(...)` — bare method invocations
        // inside the same class. OnInvoke uses this form for case dispatch.
        if (processed.Expression is not IdentifierNameSyntax id) return processed;
        var classDecl = node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl == null) return processed;
        var key = $"{classDecl.Identifier.Text}.{id.Identifier.Text}";
        if (!_wrappedMethods.TryGetValue(key, out var wrappedPositions)) return processed;

        var newArgs = processed.ArgumentList.Arguments.ToList();
        foreach (var (pos, innerType) in wrappedPositions)
        {
            if (pos < 0 || pos >= newArgs.Count) continue;
            var oldArg = newArgs[pos];
            // Build  (ByRef<T>)global::Microsoft.Dynamics.Nav.Runtime.ALCompiler.SafeCastCheck<ByRef<T>>(<inner>)
            var byRefType = WrapInByRef(innerType);
            var inner = ExtractInnerArg(oldArg.Expression);
            var safeCast = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParseExpression("global::Microsoft.Dynamics.Nav.Runtime.ALCompiler"),
                    SyntaxFactory.GenericName(SyntaxFactory.Identifier("SafeCastCheck"))
                        .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(
                            SyntaxFactory.SingletonSeparatedList<TypeSyntax>(byRefType)))),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(inner))));
            var castExpr = SyntaxFactory.CastExpression(byRefType, safeCast);
            newArgs[pos] = oldArg.WithExpression(castExpr);
        }
        return processed.WithArgumentList(processed.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(newArgs)));
    }

    /// <summary>
    /// Given the original argument expression — typically a call like
    /// `ALCompiler.ObjectToExactNavCodeunitHandle(args[1])` — return the inner
    /// `args[i]` expression so we can wrap it in `SafeCastCheck&lt;ByRef&lt;T&gt;&gt;` instead.
    /// If the shape doesn't match (e.g. the arg is already a plain expression), return
    /// the expression unchanged.
    /// </summary>
    private static ExpressionSyntax ExtractInnerArg(ExpressionSyntax expr)
    {
        if (expr is InvocationExpressionSyntax inv && inv.ArgumentList.Arguments.Count >= 1)
        {
            // The AL emitter consistently produces `ALCompiler.ObjectToExactXxx(args[N])`
            // or `ALCompiler.ObjectToXxx(args[N])` for arg unboxing. Return args[N].
            var memberAccess = inv.Expression as MemberAccessExpressionSyntax;
            var name = memberAccess?.Name.Identifier.Text ?? string.Empty;
            if (name.StartsWith("ObjectTo") || name.StartsWith("Object"))
                return inv.ArgumentList.Arguments[0].Expression;
        }
        return expr;
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static bool HasNavByRef(ParameterSyntax node) =>
        node.AttributeLists
            .SelectMany(a => a.Attributes)
            .Any(at => IsNavByRefName(at.Name.ToString()));

    private static bool IsNavByRefName(string name) =>
        name == "NavByReference" || name == "NavByReferenceAttribute"
        || name.EndsWith(".NavByReference") || name.EndsWith(".NavByReferenceAttribute");

    private static GenericNameSyntax WrapInByRef(TypeSyntax t) =>
        SyntaxFactory.GenericName(SyntaxFactory.Identifier("ByRef"))
            .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(
                SyntaxFactory.SingletonSeparatedList<TypeSyntax>(t.WithoutTrivia())));

    /// <summary>
    /// First pass — walk the tree and record:
    ///   • (className, paramName) pairs where the parameter carries [NavByReferenceAttribute]
    ///     and the enclosing method isn't an override. The matching field name (same
    ///     identifier as the parameter) gets wrapped in pass two.
    ///   • (className+methodName) → list of (paramPosition, paramType) for every method
    ///     whose signature we'll wrap, so OnInvoke dispatch sites can be rewritten.
    /// </summary>
    private sealed class ByRefCollector : CSharpSyntaxWalker
    {
        public HashSet<string> FieldsToWrap { get; } = new();
        public Dictionary<string, List<(int pos, TypeSyntax innerType)>> WrappedMethods { get; } = new();

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            CollectFromMember(node, node.Identifier.Text, node.ParameterList);
            base.VisitMethodDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            CollectFromMember(node, node.Identifier.Text, node.ParameterList);
            base.VisitConstructorDeclaration(node);
        }

        private void CollectFromMember(SyntaxNode member, string memberName, ParameterListSyntax pl)
        {
            var classDecl = member.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDecl == null) return;
            List<(int, TypeSyntax)>? positions = null;
            for (int i = 0; i < pl.Parameters.Count; i++)
            {
                var p = pl.Parameters[i];
                if (ShouldWrap(p))
                {
                    FieldsToWrap.Add($"{classDecl.Identifier.Text}.{p.Identifier.Text}");
                    positions ??= new List<(int, TypeSyntax)>();
                    positions.Add((i, p.Type!));
                }
            }
            if (positions != null)
            {
                var key = $"{classDecl.Identifier.Text}.{memberName}";
                if (!WrappedMethods.ContainsKey(key))
                    WrappedMethods[key] = positions;
            }
        }
    }
}
