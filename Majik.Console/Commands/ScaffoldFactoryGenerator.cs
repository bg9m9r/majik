using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Majik.Core.CardData.Database;

namespace Majik.Console.Commands;

/// <summary>
/// Pure logic for the <c>scaffold-factory</c> subcommand. Produces a starter
/// <c>*Factory.cs</c> source file from a <see cref="CardEntity"/>. No I/O —
/// caller writes the returned <see cref="Result"/> to disk after coverage /
/// overwrite gates.
///
/// Scope is deliberately narrow: we generate the constructor + ctor args
/// derived from the Scryfall type line + mana cost, plus an oracle-text
/// docstring + best-effort CR-rule hint. The author fills in <c>// TODO</c>
/// blocks to wire abilities. Goal: cut per-card writing cost by ~50% by
/// eliminating the boilerplate.
/// </summary>
public static class ScaffoldFactoryGenerator
{
    /// <summary>Result of a scaffold-generation pass.</summary>
    public sealed record Result(string Slug, string FileName, string SourceText);

    /// <summary>
    /// Slugify a printed card name into a PascalCase factory identifier.
    /// Strips all non-alphanumeric punctuation, splits on whitespace, and
    /// concatenates word-by-word with the first letter of each word upper-
    /// cased.
    ///
    /// Examples (verified against existing factories on disk):
    /// <list type="bullet">
    ///   <item>"Yawgmoth, Thran Physician" → "YawgmothThranPhysician"</item>
    ///   <item>"Ashiok, Dream Render"      → "AshiokDreamRender"</item>
    ///   <item>"Sol Ring"                  → "SolRing"</item>
    ///   <item>"Wrath of God"              → "WrathOfGod"</item>
    ///   <item>"Urza's Mine"               → "UrzasMine"</item>
    /// </list>
    /// </summary>
    public static string Slugify(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName))
            throw new ArgumentException("cardName required", nameof(cardName));

        // Strip everything that's not alnum or whitespace. Apostrophes,
        // commas, em-dashes, and ASCII hyphens all collapse to nothing
        // ("Urza's" → "Urzas", "Jace, the Mind Sculptor" → "Jace the Mind Sculptor").
        var stripped = new StringBuilder(cardName.Length);
        foreach (var ch in cardName)
        {
            if (char.IsLetterOrDigit(ch)) stripped.Append(ch);
            else if (char.IsWhiteSpace(ch)) stripped.Append(' ');
            // everything else (',', '\'', '-', '—', '.', ':') is dropped
        }

        var pieces = stripped.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var sb = new StringBuilder();
        var ti = CultureInfo.InvariantCulture.TextInfo;
        foreach (var p in pieces)
        {
            sb.Append(ti.ToUpper(p[0]));
            if (p.Length > 1) sb.Append(p[1..]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Build the file name for a slug — slug + "Factory.cs". Kept as a
    /// helper so test fixtures don't have to know the suffix convention.
    /// </summary>
    public static string FileNameFor(string slug) => slug + "Factory.cs";

    /// <summary>
    /// Generate the starter factory file for a <see cref="CardEntity"/>.
    /// </summary>
    public static Result Generate(CardEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (string.IsNullOrWhiteSpace(entity.Name))
            throw new ArgumentException("entity.Name required", nameof(entity));

        var slug = Slugify(entity.Name);
        var fileName = FileNameFor(slug);
        var className = slug + "Factory";

        var parsed = Majik.Core.CardData.TypeLineParser.Parse(entity.TypeLine ?? "");
        var primary = PickPrimaryType(parsed.Types);
        var manaCost = entity.ManaCost ?? "";

        var ctor = BuildConstructor(entity, primary, parsed, manaCost);
        var returnType = ReturnTypeFor(primary);
        var oracleText = entity.OracleText ?? "";
        var rulesHint = RuleHintFor(primary, oracleText);

        var sb = new StringBuilder();
        sb.AppendLine("using Majik.Core.Abilities;");
        sb.AppendLine("using Majik.Core.Cards;");
        sb.AppendLine("using Majik.Core.Cards.Types;");
        sb.AppendLine("using Majik.Core.CardData.Factories;");
        sb.AppendLine("using Majik.Core.Players;");
        sb.AppendLine();
        sb.AppendLine("namespace Majik.Core.CardData.Factories;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Named-card factory for {entity.Name} ({entity.TypeLine ?? "?"}{(string.IsNullOrEmpty(manaCost) ? "" : ", " + manaCost)}).");
        sb.AppendLine("///");
        sb.AppendLine("/// Oracle text:");
        if (string.IsNullOrWhiteSpace(oracleText))
        {
            sb.AppendLine("///   (vanilla — no printed oracle text)");
        }
        else
        {
            foreach (var line in oracleText.Replace("\r\n", "\n").Split('\n'))
            {
                sb.AppendLine($"///   {line}");
            }
        }
        sb.AppendLine("///");
        sb.AppendLine($"/// {rulesHint}");
        sb.AppendLine("///");
        sb.AppendLine("/// TODO: replace this docstring with a real implementation summary once");
        sb.AppendLine("/// the abilities / effects below are wired up.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"[CardName(\"{EscapeCSharpStringLiteral(entity.Name)}\")]");
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");
        sb.AppendLine($"    public const string CardName = \"{EscapeCSharpStringLiteral(entity.Name)}\";");
        if (primary != PrimaryType.Land)
        {
            sb.AppendLine($"    public const string PrintedManaCost = \"{manaCost}\";");
        }
        sb.AppendLine();
        sb.AppendLine($"    public static {returnType} Create(Player owner)");
        sb.AppendLine("    {");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(owner);");
        sb.AppendLine();
        sb.AppendLine($"        var card = {ctor};");
        sb.AppendLine("        card.SetOwner(owner);");
        if (primary != PrimaryType.Instant && primary != PrimaryType.Sorcery)
        {
            sb.AppendLine("        card.SetController(owner);");
        }
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(oracleText))
        {
            sb.AppendLine("        // TODO: resolve body — translate the oracle text into abilities /");
            sb.AppendLine("        //       triggers / replacements / continuous effects. See");
            sb.AppendLine("        //       neighbouring factories for shape (e.g. SolRingFactory for a");
            sb.AppendLine("        //       single mana ability; LightningHelixFactory for a spell body).");
            sb.AppendLine("        //");
            sb.AppendLine("        //       Oracle: " + Inline(oracleText));
        }
        else
        {
            sb.AppendLine("        // Vanilla card — no abilities to wire.");
        }
        sb.AppendLine();
        sb.AppendLine("        return card;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new Result(slug, fileName, sb.ToString());
    }

    /// <summary>
    /// Primary card type for factory ctor selection. Mirrors
    /// <c>ScryfallCardFactory.PickPrimaryType</c> — Land &gt; Creature &gt;
    /// Planeswalker &gt; Instant &gt; Sorcery &gt; Enchantment &gt; Artifact.
    /// </summary>
    public enum PrimaryType
    {
        Artifact,
        Creature,
        Enchantment,
        Instant,
        Land,
        Planeswalker,
        Sorcery,
    }

    /// <summary>Test seam — primary-type pick from a parsed type list.</summary>
    public static PrimaryType PickPrimaryType(IReadOnlyList<Majik.Core.Cards.Types.CardType> types)
    {
        // Same preference order as ScryfallCardFactory.PickPrimaryType.
        if (types.Contains(Majik.Core.Cards.Types.CardType.Land))        return PrimaryType.Land;
        if (types.Contains(Majik.Core.Cards.Types.CardType.Creature))    return PrimaryType.Creature;
        if (types.Contains(Majik.Core.Cards.Types.CardType.Planeswalker)) return PrimaryType.Planeswalker;
        if (types.Contains(Majik.Core.Cards.Types.CardType.Instant))     return PrimaryType.Instant;
        if (types.Contains(Majik.Core.Cards.Types.CardType.Sorcery))     return PrimaryType.Sorcery;
        if (types.Contains(Majik.Core.Cards.Types.CardType.Enchantment)) return PrimaryType.Enchantment;
        return PrimaryType.Artifact;
    }

    private static string ReturnTypeFor(PrimaryType primary) => primary switch
    {
        PrimaryType.Creature     => "Creature",
        PrimaryType.Land         => "Land",
        PrimaryType.Instant      => "Instant",
        PrimaryType.Sorcery      => "Sorcery",
        PrimaryType.Enchantment  => "Enchantment",
        PrimaryType.Artifact     => "Artifact",
        PrimaryType.Planeswalker => "Planeswalker",
        _ => "ICard",
    };

    /// <summary>
    /// Build the new-up expression for the chosen card constructor. Includes
    /// the parsed supertypes/subtypes lists when the ctor accepts them.
    /// Power/Toughness/Loyalty are pulled from <paramref name="entity"/>.
    /// </summary>
    public static string BuildConstructor(
        CardEntity entity,
        PrimaryType primary,
        Majik.Core.CardData.TypeLineParser.ParsedTypeLine parsed,
        string manaCost)
    {
        var supertypesArg = FormatEnumList("CardSupertype", parsed.Supertypes.Select(s => s.ToString()));
        var subtypesArg = FormatEnumList("CardSubtype", parsed.Subtypes.Select(s => s.ToString()));

        return primary switch
        {
            PrimaryType.Creature => $"new Creature(CardName, PrintedManaCost, {ParseStat(entity.Power)}, {ParseStat(entity.Toughness)}, {supertypesArg}, {subtypesArg})",
            PrimaryType.Land => $"new Land(CardName, {supertypesArg}, {subtypesArg})",
            PrimaryType.Instant => $"new Instant(CardName, PrintedManaCost)",
            PrimaryType.Sorcery => $"new Sorcery(CardName, PrintedManaCost)",
            PrimaryType.Enchantment => $"new Enchantment(CardName, PrintedManaCost, {supertypesArg}, {subtypesArg})",
            PrimaryType.Artifact => $"new Artifact(CardName, PrintedManaCost, {supertypesArg}, {subtypesArg})",
            PrimaryType.Planeswalker => $"new Planeswalker(CardName, PrintedManaCost, startingLoyalty: {entity.Loyalty ?? 0}, {supertypesArg}, {subtypesArg})",
            _ => $"new Card(CardName, PrintedManaCost)",
        };
    }

    private static string FormatEnumList(string enumName, IEnumerable<string> values)
    {
        var list = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (list.Count == 0) return "null";
        return $"new[] {{ {string.Join(", ", list.Select(v => $"{enumName}.{v}"))} }}";
    }

    private static int ParseStat(string? raw) =>
        int.TryParse(raw, out var n) ? n : 0;

    /// <summary>
    /// Best-effort comp-rules pointer based on primary card type. The author
    /// will swap this for a real citation once the body is wired — the
    /// scaffold just needs a non-empty hint to keep the docstring contract
    /// consistent with the existing factories.
    /// </summary>
    public static string RuleHintFor(PrimaryType primary, string oracleText)
    {
        return primary switch
        {
            PrimaryType.Creature => "CR 302 (creatures) — replace with specific ability rule once wired.",
            PrimaryType.Land => "CR 305 (lands) — replace with specific ability rule once wired.",
            PrimaryType.Instant => "CR 304 (instants) — replace with specific spell-effect rule once wired.",
            PrimaryType.Sorcery => "CR 307 (sorceries) — replace with specific spell-effect rule once wired.",
            PrimaryType.Enchantment => "CR 303 (enchantments) — replace with specific ability rule once wired.",
            PrimaryType.Artifact => "CR 301 (artifacts) — replace with specific ability rule once wired.",
            PrimaryType.Planeswalker => "CR 306 + 606 (planeswalkers / loyalty abilities) — replace with specific rule once wired.",
            _ => "CR — fill in once primary type is determined.",
        };
    }

    private static string Inline(string s)
    {
        return s.Replace("\r\n", " ").Replace('\n', ' ').Trim();
    }

    private static string EscapeCSharpStringLiteral(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
