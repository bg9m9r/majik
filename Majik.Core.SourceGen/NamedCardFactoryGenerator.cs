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
/// ## Parametric cycle factories
///
/// When a factory declares both <c>Create(Player owner)</c> and
/// <c>Create(Player owner, string[] args)</c>, the generator emits dispatch
/// arms that forward the per-attribute args array (the trailing params
/// after the card name on <c>[CardName(...)]</c>). This lets one factory
/// class implement an entire MTG card cycle (e.g. fetchlands, horizon
/// lands) parametrised by per-card constants.
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
            // When the caller passed no args at all, Roslyn surfaces this as
            // either an empty array constructor argument or no second arg.
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

        // Look for callable `Create(Player owner)` and `Create(Player, string[])`
        // overloads on the factory.
        var hasPlainCreate = false;
        var hasArgsCreate = false;
        foreach (var member in cls.GetMembers("Create"))
        {
            if (member is not IMethodSymbol m) continue;
            if (!m.IsStatic) continue;
            if (m.DeclaredAccessibility != Accessibility.Public) continue;
            if (m.Parameters.Length == 0) continue;
            var first = m.Parameters[0];
            if (first.Type.Name != "Player") continue;

            if (m.Parameters.Length == 1)
            {
                hasPlainCreate = true;
                continue;
            }

            // Detect Create(Player, string[]) overload.
            if (m.Parameters.Length == 2
                && m.Parameters[1].Type is IArrayTypeSymbol arr
                && arr.ElementType.SpecialType == SpecialType.System_String)
            {
                hasArgsCreate = true;
                continue;
            }

            // Other overloads with all-defaulted trailing params still
            // satisfy "has plain Create" since the dispatcher only passes owner.
            var restOk = true;
            for (var i = 1; i < m.Parameters.Length; i++)
            {
                if (!m.Parameters[i].HasExplicitDefaultValue) { restOk = false; break; }
            }
            if (restOk) hasPlainCreate = true;
        }

        return new FactoryEntry(
            fullyQualifiedName,
            displayName,
            registrations.ToImmutable(),
            hasPlainCreate,
            hasArgsCreate,
            location);
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<FactoryEntry> entries)
    {
        // Diagnostic — missing Create overload.
        foreach (var entry in entries)
        {
            if (!entry.HasPlainCreate && !entry.HasArgsCreate)
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
            if (!entry.HasPlainCreate && !entry.HasArgsCreate) continue;
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
            if (arm.Entry.HasArgsCreate)
            {
                // The dispatch arm prepends the card name as args[0] so the
                // factory can identify which member of its cycle is being
                // built — args[0] is always the printed card name, args[1..]
                // are the user-declared per-card payload from [CardName].
                var allArgs = new List<string> { kvp.Key };
                allArgs.AddRange(arm.Args);
                var argList = string.Join(
                    ", ",
                    allArgs.Select(a => SymbolDisplay.FormatLiteral(a, true)));
                call = $"{arm.Entry.FullyQualifiedName}.Create(owner, new[] {{ {argList} }})";
            }
            else
            {
                call = $"{arm.Entry.FullyQualifiedName}.Create(owner)";
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
        bool HasPlainCreate,
        bool HasArgsCreate,
        Location? Location);

    private sealed record DispatchArm(FactoryEntry Entry, ImmutableArray<string> Args);
}
