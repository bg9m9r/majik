using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Majik.Core.SourceGen;

/// <summary>
/// Roslyn incremental generator that scans the consuming compilation for
/// classes annotated with <c>[CardName("...")]</c> and emits a partial
/// <c>NamedCardFactory.CreateGenerated(string, Player)</c> method that
/// dispatches to each factory's static <c>Create(Player owner)</c> method.
///
/// Replaces the previous 317-arm hand-maintained <c>name switch</c> with
/// compile-time-generated code so that adding a new card no longer
/// requires editing a shared dispatch file.
///
/// ## Multi-[CardName] factories
///
/// A factory may carry multiple <c>[CardName]</c> attributes when it
/// serves functional reprints (e.g. Wrath of God + Damnation — same
/// resolve body, different printed name + cost). To produce the right
/// printed name per reprint, the factory exposes a
/// <c>public static &lt;Card&gt; Create(Player owner, string cardName)</c>
/// overload alongside the canonical <c>Create(Player owner)</c>. The
/// generator detects the named overload and emits dispatch arms of the
/// form <c>"Damnation" =&gt; F.Create(owner, "Damnation")</c>; single-name
/// factories without the overload continue to use the plain
/// <c>F.Create(owner)</c> shape unchanged.
///
/// ## Parametric cycle factories
///
/// When the same shape repeats across an MTG card cycle (e.g. fetchlands,
/// horizon lands) with per-card constants — basic-land subtypes, mana
/// colours, etc. — a factory can declare a
/// <c>Create(Player owner, string[] args)</c> overload and carry one
/// <c>[CardName(name, payload...)]</c> attribute per cycle member with
/// the per-card payload after the name. At dispatch time the generator
/// forwards the args array as <c>[name, payload...]</c> so the factory
/// can identify which cycle member is being built. Example:
/// <code>
/// [CardName("Bloodstained Mire", "Swamp", "Mountain")]
/// [CardName("Arid Mesa",         "Plains", "Mountain")]
/// public static class FetchLandCycleFactory
/// {
///     public static Land Create(Player owner) => Create(owner, new[] { ... });
///     public static Land Create(Player owner, string[] args) { /* args[0] = name */ }
/// }
/// </code>
/// The args-aware overload wins when both it and the named overload are
/// present.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class NamedCardFactoryGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "Majik.Core.CardData.Factories.CardNameAttribute";

    private const string DispatcherNamespace = "Majik.Core.CardData";

    private const string DispatcherClassName = "NamedCardFactory";

    private const string DispatcherMethodName = "CreateGenerated";

    private static readonly DiagnosticDescriptor DuplicateNameDescriptor = new(
        id: "MJK001",
        title: "Duplicate [CardName] across factories",
        messageFormat: "Card name \"{0}\" is claimed by multiple factories ({1}); each name must map to a single factory",
        category: "Majik.CardData",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingCreateDescriptor = new(
        id: "MJK002",
        title: "Factory missing Create(Player) overload",
        messageFormat: "Factory '{0}' is annotated with [CardName] but has no callable 'public static <Card> Create(Player owner)' overload",
        category: "Majik.CardData",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var factoryEntries = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => Transform(ctx))
            .Where(static entry => entry is not null)
            .Select(static (entry, _) => entry!)
            .Collect();

        context.RegisterSourceOutput(factoryEntries, Emit);
    }

    private static FactoryEntry? Transform(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol cls)
        {
            return null;
        }

        var fullyQualifiedName = cls.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var displayName = cls.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var location = cls.Locations.FirstOrDefault();

        var registrations = ImmutableArray.CreateBuilder<CardRegistration>();
        foreach (var attr in ctx.Attributes)
        {
            if (attr.ConstructorArguments.Length == 0) continue;
            var nameArg = attr.ConstructorArguments[0];
            if (nameArg.Value is not string s || string.IsNullOrWhiteSpace(s)) continue;

            // The attribute's second ctor parameter is `params string[] args`.
            // Roslyn surfaces it as either an array TypedConstant or no value
            // when the caller omitted it.
            var argsBuilder = ImmutableArray.CreateBuilder<string>();
            if (attr.ConstructorArguments.Length >= 2)
            {
                var argsArg = attr.ConstructorArguments[1];
                if (!argsArg.IsNull && argsArg.Kind == TypedConstantKind.Array)
                {
                    foreach (var v in argsArg.Values)
                    {
                        argsBuilder.Add(v.Value as string ?? string.Empty);
                    }
                }
            }

            registrations.Add(new CardRegistration(s, argsBuilder.ToImmutable()));
        }

        if (registrations.Count == 0)
        {
            return null;
        }

        // Look for callable Create overloads. Three shapes accepted:
        //   1. Create(Player owner)                          → plain
        //   2. Create(Player owner, string cardName, ...)    → reprint
        //   3. Create(Player owner, string[] args)           → parametric cycle
        // The args[] overload wins when present (most general).
        //
        // ALSO: scan for a parameterless static `CardDef Define()` (fluent
        // DSL opt-in). When present and no `Create(Player)` exists, the
        // generator synthesizes the dispatch arm by calling
        // `CardDefRuntime.Build(Factory.Define(), owner)` directly — the
        // factory class can omit `Create` entirely.
        var hasCreate = false;
        var hasNamedCreate = false;
        var hasArgsCreate = false;
        var hasDefine = false;
        foreach (var defineMember in cls.GetMembers("Define"))
        {
            if (defineMember is not IMethodSymbol dm) continue;
            if (!dm.IsStatic) continue;
            if (dm.DeclaredAccessibility != Accessibility.Public) continue;
            if (dm.Parameters.Length != 0) continue;
            // Return type must be the CardDef DSL type.
            if (dm.ReturnType.Name != "CardDef") continue;
            hasDefine = true;
            break;
        }
        foreach (var member in cls.GetMembers("Create"))
        {
            if (member is not IMethodSymbol m) continue;
            if (!m.IsStatic) continue;
            if (m.DeclaredAccessibility != Accessibility.Public) continue;
            if (m.Parameters.Length == 0) continue;
            var first = m.Parameters[0];
            if (first.Type.Name != "Player") continue;

            // Args[] overload — Create(Player, string[]).
            if (m.Parameters.Length == 2
                && m.Parameters[1].Type is IArrayTypeSymbol arr
                && arr.ElementType.SpecialType == SpecialType.System_String)
            {
                hasArgsCreate = true;
                continue;
            }

            // Plain `Create(Player owner, [defaults...])`.
            var restOk = true;
            for (var i = 1; i < m.Parameters.Length; i++)
            {
                if (!m.Parameters[i].HasExplicitDefaultValue) { restOk = false; break; }
            }
            if (restOk) hasCreate = true;

            // `Create(Player owner, string cardName, [defaults...])`.
            if (m.Parameters.Length >= 2
                && m.Parameters[1].Type.SpecialType == SpecialType.System_String)
            {
                var namedRestOk = true;
                for (var i = 2; i < m.Parameters.Length; i++)
                {
                    if (!m.Parameters[i].HasExplicitDefaultValue) { namedRestOk = false; break; }
                }
                if (namedRestOk) hasNamedCreate = true;
            }
        }

        return new FactoryEntry(
            fullyQualifiedName,
            displayName,
            registrations.ToImmutable(),
            hasCreate,
            hasNamedCreate,
            hasArgsCreate,
            hasDefine,
            location);
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<FactoryEntry> entries)
    {
        // Diagnostic — missing Create overload. Any of plain / named /
        // args-aware satisfies dispatch; a `CardDef Define()` also
        // satisfies it (the generator synthesizes the call to
        // `CardDefRuntime.Build`).
        foreach (var entry in entries)
        {
            if (!entry.HasCreateOverload
                && !entry.HasNamedCreateOverload
                && !entry.HasArgsCreateOverload
                && !entry.HasDefineMethod)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    MissingCreateDescriptor,
                    entry.Location,
                    entry.DisplayName));
            }
        }

        // Build name → (factory, args) map, reporting duplicates.
        var map = new SortedDictionary<string, DispatchArm>(StringComparer.Ordinal);
        var duplicates = new Dictionary<string, List<FactoryEntry>>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (!entry.HasCreateOverload
                && !entry.HasNamedCreateOverload
                && !entry.HasArgsCreateOverload
                && !entry.HasDefineMethod) continue;
            foreach (var reg in entry.Registrations)
            {
                if (map.TryGetValue(reg.Name, out var existing))
                {
                    if (!duplicates.TryGetValue(reg.Name, out var list))
                    {
                        list = new List<FactoryEntry> { existing.Entry };
                        duplicates[reg.Name] = list;
                    }
                    list.Add(entry);
                }
                else
                {
                    map[reg.Name] = new DispatchArm(entry, reg.Args);
                }
            }
        }

        foreach (var kvp in duplicates)
        {
            var names = string.Join(", ", kvp.Value.Select(e => e.DisplayName));
            spc.ReportDiagnostic(Diagnostic.Create(
                DuplicateNameDescriptor,
                kvp.Value[0].Location,
                kvp.Key,
                names));
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by Majik.Core.SourceGen.NamedCardFactoryGenerator.");
        sb.AppendLine("// Each arm is sourced from a [CardName(\"...\")] attribute on a factory class.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Majik.Core.Cards;");
        sb.AppendLine("using Majik.Core.Players;");
        sb.AppendLine();
        sb.AppendLine($"namespace {DispatcherNamespace};");
        sb.AppendLine();
        sb.AppendLine($"public static partial class {DispatcherClassName}");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>Total card names registered via <c>[CardName]</c> at compile time.</summary>");
        sb.AppendLine($"    public static int GeneratedRegistrationCount => {map.Count};");
        sb.AppendLine();
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// Compile-time-generated dispatch table. Returns the constructed");
        sb.AppendLine($"    /// card or <c>null</c> if <paramref name=\"name\"/> is not registered.");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    private static ICard? {DispatcherMethodName}(string name, Player owner)");
        sb.AppendLine("    {");
        sb.AppendLine("        return name switch");
        sb.AppendLine("        {");
        foreach (var kvp in map)
        {
            var literal = SymbolDisplay.FormatLiteral(kvp.Key, true);
            var arm = kvp.Value;
            string call;

            if (arm.Entry.HasArgsCreateOverload)
            {
                // Parametric cycle factory — emit args[] with the printed
                // name as args[0] and the per-card payload as args[1..].
                var allArgs = new List<string> { kvp.Key };
                allArgs.AddRange(arm.Args);
                var argList = string.Join(
                    ", ",
                    allArgs.Select(a => SymbolDisplay.FormatLiteral(a, true)));
                call = $"{arm.Entry.FullyQualifiedName}.Create(owner, new[] {{ {argList} }})";
            }
            else if (arm.Entry.HasNamedCreateOverload)
            {
                // Multi-[CardName] reprint factory — pass the printed name
                // so the canonical factory can mint each reprint distinctly.
                call = $"{arm.Entry.FullyQualifiedName}.Create(owner, {literal})";
            }
            else if (arm.Entry.HasCreateOverload)
            {
                call = $"{arm.Entry.FullyQualifiedName}.Create(owner)";
            }
            else
            {
                // Fluent-DSL factory — no Create at all, just a Define()
                // returning a CardDef. The generator synthesizes the
                // construction call here so the factory file can shrink
                // to the bare `Define()` body.
                call = $"global::Majik.Core.CardData.Definitions.CardDefRuntime.Build({arm.Entry.FullyQualifiedName}.Define(), owner)";
            }

            sb.AppendLine($"            {literal} => {call},");
        }
        sb.AppendLine("            _ => null,");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("NamedCardFactory.Generated.g.cs", sb.ToString());
    }

    private sealed record CardRegistration(string Name, ImmutableArray<string> Args);

    private sealed record FactoryEntry(
        string FullyQualifiedName,
        string DisplayName,
        ImmutableArray<CardRegistration> Registrations,
        bool HasCreateOverload,
        bool HasNamedCreateOverload,
        bool HasArgsCreateOverload,
        bool HasDefineMethod,
        Location? Location);

    private sealed record DispatchArm(FactoryEntry Entry, ImmutableArray<string> Args);
}
