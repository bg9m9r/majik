using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Soul's Attendant (Magic 2011, {W}).
///
/// Creature — Human Cleric 1/1. Functional reprint of Soul Warden with the
/// same Scryfall oracle:
///   "Whenever another creature enters, you gain 1 life."
///
/// ## Pure-JSON factory (declarative trigger + effect)
/// Materialised from <c>souls-attendant.json</c> via the same declarative
/// <c>whenever_another_creature_enters</c> trigger + <c>gain_life_self</c>
/// effect as <see cref="SoulWardenFactory"/> — kept as a separate factory
/// (rather than aliased) so the <c>[CardName]</c> dispatcher table holds the
/// printed-name identity independently and the Modern Soul Sisters archetype
/// can field both.
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Soul's Attendant")]
public static class SoulsAttendantFactory
{
    public const string CardName = "Soul's Attendant";
    public const string PrintedManaCost = "{W}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int LifeGainAmount = 1;

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "souls-attendant";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Soul's Attendant with no live <see cref="TriggerManager"/>
    /// wiring. Behaviour mirrors <see cref="SoulWardenFactory.Create(Player)"/>.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Soul's Attendant, registering the lifegain trigger with
    /// <paramref name="triggers"/> when supplied. Mirrors
    /// <see cref="SoulWardenFactory.Create(Player, TriggerManager?)"/>.
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
