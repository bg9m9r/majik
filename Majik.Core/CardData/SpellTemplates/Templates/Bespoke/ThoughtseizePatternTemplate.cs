using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

public sealed class ThoughtseizePatternTemplate : ISpellTemplate
{
    // "Target player reveals their hand. You choose a nonland card from it.
    //  That player discards that card. You lose N life." (Thoughtseize template)
    private static readonly Regex Pattern = new(
        @"target\s+player\s+reveals\s+their\s+hand\.\s*you\s+choose\s+a\s+nonland\s+card\s+from\s+it\.\s*that\s+player\s+discards\s+that\s+card\.\s*you\s+lose\s+(?<life>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+life",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "ThoughtseizePattern";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["life"] = m.Groups["life"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        ThoughtseizeSpell(ctx.Caster, ctx.Resolver, SpellTemplateHelpers.WordToInt(@params["life"]));

    /// <summary>
    /// Thoughtseize template (v1 — deterministic pick: first non-land card in target's hand).
    /// Real Thoughtseize lets the caster choose; v1 simplification picks deterministically.
    /// Caster loses <paramref name="lifeLoss"/> life after the discard.
    /// </summary>
    private static SpellDefinition ThoughtseizeSpell(Player caster, Func<object, object> resolver, int lifeLoss) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("thoughtseize", () =>
            {
                if (target is not Player tp) return;
                // v1: deterministic pick — first non-land card in target's hand.
                var pick = tp.Zones.Hand.GetCards()
                    .FirstOrDefault(c => !c.HasType(Majik.Core.Cards.Types.CardType.Land));
                if (pick != null)
                {
                    tp.Zones.Hand.RemoveCard(pick);
                    tp.Zones.Graveyard.AddCard(pick);
                    pick.SetZone(ZoneType.Graveyard);
                }
                caster.LoseLife(lifeLoss);
            }) };
        });
}
