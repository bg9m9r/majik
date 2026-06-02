using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ajani's Pridemate (Magic 2011, {1}{W}).
///
/// Creature — Cat Soldier 2/2. Oracle text (current Scryfall, post-errata):
///   "Whenever you gain life, put a +1/+1 counter on Ajani's Pridemate."
///
/// Note: an earlier printing read "you may put"; the current Scryfall oracle
/// drops the "may" (no controller choice). This factory implements the
/// non-optional, current oracle.
///
/// ## Pure-JSON factory (declarative trigger + effect)
/// Ajani's Pridemate is now fully declarative — the lifegain trigger is
/// expressed by the <c>whenever_you_gain_life</c>
/// (<see cref="WheneverYouGainLifeTriggerDef"/>) trigger variant and the
/// payoff by the existing <c>put_counter</c> self effect, both materialised
/// by <see cref="CardDefRuntime"/> from <c>ajanis-pridemate.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build(CardDefinition, Player, ReplacementBus?)"/>.
/// This replaces the prior hand-rolled
/// <see cref="Triggers.OnLifeGainedByPlayer"/> wiring — proving the
/// declarative lifegain-trigger shape carries the same behaviour.
///
/// - <b>Lifegain trigger (CR 603.6a / CR 119.3 / CR 122.1)</b>: fires on a
///   <see cref="LifeChangedEvent"/> for Pridemate's controller with a
///   strictly-positive delta (life *gain*, not loss). On resolution one
///   <see cref="Majik.Core.Counters.CounterType.PlusOnePlusOne"/> counter is
///   placed via <see cref="CountersService.Add"/> (CR 614 replacements
///   observe the intent). One counter per life-gain event regardless of the
///   gained amount.
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Ajani's Pridemate")]
public static class AjaniPridemateFactory
{
    public const string CardName = "Ajani's Pridemate";

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "ajanis-pridemate";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Ajani's Pridemate with no live <see cref="TriggerManager"/>
    /// wiring. The lifegain trigger is materialised onto the card shape from
    /// the JSON definition for structural / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Ajani's Pridemate with an optional <see cref="TriggerManager"/>
    /// and <see cref="ReplacementBus"/>. When <paramref name="triggers"/> is
    /// supplied, the declarative lifegain trigger is registered so a qualifying
    /// <see cref="LifeChangedEvent"/> auto-queues the ability. When
    /// <paramref name="replacements"/> is supplied, the +1/+1 counter
    /// placement is routed through <see cref="CountersService.Add"/> so
    /// Hardened Scales / Doubling Season replacements (CR 614) can rewrite the
    /// count.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner, replacements);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        if (triggers != null)
        {
            foreach (var trigger in card.Abilities.OfType<TriggeredAbility>())
            {
                triggers.RegisterTriggeredAbility(trigger);
            }
        }

        return card;
    }
}
