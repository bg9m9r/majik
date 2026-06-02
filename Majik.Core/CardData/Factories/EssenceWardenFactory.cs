using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Essence Warden (Planar Chaos, {G}).
///
/// Creature — Elf Shaman 1/1. Oracle text (current Scryfall):
///   "Whenever another creature enters, you gain 1 life."
///
/// Functional reprint of Soul Warden (green-costed). Now fully declarative —
/// materialised from <c>essence-warden.json</c> via the same
/// <c>whenever_another_creature_enters</c> trigger + <c>gain_life_self</c>
/// effect as <see cref="SoulWardenFactory"/>.
///
/// - <b>ETB-other-creature trigger (CR 603.6e / CR 119.3)</b>: any creature
///   other than Essence Warden entering the battlefield (under any controller)
///   triggers; on resolution Essence Warden's controller gains 1 life. The
///   trigger is active only while Essence Warden is on the battlefield (the
///   engine default).
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Essence Warden")]
public static class EssenceWardenFactory
{
    public const string CardName = "Essence Warden";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int LifeGainAmount = 1;

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "essence-warden";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Essence Warden with no live <see cref="TriggerManager"/> wiring.
    /// The ETB-other-creature trigger is materialised onto the card shape from
    /// the JSON definition for structural / dispatch tests; bus-driven firing
    /// requires the (owner, triggers) overload.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Essence Warden, registering the lifegain trigger with
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
