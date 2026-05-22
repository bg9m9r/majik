using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;

namespace Majik.Core.CardData.SpellTemplates.Templates.Tokens;

/// <summary>
/// CR 701.31 — Populate. "Create a token that's a copy of a creature
/// token you control." The agent-prompt choice ("a creature token") is
/// auto-resolved at v1 as the first creature token the caster controls.
/// Returns no-op when the caster controls no creature tokens.
///
/// Triggers on bare "Populate." sentences (Wake the Reflections,
/// Druid's Deliverance's second clause); multi-populate ("Populate X
/// times" — Full Flowering) and conditional-populate ("Whenever a
/// creature dealt damage this way dies this turn, populate" — Ghired's
/// Belligerence) need their own bindings.
/// </summary>
public sealed class PopulateTemplate : ISpellTemplate
{
    // Matches a "Populate." sentence, allowing surrounding reminder text
    // and other clauses. Wake the Reflections ("Populate. (Create a token
    // that's a copy of a creature token you control.)") matches via the
    // sentence anchor; multi-clause spells with populate as one of several
    // clauses also match since the composer fires Rehydrate per clause.
    private static readonly Regex Pattern = new(
        @"(?:^|\.\s|\n)populate(?:\s*\.|\s*$)",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "Populate";
    public BotIntent Intent => BotIntent.Token;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var caster = ctx.Caster;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[] { new Effect("populate", () =>
            {
                var pick = caster.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .FirstOrDefault(c => c.IsToken);
                if (pick == null) return;
                var keywords = pick.Abilities.OfType<KeywordAbility>()
                    .Select(k => k.Keyword)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var spec = new TokenFactory.TokenSpec(
                    Name: pick.Name,
                    Power: pick.BasePower,
                    Toughness: pick.BaseToughness,
                    Subtypes: pick.Subtypes.ToArray(),
                    Keywords: keywords);
                TokenFactory.CreateOnBattlefield(spec, caster, zones: null);
            }) });
    }
}
