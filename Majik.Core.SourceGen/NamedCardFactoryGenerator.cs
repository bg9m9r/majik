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

        // PLAN 03 Slice 3 — fileless JSON cards. The consuming project
        // registers `CardData/Cards/*.json` as <AdditionalFiles>; for each
        // we extract the slug (file stem, which is also the embedded-resource
        // id used by CardDefinitionLoader.FromEmbeddedResource) and the card
        // `name`. A JSON card whose name is NOT claimed by a hand-written
        // [CardName] factory gets a generated dispatch arm that loads the
        // embedded resource and builds it — exactly what the old wrapper
        // factory did — so the wrapper file can be deleted.
        var jsonCards = context.AdditionalTextsProvider
            .Where(static text => IsCardJson(text.Path))
            .Select(static (text, ct) => JsonCardEntry.From(text, ct))
            .Where(static entry => entry is not null)
            .Select(static (entry, _) => entry!)
            .Collect();

        var combined = factoryEntries.Combine(jsonCards);

        context.RegisterSourceOutput(
            combined,
            static (spc, pair) => Emit(spc, pair.Left, pair.Right));
    }

    /// <summary>
    /// True for an embedded card-definition JSON — a file living under a
    /// <c>CardData/Cards/</c> directory with a <c>.json</c> extension. The
    /// match is path-segment based so it does not fire on unrelated JSON
    /// (e.g. test fixtures or the gzipped modern seed).
    /// </summary>
    private static bool IsCardJson(string path)
    {
        if (path is null) return false;
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
        var normalized = path.Replace('\\', '/');
        return normalized.IndexOf("/CardData/Cards/", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.StartsWith("CardData/Cards/", StringComparison.OrdinalIgnoreCase);
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

    private static readonly DiagnosticDescriptor DuplicateJsonNameDescriptor = new(
        id: "MJK003",
        title: "Duplicate card name across JSON definitions",
        messageFormat: "Card name \"{0}\" is declared by multiple JSON definitions ({1}); each name must map to a single embedded resource",
        category: "Majik.CardData",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static void Emit(
        SourceProductionContext spc,
        ImmutableArray<FactoryEntry> entries,
        ImmutableArray<JsonCardEntry> jsonCards)
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

        // Build name -> slug for the fileless JSON cards. A JSON card whose
        // name is ALSO claimed by a [CardName] factory keeps the factory arm
        // (the factory may layer bespoke logic on top of the JSON shell —
        // e.g. cycling, MDFC transform); only JSON names with NO surviving
        // factory get a generated load-and-build arm. JSON-vs-JSON name
        // collisions are a hard error (two files can't both own one name).
        var jsonMap = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var jsonDuplicates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var jc in jsonCards)
        {
            if (jsonMap.TryGetValue(jc.Name, out var existingSlug))
            {
                if (!jsonDuplicates.TryGetValue(jc.Name, out var list))
                {
                    list = new List<string> { existingSlug };
                    jsonDuplicates[jc.Name] = list;
                }
                list.Add(jc.Slug);
            }
            else
            {
                jsonMap[jc.Name] = jc.Slug;
            }
        }

        foreach (var kvp in jsonDuplicates)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                DuplicateJsonNameDescriptor,
                Location.None,
                kvp.Key,
                string.Join(", ", kvp.Value.Select(s => s + ".json"))));
        }

        // Fileless arms = JSON names with no [CardName] factory claiming them.
        var filelessJson = jsonMap
            .Where(kvp => !map.ContainsKey(kvp.Key))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by Majik.Core.SourceGen.NamedCardFactoryGenerator.");
        sb.AppendLine("// Arms are sourced from [CardName(\"...\")] attributes on factory classes");
        sb.AppendLine("// and from fileless CardData/Cards/*.json definitions (no wrapper class).");
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
        sb.AppendLine($"    /// <summary>Total fileless JSON card names dispatched without a wrapper factory.</summary>");
        sb.AppendLine($"    public static int GeneratedJsonCardCount => {filelessJson.Count};");
        sb.AppendLine();
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// Card names sourced directly from an embedded <c>CardData/Cards/*.json</c>");
        sb.AppendLine($"    /// definition with no hand-written <c>[CardName]</c> wrapper factory. These");
        sb.AppendLine($"    /// names are folded into <c>ImplementedCardNames.All</c> so deleting the");
        sb.AppendLine($"    /// wrappers does not regress the implemented-name set (PLAN 03 Slice 3).");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public static readonly string[] GeneratedJsonCardNames =");
        sb.AppendLine("    {");
        foreach (var kvp in filelessJson)
        {
            sb.AppendLine($"        {SymbolDisplay.FormatLiteral(kvp.Key, true)},");
        }
        sb.AppendLine("    };");
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

        // Fileless JSON arms. Equivalent to the deleted wrapper factory:
        //   (Cast)CardDefinitionFactory.Build(FromEmbeddedResource("slug"), owner)
        // — only the dispatch is generated; the JSON stays an EmbeddedResource
        // so runtime loading is byte-identical to the wrapper path.
        foreach (var kvp in filelessJson)
        {
            var literal = SymbolDisplay.FormatLiteral(kvp.Key, true);
            var slugLiteral = SymbolDisplay.FormatLiteral(kvp.Value, true);
            var call =
                "global::Majik.Core.CardData.Definitions.CardDefinitionFactory.Build(" +
                "global::Majik.Core.CardData.Definitions.CardDefinitionLoader.FromEmbeddedResource(" +
                $"{slugLiteral}), owner)";
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

    /// <summary>
    /// A fileless JSON card definition. <see cref="Slug"/> is the file stem
    /// (also the embedded-resource id consumed by
    /// <c>CardDefinitionLoader.FromEmbeddedResource</c>); <see cref="Name"/>
    /// is the printed card name read from the JSON <c>"name"</c> field.
    /// </summary>
    private sealed record JsonCardEntry(string Slug, string Name)
    {
        public static JsonCardEntry? From(AdditionalText text, System.Threading.CancellationToken ct)
        {
            var slug = SlugOf(text.Path);
            if (string.IsNullOrEmpty(slug)) return null;

            var content = text.GetText(ct)?.ToString();
            if (string.IsNullOrEmpty(content)) return null;

            var name = ExtractName(content!);
            if (string.IsNullOrWhiteSpace(name)) return null;

            return new JsonCardEntry(slug, name!);
        }

        /// <summary>File name without the <c>.json</c> extension.</summary>
        private static string SlugOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            var normalized = path.Replace('\\', '/');
            var lastSlash = normalized.LastIndexOf('/');
            var file = lastSlash >= 0 ? normalized.Substring(lastSlash + 1) : normalized;
            return file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? file.Substring(0, file.Length - ".json".Length)
                : file;
        }

        /// <summary>
        /// Pull the top-level <c>"name": "..."</c> value out of a card JSON.
        /// The generator targets netstandard2.0 (no System.Text.Json), and
        /// the card schema always declares <c>name</c> as a top-level string,
        /// so a focused scan that respects JSON string escaping is sufficient
        /// and avoids a parser dependency.
        /// </summary>
        private static string? ExtractName(string json)
        {
            const string key = "\"name\"";
            var idx = json.IndexOf(key, StringComparison.Ordinal);
            while (idx >= 0)
            {
                var i = idx + key.Length;
                // Skip whitespace then the ':'.
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i < json.Length && json[i] == ':')
                {
                    i++;
                    while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                    if (i < json.Length && json[i] == '"')
                    {
                        return ReadJsonString(json, i + 1);
                    }
                }
                idx = json.IndexOf(key, idx + key.Length, StringComparison.Ordinal);
            }
            return null;
        }

        /// <summary>Read a JSON string body starting just past the opening
        /// quote, honouring the standard escape sequences.</summary>
        private static string? ReadJsonString(string json, int start)
        {
            var sb = new StringBuilder();
            var i = start;
            while (i < json.Length)
            {
                var ch = json[i];
                if (ch == '\\')
                {
                    if (i + 1 >= json.Length) return null;
                    var esc = json[i + 1];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 5 < json.Length
                                && int.TryParse(
                                    json.Substring(i + 2, 4),
                                    System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(esc); break;
                    }
                    i += 2;
                    continue;
                }
                if (ch == '"')
                {
                    return sb.ToString();
                }
                sb.Append(ch);
                i++;
            }
            return null;
        }
    }
}
