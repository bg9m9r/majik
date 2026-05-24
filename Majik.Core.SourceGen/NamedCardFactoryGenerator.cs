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
        var hasCreate = false;
        foreach (var member in cls.GetMembers("Create"))
        {
            if (member is not IMethodSymbol m) continue;
            if (!m.IsStatic) continue;
            if (m.DeclaredAccessibility != Accessibility.Public) continue;
            if (m.Parameters.Length == 0) continue;
            var first = m.Parameters[0];
            if (first.Type.Name != "Player") continue;
            var restOk = true;
            for (var i = 1; i < m.Parameters.Length; i++)
            {
                if (!m.Parameters[i].HasExplicitDefaultValue) { restOk = false; break; }
            }
            if (restOk) { hasCreate = true; break; }
        }

        return new FactoryEntry(
            fullyQualifiedName,
            displayName,
            names.ToImmutable(),
            hasCreate,
            location);
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<FactoryEntry> entries)
    {
        // Diagnostic — missing Create overload.
        foreach (var entry in entries)
        {
            if (!entry.HasCreateOverload)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    MissingCreateDescriptor,
                    entry.Location,
                    entry.DisplayName));
            }
        }

        // Build name → factory map, reporting duplicates.
        var map = new SortedDictionary<string, FactoryEntry>(StringComparer.Ordinal);
        var duplicates = new Dictionary<string, List<FactoryEntry>>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (!entry.HasCreateOverload) continue;
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
            sb.AppendLine($"            {literal} => {kvp.Value.FullyQualifiedName}.Create(owner),");
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
        Location? Location);
}
