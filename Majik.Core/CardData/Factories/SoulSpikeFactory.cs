using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Soul Spike (Coldsnap, {3}{B}{B}).
///
/// Instant. Oracle text:
///   "You may exile two black cards from your hand rather than pay this
///    spell's mana cost.
///    Soul Spike deals 4 damage to any target and you gain 4 life."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {3}{B}{B}.
/// - <b>Pitch alt-cost (CR 117.11 + CR 701.21)</b>: callers supply
///   <see cref="Majik.Core.Costs.ExileTwoColoredCardsAlternativeCost"/>
///   with <see cref="ManaColor.Black"/> + two distinct black cards from
///   hand to <see cref="SpellCastFlow.CastAsync"/>; the alt-cost exiles
///   both pitched cards on resolution. (See dedicated class for the new
///   two-card pitch primitive — extends the
///   <see cref="Majik.Core.Costs.ExileColoredCardAlternativeCost"/>
///   shape to <c>n=2</c>.)
/// - Single 1..1 "any target" target request — same shape as Lightning
///   Helix / Shock. On resolution: 4 damage to the chosen target (CR 119,
///   routed through <see cref="Fx.DealDamageAny"/> so Planeswalker
///   targets get loyalty removal per CR 306.7), then the spell controller
///   gains 4 life (CR 119.3, <see cref="Fx.GainLife"/>). Both clauses
///   apply in printed order as part of one resolution — the lifegain is
///   gated by the "any target" rule (CR 608.2b: illegal target →
///   whole-spell no-op), but is otherwise unconditional and is NOT
///   lifelink.
/// </summary>
[CardName("Soul Spike")]
public static class SoulSpikeFactory
{
    public const string CardName = "Soul Spike";
    public const string PrintedManaCost = "{3}{B}{B}";

    public const int DamageAmount = 4;
    public const int LifeGainAmount = 4;

    /// <summary>CardDef DSL — card shape only. Damage + lifegain body is
    /// supplied at cast time via <see cref="BuildSpellDefinition"/>
    /// because <see cref="SpellDefinition"/> needs the caller's target
    /// resolver from the <see cref="GameContext"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Soul Spike is
    /// cast. Single 1..1 "any target" request; on resolution deals 4
    /// damage to the target and the controller gains 4 life.
    /// </summary>
    /// <param name="controller">Spell controller — gains 4 life on
    /// resolution.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline($"{CardName}: 4 damage + 4 life", () =>
                    {
                        // CR 119 — damage step. Routes Player / Creature /
                        // Planeswalker via Fx.DealDamageAny.
                        Fx.DealDamageAny(target, DamageAmount);

                        // CR 119.3 — controller gains 4 life unconditionally
                        // as part of the same resolution. NOT lifelink.
                        Fx.GainLife(controller, LifeGainAmount);
                    }),
                };
            });
    }
}
