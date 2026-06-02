using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Soul Warden (Exodus, {W}).
///
/// Creature — Human Cleric 1/1. Oracle text (current Scryfall):
///   "Whenever another creature enters, you gain 1 life."
///
/// ## Pure-JSON factory (declarative trigger + effect)
/// Soul Warden is now fully declarative — the ETB-other-creature trigger is
/// expressed by the <c>whenever_another_creature_enters</c>
/// (<see cref="WheneverAnotherCreatureEntersTriggerDef"/>) trigger variant
/// (default any-controller scope — the printed Soul Sisters care about ANY
/// creature entering, not just yours) and the payoff by the existing
/// <c>gain_life_self</c> effect, both materialised by <see cref="CardDefRuntime"/>
/// from <c>soul-warden.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build(CardDefinition, Player, ReplacementBus?)"/>.
/// This replaces the prior hand-rolled <see cref="EventTriggerCondition{TEvent}"/>
/// over <see cref="Majik.Core.Events.CardMovedEvent"/> wiring — proving the
/// declarative "another creature enters" shape carries the same behaviour.
///
/// - <b>ETB-other-creature trigger (CR 603.6e / CR 119.3)</b>: any creature
///   other than Soul Warden entering the battlefield (under any controller)
///   triggers; on resolution Soul Warden's controller gains 1 life. The
///   trigger is active only while Soul Warden is on the battlefield (the engine
///   default).
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Soul Warden")]
public static class SoulWardenFactory
{
    public const string CardName = "Soul Warden";
    public const string PrintedManaCost = "{W}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int LifeGainAmount = 1;

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "soul-warden";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Soul Warden with no live <see cref="TriggerManager"/> wiring.
    /// The ETB-other-creature trigger is materialised onto the card shape from
    /// the JSON definition for structural / dispatch tests; bus-driven firing
    /// requires the (owner, triggers) overload.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Soul Warden, registering the lifegain trigger with
    /// <paramref name="triggers"/> when supplied so a qualifying
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> automatically queues the
    /// ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner);
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
