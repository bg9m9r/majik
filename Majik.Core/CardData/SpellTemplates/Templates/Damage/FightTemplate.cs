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
                    // CR 701.12 — both creatures deal damage equal to their
                    // (pre-fight) power to the other simultaneously, routed
                    // through the shared Fx.Fight primitive so deathtouch
                    // (CR 702.2b) and lifelink (CR 702.15a) apply. Fx.Fight
                    // no-ops if either target is not a creature (CR 701.12c —
                    // a fight needs both creatures present).
                    Majik.Core.Primitives.Fx.Fight(a as Creature, b as Creature);
                }) };
            });
    }
}
