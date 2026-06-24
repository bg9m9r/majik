using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lifecreed Duo (Bloomburrow, {1}{W}).
///
/// Creature — Bat Bird 1/2. Oracle text (verified against Scryfall):
///   "Flying
///    Whenever another creature you control enters, you gain 1 life."
///
/// ## Pure-JSON factory (declarative keyword + trigger + effect)
/// Materialised entirely from <c>lifecreed-duo.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>:
/// - <b>Flying</b> from the declarative <c>keywords</c> array (CR 702.9).
/// - <b>Triggered ability (CR 603.6e / CR 109.5)</b>: a
///   <c>whenever_another_creature_enters</c> trigger scoped to the controller's
///   OTHER creatures (<c>youControlOnly</c> true — "another creature you
///   control"; <c>includeSelf</c> default false excludes Lifecreed Duo itself).
///   On resolution the <c>gain_life_self</c> verb adds 1 life to the
///   controller, routed through the shared <c>Fx.GainLife</c> primitive.
///   Untargeted (CR 608.2) — no target announced.
///
/// Same declarative shape as <see cref="SoulsAttendantFactory"/> /
/// <see cref="SoulWardenFactory"/>, but gated to creatures the controller
/// controls (<c>youControlOnly</c>) and carrying Flying.
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Lifecreed Duo")]
public static class LifecreedDuoFactory
{
    public const string CardName = "Lifecreed Duo";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 1;
    public const int Toughness = 2;
    public const int LifeGainAmount = 1;

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "lifecreed-duo";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Lifecreed Duo with no live <see cref="TriggerManager"/> wiring.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Lifecreed Duo, registering the lifegain trigger with
    /// <paramref name="triggers"/> when supplied.
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
