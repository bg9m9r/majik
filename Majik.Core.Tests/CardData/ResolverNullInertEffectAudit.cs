using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Source analyzer for the <b>"bound-but-inert (resolver-null)"</b> card-factory
/// bug class fixed in #2540 / #2543 / #2549 / #2551.
///
/// <para>
/// A wide family of <c>[CardName]</c> factories captured an each-opponent /
/// each-player "resolver" — a <c>Func&lt;IReadOnlyList&lt;Player&gt;&gt;</c> (or a
/// <c>Func&lt;…Permanent…&gt;</c>) supplied at factory-build time — and invoked it
/// from inside the <b>resolution closure</b> of an <c>Effect</c> / <c>Fx.Inline</c>.
/// The production routed build (<c>GameFacade.BuildDeckCard →
/// NamedCardFactory.Create(name, owner[, effects])</c>) dispatches the single-arg /
/// effects-aware overload, which passes that resolver <b>null</b> — so the
/// each-opponent / each-player clause was <b>inert in real games</b> (only
/// resolver-injecting factory-direct tests ever saw it run). The ability itself
/// was live on the routed build (auto-bound by <c>TriggerManager.BindCard</c> /
/// loyalty dispatch), so only the effect body was dead.
/// </para>
///
/// <para>
/// The correct pattern reads the live players off the
/// <see cref="ResolutionContext"/> that <c>ResolveAsync</c> already threads in —
/// via the shared <c>ContextOpponents.Of(ctx, controller)</c> helper (or a direct
/// <c>rc.Game</c> / <c>ctx.Game</c> read). This analyzer flags the inert signature
/// so it can't regress and any straggler surfaces.
/// </para>
///
/// <para><b>The flagged triple</b> (all three must hold for a factory file):</para>
/// <list type="number">
///   <item>(a) declares a parameter that is a <c>Func&lt;…&gt;</c> returning a
///     collection of <c>Player</c> or of a <c>…Permanent…</c> type — an
///     "opponents / players / targets resolver"; AND</item>
///   <item>(b) invokes that resolver (<c>resolver?.Invoke()</c> / <c>resolver()</c>)
///     from inside an <c>Effect</c> / <c>Fx.Inline</c> resolution closure — i.e. it
///     runs at resolution (directly, or via a private helper method that closure
///     calls); AND</item>
///   <item>(c) the factory does NOT read <c>rc.Game</c> / <c>ctx.Game</c> /
///     <c>ContextOpponents</c> / <c>ResolutionContext</c> anywhere — i.e. it relies
///     solely on the (null-on-prod) resolver, with no context fallback.</item>
/// </list>
/// </summary>
internal static class ResolverNullInertEffectAudit
{
    /// <summary>A single flagged factory + the resolver param it relies on.</summary>
    public sealed record Violation(string FileName, string ResolverParam, string Detail);

    // Collection generics whose element type we treat as a "players / permanents"
    // resolver payload. CR-irrelevant — purely the C# shapes the bug used.
    private static readonly string[] CollectionGenerics =
    {
        "IReadOnlyList", "IEnumerable", "IReadOnlyCollection", "ICollection", "List",
    };

    /// <summary>
    /// Analyze a single factory source string. Returns the violations (0 or more —
    /// a factory could declare more than one inert resolver, though in practice
    /// it's one).
    /// </summary>
    public static IReadOnlyList<Violation> Analyze(string source, string fileName)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        // ---- (c) context fallback? If the factory reads the live resolution
        //      context ANYWHERE, it has a fallback and is not inert. ----------
        if (ReadsResolutionContext(root))
            return Array.Empty<Violation>();

        // ---- (a) resolver params: Func<Collection<Player|...Permanent...>> -----
        var resolverParams = root.DescendantNodes()
            .OfType<ParameterSyntax>()
            .Where(p => p.Type != null && IsResolverFuncType(p.Type))
            .Select(p => p.Identifier.ValueText)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.Ordinal);

        if (resolverParams.Count == 0)
            return Array.Empty<Violation>();

        // ---- Build the set of method/local-function bodies reachable from an
        //      Effect / Fx.Inline resolution closure, then check (b): does any
        //      reachable body invoke a resolver param? -------------------------
        var reachable = CollectEffectReachableBodies(root);

        var violations = new List<Violation>();
        foreach (var param in resolverParams.OrderBy(p => p, StringComparer.Ordinal))
        {
            if (InvokesResolverInBodies(reachable, param))
            {
                violations.Add(new Violation(
                    fileName,
                    param,
                    $"resolver '{param}' is invoked inside an Effect/Fx.Inline resolution " +
                    "closure but the factory never reads rc.Game/ctx.Game/ContextOpponents/" +
                    "ResolutionContext — the prod routed build passes it null, so the effect " +
                    "body is inert in real games (resolver-null bug class, see #2551). " +
                    "Read the live game off the ResolutionContext (ContextOpponents.Of) instead."));
            }
        }
        return violations;
    }

    // ----------------------------------------------------------------------- //

    /// <summary>(c) — any read of the live resolution context anywhere in the file.</summary>
    private static bool ReadsResolutionContext(SyntaxNode root)
    {
        // ContextOpponents.Of(...) / ContextOpponents anywhere.
        foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (id.Identifier.ValueText == "ContextOpponents") return true;
            if (id.Identifier.ValueText == "ResolutionContext") return true;
        }

        // rc.Game / ctx.Game member access (the threaded-context read). Also
        // accept any "<ctxParam>.Game" where the receiver is a lambda parameter
        // literally named rc/ctx/resolutionContext/context — the established
        // fixed shape (ctx => … ctx.Game … / rc => … rc.Game …).
        foreach (var ma in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (ma.Name.Identifier.ValueText != "Game") continue;
            if (ma.Expression is IdentifierNameSyntax recv)
            {
                var r = recv.Identifier.ValueText;
                if (r is "rc" or "ctx" or "context" or "resolutionContext" or "resolveContext")
                    return true;
            }
        }
        return false;
    }

    /// <summary>(a) — Func&lt;Collection&lt;Player | …Permanent…&gt;&gt;.</summary>
    private static bool IsResolverFuncType(TypeSyntax type)
    {
        // Unwrap nullable (Func<...>?).
        if (type is NullableTypeSyntax nt) type = nt.ElementType;

        if (type is not GenericNameSyntax g) return false;
        if (g.Identifier.ValueText != "Func") return false;
        if (g.TypeArgumentList.Arguments.Count != 1) return false;

        var arg = g.TypeArgumentList.Arguments[0];
        if (arg is NullableTypeSyntax argNt) arg = argNt.ElementType;
        if (arg is not GenericNameSyntax inner) return false;

        if (!CollectionGenerics.Contains(inner.Identifier.ValueText)) return false;
        if (inner.TypeArgumentList.Arguments.Count != 1) return false;

        // Element type text contains Player or Permanent (covers Creature/Land
        // etc. only when literally "Permanent"; targeting creature/artifact
        // resolvers use Creature/Artifact element types and are deliberately NOT
        // matched — they are wired through the targeting system, not the
        // each-player effect-body drain).
        var elementText = inner.TypeArgumentList.Arguments[0].ToString();
        return elementText == "Player"
            || elementText.Contains("Permanent", StringComparison.Ordinal);
    }

    /// <summary>
    /// Collect every method/local-function body transitively reachable from a
    /// lambda passed to <c>new Effect(...)</c> or <c>Fx.Inline(...)</c> — i.e. the
    /// code that runs at effect resolution. Returns the syntax nodes of those
    /// bodies (the effect closures themselves plus any private helper bodies they
    /// call, by name, within this same file).
    /// </summary>
    private static List<SyntaxNode> CollectEffectReachableBodies(SyntaxNode root)
    {
        // Index helper methods + local functions by name so we can follow calls.
        var methodsByName = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .GroupBy(m => m.Identifier.ValueText, StringComparer.Ordinal)
            .ToDictionary(grp => grp.Key, grp => grp.ToList(), StringComparer.Ordinal);

        var localFuncsByName = root.DescendantNodes()
            .OfType<LocalFunctionStatementSyntax>()
            .GroupBy(m => m.Identifier.ValueText, StringComparer.Ordinal)
            .ToDictionary(grp => grp.Key, grp => grp.ToList(), StringComparer.Ordinal);

        // Roots: lambdas that are arguments to `new Effect(...)` / `Fx.Inline(...)`.
        var seeds = new List<SyntaxNode>();
        foreach (var node in root.DescendantNodes())
        {
            ArgumentListSyntax? argList = node switch
            {
                ObjectCreationExpressionSyntax oc when TypeNameIs(oc.Type, "Effect") => oc.ArgumentList,
                InvocationExpressionSyntax inv when IsFxInline(inv) => inv.ArgumentList,
                _ => null,
            };
            if (argList == null) continue;

            foreach (var a in argList.Arguments)
            {
                if (a.Expression is LambdaExpressionSyntax lambda)
                    seeds.Add(lambda);
            }
        }

        // BFS over called method/local-function names within the file.
        var visited = new List<SyntaxNode>();
        var seenMethods = new HashSet<SyntaxNode>();
        var queue = new Queue<SyntaxNode>(seeds);
        visited.AddRange(seeds);

        while (queue.Count > 0)
        {
            var body = queue.Dequeue();
            foreach (var call in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var calleeName = CalleeSimpleName(call.Expression);
                if (calleeName == null) continue;

                if (methodsByName.TryGetValue(calleeName, out var ms))
                {
                    foreach (var m in ms)
                    {
                        if (seenMethods.Add(m)) { visited.Add(m); queue.Enqueue(m); }
                    }
                }
                if (localFuncsByName.TryGetValue(calleeName, out var lfs))
                {
                    foreach (var lf in lfs)
                    {
                        if (seenMethods.Add(lf)) { visited.Add(lf); queue.Enqueue(lf); }
                    }
                }
            }

            // A method passed BY REFERENCE as the canActivateCheck / payer arg
            // (e.g. `canActivateCheck: CanActivate`) is a non-Effect gate — we do
            // NOT follow method-group references here, only direct call sites,
            // so canActivateCheck / additionalCostPayer bodies are excluded from
            // the Effect-reachable set unless an Effect closure actually calls them.
        }

        return visited;
    }

    /// <summary>(b) — does any reachable body invoke <paramref name="param"/>?</summary>
    private static bool InvokesResolverInBodies(IReadOnlyList<SyntaxNode> bodies, string param)
    {
        foreach (var body in bodies)
        {
            foreach (var inv in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                // resolver?.Invoke()  /  resolver.Invoke()
                if (inv.Expression is MemberAccessExpressionSyntax ma
                    && ma.Name.Identifier.ValueText == "Invoke"
                    && ReceiverIsIdentifier(ma.Expression, param))
                {
                    return true;
                }
                // resolver()  (direct delegate invocation)
                if (inv.Expression is IdentifierNameSyntax id
                    && id.Identifier.ValueText == param)
                {
                    return true;
                }
            }
            // resolver?.Invoke() lowers to a ConditionalAccessExpression:
            //   resolver  ?  .Invoke()
            foreach (var cond in body.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>())
            {
                if (cond.Expression is IdentifierNameSyntax cid
                    && cid.Identifier.ValueText == param
                    && cond.WhenNotNull is InvocationExpressionSyntax wnInv
                    && wnInv.Expression is MemberBindingExpressionSyntax mb
                    && mb.Name.Identifier.ValueText == "Invoke")
                {
                    return true;
                }
            }
        }
        return false;
    }

    // --------------------------- syntax helpers ---------------------------- //

    private static bool ReceiverIsIdentifier(ExpressionSyntax expr, string name)
    {
        // For `resolver.Invoke()` the receiver is an IdentifierName; for
        // `resolver?.Invoke()` it is handled via ConditionalAccess above.
        return expr is IdentifierNameSyntax id && id.Identifier.ValueText == name;
    }

    private static bool TypeNameIs(TypeSyntax type, string simpleName)
    {
        return type switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText == simpleName,
            QualifiedNameSyntax q => q.Right.Identifier.ValueText == simpleName,
            GenericNameSyntax g => g.Identifier.ValueText == simpleName,
            _ => false,
        };
    }

    private static bool IsFxInline(InvocationExpressionSyntax inv)
    {
        // Fx.Inline(...) — member access with member name "Inline" on receiver "Fx".
        return inv.Expression is MemberAccessExpressionSyntax ma
            && ma.Name.Identifier.ValueText == "Inline"
            && ma.Expression is IdentifierNameSyntax recv
            && recv.Identifier.ValueText == "Fx";
    }

    /// <summary>Simple (unqualified) name of a call target, or null for member calls we don't follow.</summary>
    private static string? CalleeSimpleName(ExpressionSyntax callee)
    {
        return callee switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            // this.Helper(...) / TypeName.Helper(...) — follow the simple right name
            // so static private helpers in the same factory are reachable.
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            _ => null,
        };
    }
}
