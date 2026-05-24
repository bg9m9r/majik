using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Daze (Nemesis, {1}{U}).
///
/// Instant. Oracle text:
///   "You may return an Island you control to its owner's hand rather than
///    pay this spell's mana cost.
///    Counter target spell unless its controller pays {1}."
///
/// Implemented in v1:
///   * Instant card shape ({1}{U}, Blue).
///   * "Counter target spell unless its controller pays {1}" via
///     <see cref="BuildDefinition"/> — mirrors
///     <c>CounterSpellFactory.CounterTargetSpellUnlessPay</c> with N=1. At
///     resolution time the engine auto-consults the target's controller's
///     mana pool; if {1} is available it's spent and the counter no-ops.
///     Daze prints no timing gate on its pitch cost (unlike Force of Will);
///     the "unless pay" rider on the resolved effect is the cost-side.
///   * Bounce-land pitch alternative cost via
///     <see cref="Majik.Core.Costs.BounceLandPitchAlternativeCost"/>:
///     return an Island the caster controls to its owner's hand. No mana
///     paid (CR 118.9).
///
/// Pitch alt-cost surfaced through
/// <see cref="Majik.Core.Costs.BounceLandPitchAlternativeCost"/>; callers
/// construct one with the Island they want to bounce. A bot-side probe
/// (mirror of <c>PitchAltCostProbe</c>) is deferred — Daze's pitch always
/// pays so the probe shape is just "for each Island controlled, yield one
/// candidate" and lives outside this factory's surface in v1.
/// </summary>
[CardName("Daze")]
public static class DazeFactory
{
    public const string CardName = "Daze";

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, "{1}{U}");
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>Build the "counter target spell unless its controller pays {1}"
    /// SpellDefinition. Mirrors
    /// <c>CounterSpellFactory.CounterTargetSpellUnlessPay(..., unlessPayN: 1)</c>
    /// — inlined here so the named-card factory is fully self-contained.</summary>
    public static SpellDefinition BuildDefinition(Func<object, object> targetResolver, Majik.Core.Stack.Stack? stack) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Daze — counter target spell unless its controller pays {1}", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;
                        // CR 118.4 — if the target's controller has {1} in pool
                        // they may pay; v1 short-circuits to "auto-pay if able".
                        if (spell.Controller is not null
                            && spell.Controller.PayMana(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(1)))
                        {
                            return;
                        }
                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
}
