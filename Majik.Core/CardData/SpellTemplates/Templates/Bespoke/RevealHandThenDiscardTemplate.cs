using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Duress / Coercion / Pilfer / Despise family: "Target opponent reveals their
/// hand. You choose [filter] card from it. That player discards that card."
///
/// Distinct from <see cref="ThoughtseizePatternTemplate"/> which requires the
/// trailing "You lose N life" clause and the "Target player" wording. This one
/// matches the broader Duress shape: "Target opponent", any card-type filter,
/// no mandatory life loss.
///
/// v1 stub: card-type filter is captured but ignored at resolution — the picker
/// deterministically chooses the first non-land card in the opponent's hand,
/// matching the Thoughtseize stub. Trailing rider clauses (Humiliate's "+1/+1
/// counter", Diplomacy's "lose 2 life if you control a Warrior", etc.) are
/// silently dropped. The bound spell resolves the core "see hand → discard"
/// effect without the rider, which beats "doesn't bind at all".
/// </summary>
public sealed class RevealHandThenDiscardTemplate : ISpellTemplate
{
    // Examples:
    //   "Target opponent reveals their hand. You choose a nonland card from it. That player discards that card."
    //   "Target opponent reveals their hand. You choose a noncreature, nonland card from it. That player discards that card."
    //   "Target opponent reveals their hand. You choose a creature or planeswalker card from it. That player discards that card."
    //   "Target opponent reveals their hand. You choose a card from it. That player discards that card."
    private static readonly Regex Pattern = new(
        @"^target\s+(?:opponent|player)\s+reveals\s+their\s+hand\.\s*you\s+choose\s+(?:a|an)\s+(?<filter>[a-z][a-z0-9\s,\-]{0,80}?\s+)?card\s+from\s+it(?:\s+[^.]{0,80}?)?\.\s*that\s+player\s+discards\s+that\s+card\.",
        RegexOptions.IgnoreCase);

    public int Priority => 95;
    public string Name => "RevealHandThenDiscard";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        var filter = m.Groups["filter"].Success ? m.Groups["filter"].Value.Trim() : "";
        return new Dictionary<string, string> { ["filter"] = filter };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        DuressSpell(ctx.Resolver);

    /// <summary>
    /// v1 stub: deterministic pick — first non-land card in opponent's hand
    /// goes to graveyard. Card-type filter is ignored (lossy semantic).
    /// </summary>
    private static SpellDefinition DuressSpell(Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target opponent", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("reveal-hand-then-discard", () =>
            {
                if (target is not Player tp) return;
                var pick = tp.Zones.Hand.GetCards()
                    .FirstOrDefault(c => !c.HasType(Majik.Core.Cards.Types.CardType.Land));
                if (pick is null) return;
                tp.Zones.Hand.RemoveCard(pick);
                tp.Zones.Graveyard.AddCard(pick);
                pick.SetZone(ZoneType.Graveyard);
            }) };
        });
}
