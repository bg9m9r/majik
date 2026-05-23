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
/// Inquisition of Kozilek family: "Target player reveals their hand. You choose
/// a nonland card from it with mana value N or less. That player discards that
/// card." Distinct from <see cref="ThoughtseizePatternTemplate"/> (no life-loss
/// clause) and from <see cref="RevealHandThenDiscardTemplate"/> (which ignores
/// the mana-value cap — Inquisition's defining differentiator vs. Thoughtseize).
///
/// Priority is set above <see cref="RevealHandThenDiscardTemplate"/>'s 95 so this
/// shape claims the bind before the generic Duress template (which would
/// silently drop the mv filter and pick the first non-land card regardless).
///
/// v1 stub: deterministic pick — the first non-land card in the target's hand
/// whose mana value is &lt;= the captured cap. If no card in hand satisfies the
/// cap, no discard occurs (mirrors CR 701.16 "if no card can be chosen").
/// </summary>
public sealed class InquisitionOfKozilekPatternTemplate : ISpellTemplate
{
    // Examples:
    //   "Target player reveals their hand. You choose a nonland card from it with mana value 3 or less. That player discards that card."
    private static readonly Regex Pattern = new(
        @"target\s+(?:player|opponent)\s+reveals\s+their\s+hand\.\s*you\s+choose\s+a\s+nonland\s+card\s+from\s+it\s+with\s+mana\s+value\s+(?<cap>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+or\s+less\.\s*that\s+player\s+discards\s+that\s+card\.",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "InquisitionOfKozilekPattern";
    public BotIntent Intent => BotIntent.Discard;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["cap"] = m.Groups["cap"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        InquisitionSpell(ctx.Resolver, SpellTemplateHelpers.WordToInt(@params["cap"]), ctx.EventBus);

    /// <summary>
    /// v1 stub: deterministic pick — first non-land card in target's hand with
    /// mana value &lt;= <paramref name="cap"/>. No life loss (the Thoughtseize
    /// differentiator).
    /// </summary>
    private static SpellDefinition InquisitionSpell(
        Func<object, object> resolver,
        int cap,
        Majik.Core.Events.IEventBus? eventBus) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("inquisition-of-kozilek", () =>
            {
                if (target is not Player tp) return;
                // CR 701.16 — "Target player reveals their hand": emit one
                // CardRevealedEvent per card so clients can flash them briefly.
                RevealHelper.RevealHand(eventBus, tp, "InquisitionOfKozilekPattern");
                // v1: deterministic pick — first non-land card in target's hand
                // whose mana value is <= cap. ManaCostValue lives on Card, so
                // narrow from ICard before sampling the mv.
                var pick = tp.Zones.Hand.GetCards()
                    .OfType<Card>()
                    .FirstOrDefault(c =>
                        !c.HasType(Majik.Core.Cards.Types.CardType.Land)
                        && c.ManaCostValue.TotalValue <= cap);
                if (pick is null) return;
                tp.Zones.Hand.RemoveCard(pick);
                tp.Zones.Graveyard.AddCard(pick);
                pick.SetZone(ZoneType.Graveyard);
            }) };
        });
}
