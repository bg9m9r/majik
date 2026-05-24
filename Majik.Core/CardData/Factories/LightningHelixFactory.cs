using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Primitives;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lightning Helix (Ravnica: City of Guilds / Modern
/// Horizons, {R}{W}).
///
/// Instant. Oracle text:
///   "Lightning Helix deals 3 damage to any target and you gain 3 life."
///
/// ## Implementation
///
/// Single 1..1 "any target" target request — same shape as Lightning Bolt
/// / Unholy Heat. On resolution the spell deals 3 damage to the chosen
/// target (CR 119) and the spell controller gains 3 life (CR 119.3). Both
/// effects happen as part of one resolution; per CR 608.2c-style ordering
/// they apply in the printed order (damage first, then lifegain) — the
/// lifegain is unconditional and is NOT lifelink, so it fires regardless of
/// whether the damage step did anything (CR 608.2b illegal-target check
/// short-circuits damage only; the "and you gain 3 life" clause is part of
/// the same resolution and is gated by the targeting rule — if the lone
/// target is illegal at resolution the whole spell does nothing).
///
/// <see cref="OracleSpellBinder.DealDamage"/> handles Player + Creature
/// only; Planeswalker damage is dealt via the shared
/// <see cref="SearingBlazeFactory.DealDamageWithPlaneswalker"/> helper
/// (loyalty removal — CR 119.3 / 306.7).
///
/// Card-shape only here; the resolve-time spell definition (target +
/// damage + lifegain) is built on-demand via
/// <see cref="BuildSpellDefinition(Player, Func{object, object})"/>
/// because <see cref="SpellDefinition"/> needs a target resolver supplied
/// by the caller's <see cref="GameContext"/>.
/// </summary>
[CardName("Lightning Helix")]
public static class LightningHelixFactory
{
    public const string CardName = "Lightning Helix";
    public const string PrintedManaCost = "{R}{W}";

    public const int DamageAmount = 3;
    public const int LifeGainAmount = 3;

    /// <summary>
    /// Build a Lightning Helix instant owned by <paramref name="owner"/>.
    /// Card shape only — see <see cref="BuildSpellDefinition"/> for the
    /// resolve-time damage + lifegain effect.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Lightning Helix is
    /// cast. Single 1..1 "any target" request; on resolution deals 3 damage
    /// to the target and the controller gains 3 life.
    /// </summary>
    /// <param name="controller">Spell controller — gains 3 life on
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
                    Fx.Inline("Lightning Helix: 3 damage + 3 life", () =>
                    {
                        // CR 119 — damage step. Routes Player / Creature /
                        // Planeswalker via the shared Effects facade.
                        Fx.DealDamageAny(target, DamageAmount);

                        // CR 119.3 — controller gains 3 life unconditionally
                        // as part of the same resolution.
                        Fx.GainLife(controller, LifeGainAmount);
                    }),
                };
            });
    }
}
