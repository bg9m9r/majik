using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Valley Mightcaller (Bloomburrow — {G}).
///
/// Creature — Frog Warrior 1/1. Oracle text (verified against Scryfall):
///   "Trample
///    Whenever another Frog, Rabbit, Raccoon, or Squirrel you control enters,
///    put a +1/+1 counter on this creature."
///
/// A mono-green Bloomburrow "typal" payoff: every OTHER creature you control
/// that enters as a Frog, Rabbit, Raccoon, or Squirrel pumps Mightcaller with a
/// +1/+1 counter, and Trample lets the accumulated power spill through blockers.
/// The base shape (name, Creature — Frog Warrior, {G}, 1/1, Trample) is
/// materialised from the embedded JSON definition (<c>valley-mightcaller.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/> — Trample is a printed
/// <c>keywords</c> line (CR 702.19), handled entirely in the JSON shape. The
/// counter trigger is layered on top here, since the JSON
/// <c>AbilityDefinition</c> schema doesn't express a multi-subtype
/// other-creature-enters counter trigger (same posture as
/// <see cref="MarwynTheNurturerFactory"/>'s single-subtype Elf trigger).
///
/// ## Implemented (v1)
///
/// ### "Whenever another Frog/Rabbit/Raccoon/Squirrel you control enters, +1/+1 counter." (CR 603.1)
/// A <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/> fires when
/// another permanent enters the controller's battlefield that is a creature with
/// at least one of the four matched subtypes (ToZone == Battlefield,
/// controller-owned, NOT Mightcaller herself — "another"). On resolution it puts
/// one +1/+1 counter on Mightcaller (CR 122 / CR 121.2) via
/// <see cref="Majik.Core.Primitives.Fx.PlaceCounter"/>, routed through the
/// replacement bus so counter-doublers (Hardened Scales / Doubling Season)
/// apply (CR 614.1c). Mightcaller herself is a Frog, but the "another" qualifier
/// excludes her own ETB. There is no once-per-turn lock — every matching enter
/// adds a counter (CR 603.1). One counter per matching enter even if the
/// entering creature shares MULTIPLE of the four subtypes (the trigger condition
/// is "is a Frog, Rabbit, Raccoon, OR Squirrel" — a single event, single
/// counter).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — single-arg dispatcher path
///   (<see cref="NamedCardFactory"/>). The counter trigger is attached for shape
///   observability but NOT registered with any <see cref="TriggerManager"/>.
/// - <see cref="Create(Player, TriggerManager?)"/> — registers the counter
///   trigger so a matching-subtype-enter <see cref="CardMovedEvent"/> fires it
///   automatically.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the counter trigger's <c>activeZones</c> gates it to
///   the battlefield so it no-ops once Mightcaller leaves play (CR 603.6c).
/// </summary>
[CardName("Valley Mightcaller")]
public static class ValleyMightcallerFactory
{
    public const string CardName = "Valley Mightcaller";
    public const string Slug = "valley-mightcaller";

    /// <summary>The four subtypes whose other-creature ETBs grow Mightcaller.</summary>
    private static readonly CardSubtype[] MatchedSubtypes =
    {
        CardSubtype.Frog, CardSubtype.Rabbit, CardSubtype.Raccoon, CardSubtype.Squirrel,
    };

    /// <summary>
    /// Single-arg dispatcher path. The counter trigger is attached structurally
    /// so the card shape is correct, but it is NOT registered with any
    /// <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct a fully-wired Valley Mightcaller.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager the counter trigger registers with
    /// so a matching-subtype-enter <see cref="CardMovedEvent"/> fires it
    /// automatically. May be null — the trigger is still attached to the card
    /// shape.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Creature — Frog Warrior,
        // {G}, 1/1, Trample). The JSON carries Trample as a printed keyword; the
        // counter trigger is layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        BuildCounterTrigger(card, owner, triggers);

        return card;
    }

    // --- "Whenever another Frog/Rabbit/Raccoon/Squirrel you control enters, +1/+1 counter" (CR 603.1) ---

    private static void BuildCounterTrigger(Creature card, Player owner, TriggerManager? triggers)
    {
        // CR 603.1 — "Whenever ANOTHER Frog, Rabbit, Raccoon, or Squirrel YOU
        // control enters, put a +1/+1 counter on this creature."
        //   * ToZone == Battlefield (something entered the battlefield),
        //   * the entering card is a creature with one of the four subtypes,
        //   * its controller is this card's controller ("you control"),
        //   * it is NOT Mightcaller herself ("another").
        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (ReferenceEquals(e.Card, card)) return false; // "another"
            if (e.Card is not Creature entered) return false;
            if (!MatchedSubtypes.Any(entered.HasSubtype)) return false;

            var controller = card.Controller ?? owner;
            return ReferenceEquals(entered.Controller, controller);
        });

        var counterEffect = new Effect(
            $"{CardName} — put a +1/+1 counter on {CardName}",
            // CR 122 / CR 121.2 — put a +1/+1 counter on Mightcaller. Routed
            // through Fx.PlaceCounter so the replacement bus (Hardened Scales /
            // Doubling Season) can adjust the amount (CR 614.1c).
            () => Majik.Core.Primitives.Fx.PlaceCounter(card, CounterType.PlusOnePlusOne, 1));

        var counterTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(counterTrigger);
        triggers?.RegisterTriggeredAbility(counterTrigger);
    }
}
