using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;

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
        IReadOnlyList<string> grantedKeywords) => new(
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

            var spec = new TokenFactory.TokenSpec(
                Name: subtypeRaw,
                Power: power,
                Toughness: toughness,
                Subtypes: subtypes,
                Keywords: grantedKeywords);

            for (var i = 0; i < count; i++)
                TokenFactory.CreateOnBattlefield(spec, caster);
        }) });

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
