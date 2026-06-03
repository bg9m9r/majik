using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mardu Woe-Reaper (Fate Reforged, {W}).
///
/// Creature — Human Warrior 2/1. Oracle text (current Scryfall):
///   "Whenever this creature or another Warrior you control enters, you may
///   exile target creature card from a graveyard. If you do, you gain 1 life."
///
/// ## Pure-JSON factory (declarative trigger + effect)
/// Mardu Woe-Reaper is fully declarative — built from
/// <c>mardu-woe-reaper.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build(CardDefinition, Player)"/>:
///
/// - <b>Subtype-gated ETB-of-another trigger (CR 603.6e)</b>: the
///   <c>whenever_another_creature_enters</c>
///   (<see cref="WheneverAnotherCreatureEntersTriggerDef"/>) variant with
///   <c>youControlOnly: true</c> (CR 109.5 — "you control"), <c>subtype:
///   "Warrior"</c> (CR 205.3 — tribal gate) and <c>includeSelf: true</c>
///   (CR 603.6e — "this creature OR another Warrior" includes the source's own
///   entry). Mardu Woe-Reaper is itself a Warrior, so its own ETB also fires.
/// - <b>Payoff (CR 701.21 / CR 119.3)</b>: the
///   <c>may_exile_target_card_then_gain_life</c>
///   (<see cref="MayExileTargetCardThenGainLifeEffectDef"/>) verb — an OPTIONAL
///   ("you may", <c>MinTargets: 0</c>) exile of a target
///   <c>creature_card_in_graveyard</c>; "If you do" (the exile actually
///   happened) the controller gains 1 life. Declining the may, or an illegal
///   target at resolution, exiles nothing and gains no life.
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Mardu Woe-Reaper")]
public static class MarduWoeReaperFactory
{
    public const string CardName = "Mardu Woe-Reaper";
    public const string PrintedManaCost = "{W}";
    public const int Power = 2;
    public const int Toughness = 1;
    public const int LifeGainAmount = 1;

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "mardu-woe-reaper";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Mardu Woe-Reaper with no live <see cref="TriggerManager"/>
    /// wiring. The ETB trigger is materialised onto the card shape from the JSON
    /// definition for structural / dispatch tests; bus-driven firing requires
    /// the (owner, triggers) overload.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Mardu Woe-Reaper, registering its triggered ability with
    /// <paramref name="triggers"/> when supplied so a qualifying
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> (this or another Warrior
    /// you control entering) automatically queues the ability.
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
