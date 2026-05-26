using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Misdirection (Mercadian Masques, {2}{U}{U}).
///
/// Instant. Oracle text:
///   "You may exile a blue card from your hand rather than pay this
///    spell's mana cost.
///    Change the target of target spell with a single target."
///
/// ## Implemented (v1)
/// - Instant card shape ({2}{U}{U}, Blue) — built via the fluent
///   <see cref="CardDef"/> DSL.
/// - Pitch alternative cost via
///   <see cref="Majik.Core.Costs.ExileColoredCardAlternativeCost"/>
///   (<c>RequiredColor = Blue</c>) — the no-timing-gate / no-life-rider
///   pitch primitive (same one Snapback / Foil / Pyrokinesis use).
///   Misdirection's printed pitch carries NO "if it's not your turn"
///   restriction (unlike the Force-of-Will cycle).
/// - Resolve effect (<see cref="BuildDefinition"/>): "change the target
///   of target spell with a single target" — delegates to the shared
///   <see cref="SpellRedirector.RedirectTopSpellSingleTarget"/> primitive
///   (CR 114.6 — changing targets). Mirrors
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Bespoke.RedirectTemplate"/>
///   and the Deflection / Imp's Mischief / Shunt family.
///
/// ## Deferred (v1 gaps)
/// - <b>Effect-closure retargeting</b>: see <see cref="SpellRedirector"/>
///   class-level remarks. v1 mutates <c>ChosenTargets</c> only; the
///   pre-built effect closures still resolve against the original target.
///   The legality recheck (CR 608.2b) on the resolver does observe the
///   redirect, but the actual damage / counter / destroy ultimately lands
///   on the original target. Lossy semantic, documented — same posture
///   every other Redirect-family card inherits today.
/// - <b>"target spell with a single target" choice</b>: in MTG the caster
///   first picks WHICH spell to redirect, then picks the new target. v1
///   collapses to "top single-target spell" (same as RedirectTemplate).
/// - <b>Bot probe</b>: not surfaced through
///   <see cref="PitchAltCostProbe.DefaultLookup"/> — that probe is keyed
///   by <see cref="Majik.Core.Costs.PitchAlternativeCost"/>'s not-your-turn
///   shape, which Misdirection's pitch lacks. Same posture as Snapback /
///   Pyrokinesis / Foil / Soul Spike.
/// </summary>
[CardName("Misdirection")]
public static class MisdirectionFactory
{
    public const string CardName = "Misdirection";
    public const string PrintedManaCost = "{2}{U}{U}";

    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "change the target of target spell with a single target"
    /// SpellDefinition. The agent picks a new target object; on resolution
    /// the redirector rewrites the top single-target spell's
    /// <see cref="Majik.Core.Spells.Spell.ChosenTargets"/> to that pick.
    /// </summary>
    /// <param name="targetResolver">Resolves the chosen new-target object.</param>
    /// <param name="stack">Live stack — required to locate the spell to
    /// redirect. Null in pure-shape tests; the effect becomes a no-op.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            // v1 surfaces a single TargetRequest for the NEW target — same
            // shape as RedirectTemplate. The "target spell with a single
            // target" pick is collapsed into "top single-target spell" at
            // resolve time (see SpellRedirector remarks).
            TargetRequests: new[]
            {
                new TargetRequest("new target", 1, 1, Array.Empty<object>(),
                    BotIntent.Protection | BotIntent.Removal),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var newTarget = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Misdirection — change the target of target spell with a single target", () =>
                    {
                        if (stack == null) return;
                        SpellRedirector.RedirectTopSpellSingleTarget(stack, newTarget);
                    }),
                };
            });
}
