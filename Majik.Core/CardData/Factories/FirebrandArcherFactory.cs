using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Firebrand Archer (Hour of Devastation, {1}{R}).
///
/// Creature — Human Archer 2/1. Oracle text (verified against Scryfall):
///   "Whenever you cast a noncreature spell, this creature deals 1 damage to
///    each opponent."
///
/// Functional reprint of Kessig Flamebreather. Combines two established
/// engine patterns:
///   - The noncreature-cast trigger predicate of
///     <see cref="ThirdPathIconoclastFactory"/> / Monastery Mentor (a
///     <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> that
///     fires when the controller casts a spell whose card is NOT a Creature —
///     CR 205.3 / 302.1 / 603.1).
///   - The "deal 1 damage to each opponent" burn half of
///     <see cref="VoldarenEpicureFactory"/> — damage routes through
///     <see cref="Fx.DealDamageAny"/> over an injected
///     <c>opponentResolver</c>, since the Player aggregate exposes no
///     opponents list at v1 (same posture as Creeping Chill / Omnath).
///
/// ## Implementation
///
/// - 2/1 Human Archer, mana cost {1}{R}. Base shape (name, Creature,
///   Human/Archer subtypes, {1}{R}, 2/1) is materialised from the embedded
///   JSON definition (<c>firebrand-archer.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. The JSON carries no abilities;
///   the noncreature-cast burn trigger is layered on here (the JSON
///   AbilityDefinition schema doesn't yet express this trigger shape, same
///   posture as <see cref="ThirdPathIconoclastFactory"/>).
/// - <b>Noncreature-cast burn trigger (CR 603.1)</b>: fires whenever this
///   card's controller casts a noncreature spell. On resolution the archer
///   deals 1 damage to each opponent (CR 119 — damage to a player is life
///   loss).
///
/// ## Deferred (v1 gaps)
/// - <b>Live "each opponent" enumeration</b>: no <c>Player.Opponents</c>
///   accessor exists at v1; the resolver-injection pattern is shared with
///   <see cref="VoldarenEpicureFactory"/> / <see cref="CreepingChillFactory"/>
///   / <see cref="OmnathLocusOfCreationFactory"/>. Without a resolver the burn
///   half silently no-ops; the trigger still fires and is observable on the
///   stack.
/// - <b>Damage source threading</b>: the printed text makes the archer the
///   damage source ("this creature deals 1 damage"). <see cref="Fx.DealDamageAny"/>
///   is target-side and doesn't yet thread the source through (matches
///   Voldaren Epicure / Creeping Chill — deferred at the primitive level).
/// </summary>
[CardName("Firebrand Archer")]
public static class FirebrandArcherFactory
{
    public const string CardName = "Firebrand Archer";
    public const string Slug = "firebrand-archer";
    public const int DamageAmount = 1;

    /// <summary>
    /// Construct Firebrand Archer with no live bus / trigger-manager wiring.
    /// The burn trigger is attached to the card for shape observability and
    /// the burn half no-ops at resolution (no opponent resolver). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, opponentResolver: null);

    /// <summary>
    /// Construct Firebrand Archer with optional event bus, trigger manager,
    /// and opponent resolver. When <paramref name="triggers"/> is supplied the
    /// burn trigger is registered so the bus surfaces it as pending on a
    /// matching <see cref="SpellCastEvent"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Not used directly by this factory; reserved for
    /// future lifecycle subscribers (e.g. LTB unregister).</param>
    /// <param name="triggers">TriggerManager for the burn trigger. May be
    /// null — the trigger is still attached to the card shape.</param>
    /// <param name="opponentResolver">Live enumerator of "each opponent" for
    /// the burn half. Without a resolver the damage half no-ops.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human/Archer subtypes, {1}{R}, 2/1). The JSON carries no abilities —
        // the burn trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 603.1 — "Whenever you cast a noncreature spell, this creature
        // deals 1 damage to each opponent."
        // Predicate: the spell's controller matches this card's controller AND
        // the spell's card is NOT a Creature (CR 205.3 / 302.1 — a
        // "noncreature spell" is any spell that isn't a creature spell, so
        // artifact creatures and other creature spells are excluded). Same
        // noncreature filter as Third Path Iconoclast / Monastery Mentor.
        var burnCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && !e.Spell.Card.HasType(CardType.Creature));

        var burnEffect = new Effect(
            $"{CardName}: deal {DamageAmount} damage to each opponent (whenever you cast a noncreature spell)",
            () =>
            {
                // CR 119 — damage to each opponent is life loss. The Player
                // aggregate exposes no opponents list at v1, so the caller
                // threads them through via opponentResolver (same pattern as
                // Voldaren Epicure / Creeping Chill). Without a resolver the
                // burn half no-ops.
                var opponents = opponentResolver?.Invoke();
                if (opponents == null) return;

                foreach (var opp in opponents)
                {
                    if (ReferenceEquals(opp, owner)) continue;
                    Fx.DealDamageAny(opp, DamageAmount);
                }
            });

        var burnTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: burnCondition,
            effects: new IEffect[] { burnEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(burnTrigger);
        triggers?.RegisterTriggeredAbility(burnTrigger);

        return card;
    }
}
