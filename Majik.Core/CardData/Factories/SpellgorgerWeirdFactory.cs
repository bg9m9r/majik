using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spellgorger Weird (Guilds of Ravnica, {2}{R}).
///
/// Creature — Weird 2/2. Oracle text (Scryfall, verified):
///   "Whenever you cast a noncreature spell, put a +1/+1 counter on this
///    creature."
///
/// ## Pure-JSON factory (declarative trigger + effect)
/// Spellgorger Weird is now fully declarative — the cast trigger is expressed
/// by the <c>whenever_you_cast_spell</c> (<see cref="WheneverYouCastSpellTriggerDef"/>,
/// <c>noncreatureOnly</c>) trigger variant and the payoff by the existing
/// <c>put_counter</c> self effect, both materialised by
/// <see cref="CardDefRuntime"/> from <c>spellgorger-weird.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build(CardDefinition, Player, ReplacementBus?)"/>.
/// This replaces the prior hand-rolled <see cref="EventTriggerCondition{TEvent}"/>
/// over <see cref="SpellCastEvent"/> — proving the declarative cast-trigger
/// shape (CR 109.5 "you cast" + CR 112.1 noncreature) carries the same
/// behaviour.
///
/// - <b>Noncreature-cast counter trigger (CR 603.1)</b>: fires on
///   <see cref="SpellCastEvent"/> where the spell's controller is the Weird's
///   controller AND the spell is noncreature; on resolution one
///   <see cref="Majik.Core.Counters.CounterType.PlusOnePlusOne"/> counter is
///   placed via <see cref="CountersService.Add"/> so Hardened Scales /
///   Doubling Season replacements (CR 614) can rewrite the count.
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Spellgorger Weird")]
public static class SpellgorgerWeirdFactory
{
    public const string CardName = "Spellgorger Weird";

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "spellgorger-weird";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Spellgorger Weird with no live wiring. The cast trigger is
    /// materialised onto the card shape from the JSON definition; not
    /// registered with a <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, replacements: null);

    /// <summary>
    /// Construct Spellgorger Weird with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Reserved for future lifecycle subscribers; not
    /// consumed directly today.</param>
    /// <param name="triggers">When supplied, the declarative cast trigger
    /// materialised from the JSON definition is registered so a qualifying
    /// <see cref="SpellCastEvent"/> auto-queues the ability. May be null — the
    /// trigger is still attached to the card shape for inspection.</param>
    /// <param name="replacements">ReplacementBus routed into the counter
    /// placement (CR 614). May be null — the counter is placed directly.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ReplacementBus? replacements)
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
