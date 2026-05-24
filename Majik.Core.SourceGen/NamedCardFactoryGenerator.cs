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

        var names = ImmutableArray.CreateBuilder<string>();
        foreach (var attr in ctx.Attributes)
        {
            if (attr.ConstructorArguments.Length == 0) continue;
            var arg = attr.ConstructorArguments[0];
            if (arg.Value is string s && !string.IsNullOrWhiteSpace(s))
            {
                names.Add(s);
            }
        }

        if (names.Count == 0)
        {
            return null;
        }

        // Look for a callable `Create(Player owner)` overload — single-arg
        // or any overload whose remaining parameters all have defaults.
        // Separately detect a `Create(Player owner, string cardName, ...)`
        // overload (name as the second positional parameter, any remaining
        // parameters defaulted). When present, multi-[CardName] factories
        // dispatch through it so the canonical factory can produce the
        // right printed name for each functional reprint.
        var hasCreate = false;
        var hasNamedCreate = false;
        foreach (var member in cls.GetMembers("Create"))
        {
            if (member is not IMethodSymbol m) continue;
            if (!m.IsStatic) continue;
            if (m.DeclaredAccessibility != Accessibility.Public) continue;
            if (m.Parameters.Length == 0) continue;
            var first = m.Parameters[0];
            if (first.Type.Name != "Player") continue;

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
            names.ToImmutable(),
            hasCreate,
            hasNamedCreate,
            location);
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<FactoryEntry> entries)
    {
        // Diagnostic — missing Create overload. Either the plain
        // `Create(Player)` or the named `Create(Player, string, ...)` form
        // satisfies dispatch.
        foreach (var entry in entries)
        {
            if (!entry.HasCreateOverload && !entry.HasNamedCreateOverload)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    MissingCreateDescriptor,
                    entry.Location,
                    entry.DisplayName));
            }
        }

        // Build name → factory map, reporting duplicates. A factory is
        // eligible if it has either the plain `Create(Player)` overload
        // or the named `Create(Player, string, ...)` overload — the
        // dispatcher picks the latter when available so multi-[CardName]
        // factories can produce the correct printed name per reprint.
        var map = new SortedDictionary<string, FactoryEntry>(StringComparer.Ordinal);
        var duplicates = new Dictionary<string, List<FactoryEntry>>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (!entry.HasCreateOverload && !entry.HasNamedCreateOverload) continue;
            foreach (var name in entry.CardNames)
            {
                if (map.TryGetValue(name, out var existing))
                {
                    if (!duplicates.TryGetValue(name, out var list))
                    {
                        list = new List<FactoryEntry> { existing };
                        duplicates[name] = list;
                    }
                    list.Add(entry);
                }
                else
                {
                    map[name] = entry;
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
            // Prefer the `Create(Player, string)` overload when the factory
            // exposes one — multi-[CardName] factories use the second
            // argument to produce the correct printed name per reprint.
            // Fall back to the plain `Create(Player)` otherwise so existing
            // single-name factories stay unchanged.
            var call = kvp.Value.HasNamedCreateOverload
                ? $"{kvp.Value.FullyQualifiedName}.Create(owner, {literal})"
                : $"{kvp.Value.FullyQualifiedName}.Create(owner)";
            sb.AppendLine($"            {literal} => {call},");
        }
        sb.AppendLine("            _ => null,");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("NamedCardFactory.Generated.g.cs", sb.ToString());
    }

    private sealed record FactoryEntry(
        string FullyQualifiedName,
        string DisplayName,
        ImmutableArray<string> CardNames,
        bool HasCreateOverload,
        bool HasNamedCreateOverload,
        Location? Location);
}
