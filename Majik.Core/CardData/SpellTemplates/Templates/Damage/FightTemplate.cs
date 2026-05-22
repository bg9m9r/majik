using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

/// <summary>
/// CR 701.13 — Fight. "Target creature [you control] fights another target
/// creature [you don't control]." Each fights resolves by having both
/// creatures deal damage equal to their power to the other simultaneously
/// (CR 701.13a).
///
/// Catches single-clause fight spells: Pit Fight, Prey Upon, Pounce, Blood
/// Feud, Clash of Titans, Mutant's Prey, Dissension in the Ranks, etc.
/// Multi-clause fight riders ("It fights target creature ...") that depend
/// on anaphora to the previous clause's target stay unmatched at v1.
///
/// Control / tribal / counter modifiers on the targets are accepted by the
/// regex but the runtime stub doesn't enforce them — target legality is
/// the spell-cast flow's concern, not the resolved effect.
/// </summary>
public sealed class FightTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+(?:[\w'-]+\s+)*?creature\b(?:\s+(?:you\s+control|an\s+opponent\s+controls|you\s+don'?t\s+control|with\s+[\w\s+/-]+))?\s+fights?\s+(?:another\s+)?target\s+(?:[\w'-]+\s+)*?creature\b",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "Fight";
    public BotIntent Intent => BotIntent.Burn;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature (yours)", 1, 1, Array.Empty<object>()),
                new TargetRequest("target creature (other)", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var a = resolver(p.Targets[0][0]);
                var b = resolver(p.Targets[1][0]);
                return new IEffect[] { new Effect("fight", () =>
                {
                    if (a is not Creature ca || b is not Creature cb) return;
                    // CR 701.13a — each deals damage equal to its CURRENT
                    // power to the other simultaneously. Read both powers
                    // before any damage applies so a -X/-X to one doesn't
                    // change the other's incoming damage.
                    var aPower = ca.Power;
                    var bPower = cb.Power;
                    if (aPower > 0) cb.TakeDamage(aPower);
                    if (bPower > 0) ca.TakeDamage(bPower);
                }) };
            });
    }
}
