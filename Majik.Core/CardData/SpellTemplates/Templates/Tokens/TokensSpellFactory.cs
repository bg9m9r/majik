using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.SpellTemplates.Templates.Tokens;

internal static class TokensSpellFactory
{
    internal static SpellDefinition CreateTreasureTokensSpell(Player caster, int count) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"create {count} Treasure", () =>
        {
            for (var i = 0; i < count; i++)
                TokenFactory.CreateTreasure(caster);
        }) });

    internal static SpellDefinition CreateFoodTokensSpell(Player caster, int count) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"create {count} Food", () =>
        {
            for (var i = 0; i < count; i++)
                TokenFactory.CreateFood(caster);
        }) });

    internal static SpellDefinition CreateClueTokensSpell(Player caster, int count) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"create {count} Clue", () =>
        {
            for (var i = 0; i < count; i++)
                TokenFactory.CreateClue(caster);
        }) });

    // CR 701.30 — "To investigate" means to create a Clue token.
    internal static SpellDefinition InvestigateNTimesSpell(Player caster, int count) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"investigate {count}", () =>
        {
            for (var i = 0; i < count; i++)
                TokenFactory.CreateClue(caster);
        }) });

    internal static SpellDefinition CreateTokensSpell(
        Player caster, int count, int power, int toughness, string subtypeRaw,
        IReadOnlyList<string> grantedKeywords,
        string? colourRaw = null) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"create {count} {power}/{toughness}", () =>
        {
            // Subtype enum lookup is best-effort; tokens with unrecognised
            // subtypes still spawn with no subtype attached.
            Majik.Core.Cards.Types.CardSubtype? subtype = null;
            if (Enum.TryParse<Majik.Core.Cards.Types.CardSubtype>(
                char.ToUpperInvariant(subtypeRaw[0]) + subtypeRaw[1..].ToLowerInvariant(),
                out var st))
            {
                subtype = st;
            }

            var subtypes = subtype.HasValue
                ? new[] { subtype.Value }
                : Array.Empty<Majik.Core.Cards.Types.CardSubtype>();

            // CR 105 / CR 111.4 — token colour identity is whatever the
            // printed clause says. The template's regex captures the
            // colour phrase (e.g. "white", "red and green", "colorless");
            // ParseTokenColours folds that into a ManaColor list. Empty
            // list = explicit colourless (matches printed text + "no
            // colour mentioned" fallback shape).
            var colours = ParseTokenColours(colourRaw);

            var spec = new TokenFactory.TokenSpec(
                Name: subtypeRaw,
                Power: power,
                Toughness: toughness,
                Subtypes: subtypes,
                Keywords: grantedKeywords,
                Colors: colours);

            for (var i = 0; i < count; i++)
                TokenFactory.CreateOnBattlefield(spec, caster);
        }) });

    /// <summary>
    /// Parse the template-captured colour phrase ("white", "red and green",
    /// "blue or red", "colorless") into a deduplicated
    /// <see cref="ManaColor"/> list. Empty / "colorless" / unparseable
    /// input collapses to an empty list (CR 105.2c — colourless).
    /// </summary>
    internal static IReadOnlyList<ManaColor> ParseTokenColours(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<ManaColor>();
        var set = new HashSet<ManaColor>();
        // The template's regex matches each colour word as a separate
        // alternative; split on whitespace + " and " / " or " connectives,
        // then map each token to ManaColor.
        var tokens = raw.Replace(" and ", " ").Replace(" or ", " ")
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var t in tokens)
        {
            switch (t.Trim().ToLowerInvariant())
            {
                case "white":     set.Add(ManaColor.White); break;
                case "blue":      set.Add(ManaColor.Blue); break;
                case "black":     set.Add(ManaColor.Black); break;
                case "red":       set.Add(ManaColor.Red); break;
                case "green":     set.Add(ManaColor.Green); break;
                case "colorless": /* CR 105.2c — explicit colourless */ break;
                default:          break; // unknown word — ignore
            }
        }
        return set.ToList();
    }

    internal static IReadOnlyList<string> ParseKeywordList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        // Split on commas / "and"; trim; canonicalise via NormaliseKeyword.
        return raw.Replace(" and ", ",").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => SpellTemplates.Templates.Counters.CountersSpellFactory.NormaliseKeyword(s.Trim()))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }
}
