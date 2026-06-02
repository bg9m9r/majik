using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Valley Floodcaller (Bloomburrow, {2}{U}).
/// Creature — Otter Wizard 2/2. Oracle text (verified against Scryfall):
///   "Flash
///    You may cast noncreature spells as though they had flash.
///    Whenever you cast a noncreature spell, Birds, Frogs, Otters, and Rats
///    you control get +1/+1 until end of turn. Untap them."
///
/// The base shape (name, Creature, Otter + Wizard subtypes, {2}{U}, 2/2) is
/// materialised from the embedded JSON definition (<c>valley-floodcaller.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed behaviours
/// (Flash keyword, the noncreature-flash static, the cast-trigger pump+untap)
/// are layered on here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express keyword markers, flash-grant statics, or cast triggers (same
/// posture as <see cref="BeastWhispererFactory"/> /
/// <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Flash (CR 702.8)</b> on Valley Floodcaller itself — a
///   <see cref="KeywordAbility"/> marker consumed by
///   <see cref="Majik.Core.Rules.TimingRules.CanCastAtInstantSpeed"/> (same
///   wiring as <see cref="BrazenBorrowerFactory"/>).
/// - <b>Noncreature-flash static (CR 117.1a / 702.8 / 113.3c)</b>:
///   "You may cast noncreature spells as though they had flash." Wired via a
///   <see cref="FlashGrantStaticEffect"/>: while Valley Floodcaller is on the
///   battlefield, an entry is registered in
///   <see cref="Majik.Core.Rules.FlashGrantRegistry"/> matching every card
///   owned (= controlled at cast-time per CR 108.4) by Valley Floodcaller's
///   controller whose type set does NOT include Creature. The flash grant is
///   consulted at the cast-time speed check (after the printed Instant/Flash
///   check) so the controller may cast their instants/sorceries/artifacts/
///   enchantments/planeswalkers at instant speed while the Floodcaller is on
///   the battlefield. The predicate keys off owner: per CR 108.4 the
///   controller of a card outside the battlefield is its owner, so for cards
///   in hand this is the cast-time controller (same simplification as
///   <see cref="SigardasAidFactory"/>'s "you control" gate). On LTB the
///   grant lifts automatically (FlashGrantStaticEffect's CardMovedEvent
///   lifecycle).
/// - <b>Cast-trigger pump + untap (CR 603.1 / CR 613.1c Layer 7c / CR 514.2)</b>:
///   "Whenever you cast a noncreature spell, Birds, Frogs, Otters, and Rats
///   you control get +1/+1 until end of turn. Untap them." Wired as a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/>,
///   gated to the controller (CR 109.5) AND to a noncreature spell (the
///   same predicate as <see cref="Keywords.ProwessFactory"/> /
///   <see cref="SlickshotShowOffFactory"/>). On resolve it snapshots the
///   controller's battlefield, filters to permanents whose subtypes include
///   Bird, Frog, Otter or Rat, and for each registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+1, +1) (Layer 7c, end-of-turn
///   expirable per CR 514.2) on that creature's
///   <see cref="Creature.ActiveEffects"/> and untaps it (guarded by
///   <see cref="Permanent.IsTapped"/> — <see cref="Permanent.Untap"/> throws
///   on an already-untapped permanent). Self-cast does NOT contribute: the
///   SpellCastEvent for Valley Floodcaller itself fires while it is still a
///   Creature spell on the stack, so the noncreature predicate filters it
///   out (CR 110.4). Multiple noncreature casts in a turn stack additively —
///   each registers a fresh pump (CR 613, multiple Layer 7c effects all
///   apply). The trigger is only active while the Floodcaller is on the
///   battlefield (CR 603.6a; <c>activeZones = {Battlefield}</c>).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The Flash keyword and the
///   cast trigger are attached for inspection; the flash-grant static and
///   the trigger are NOT live-registered (no bus / trigger manager).
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully wired.
///   The flash-grant static attaches (registering with
///   <see cref="Majik.Core.Rules.FlashGrantRegistry"/> while on the
///   battlefield) and the cast trigger registers with
///   <paramref name="triggers"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may"</b>: the static is a permission ("you MAY cast …"), modelled
///   as an always-available flash grant — there is no decline path to model
///   (granting flash never forces a cast).
/// - <b>Controller transfers of cards in hand</b>: the flash-grant predicate
///   keys on owner (CR 108.4 — controller of a card outside the battlefield
///   is its owner). Control-changing effects targeting cards in hand are not
///   modelled by v1 (same posture as <see cref="SigardasAidFactory"/>).
/// </summary>
[CardName("Valley Floodcaller")]
public static class ValleyFloodcallerFactory
{
    public const string CardName = "Valley Floodcaller";
    public const string Slug = "valley-floodcaller";
    public const int Power = 2;
    public const int Toughness = 2;
    private const string FlashKeyword = "Flash";

    private static readonly CardSubtype[] PumpedSubtypes =
    {
        CardSubtype.Bird,
        CardSubtype.Frog,
        CardSubtype.Otter,
        CardSubtype.Rat,
    };

    /// <summary>
    /// Construct Valley Floodcaller with no live wiring. The Flash keyword
    /// marker and the cast-trigger pump are attached to the card shape; the
    /// flash-grant static is created but NOT attached (so the registry isn't
    /// touched on the shape path), and the trigger is not registered (no
    /// trigger manager). Suitable for dispatcher / shape tests. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Valley Floodcaller with optional runtime services. When
    /// <paramref name="eventBus"/> is supplied the noncreature-flash static
    /// attaches (registering with <see cref="Majik.Core.Rules.FlashGrantRegistry"/>
    /// while on the battlefield, releasing on LTB). When
    /// <paramref name="triggers"/> is supplied the cast-trigger pump is
    /// registered so <see cref="SpellCastEvent"/>s published on the bus route
    /// through it.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Otter + Wizard subtypes, {2}{U}, 2/2). The JSON carries no
        // abilities — the three printed behaviours are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // CR 702.8 — Flash on Valley Floodcaller itself. Keyword marker
        // consumed by TimingRules.CanCastAtInstantSpeed (same wiring shape
        // as Brazen Borrower's Flash).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility(FlashKeyword, card, owner));

        // ----------------------------------------------------------------
        // Static — "You may cast noncreature spells as though they had
        // flash." (CR 117.1a / 702.8.) Predicate matches any card owned by
        // the controller whose type set does NOT include Creature. Per
        // CR 108.4 the controller of a card outside the battlefield is its
        // owner, so for cards in hand the owner check is the cast-time
        // controller. Cards already on the battlefield don't need flash, so
        // the predicate is harmless when those are queried.
        // ----------------------------------------------------------------
        var flashGrant = new FlashGrantStaticEffect(
            source: card,
            eventBus: eventBus,
            predicate: c =>
            {
                if (c == null) return false;
                if (!ReferenceEquals(c.Owner, owner)) return false;
                return !c.HasType(CardType.Creature);
            });
        flashGrant.Attach();

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.1.
        //   "Whenever you cast a noncreature spell, Birds, Frogs, Otters,
        //    and Rats you control get +1/+1 until end of turn. Untap them."
        // "You cast" → the spell's controller is this card's controller
        // (CR 109.5). Noncreature filter: the spell's card lacks the
        // Creature type (CR 302.1 / CR 110.4 — once on the stack the spell
        // carries its card types, so casting the Floodcaller itself is a
        // Creature spell and does NOT self-trigger).
        // ----------------------------------------------------------------
        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var liveController = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, liveController)) return false;
            return !e.Spell.Card.HasType(CardType.Creature);
        });

        var pumpEffect = new Effect(
            $"{CardName}: Birds/Frogs/Otters/Rats you control get +1/+1 EOT and untap",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 608.2 — snapshot the battlefield before applying so any
                // same-step zone moves don't disturb enumeration.
                var targets = controller.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Where(IsPumped)
                    .ToList();

                foreach (var creature in targets)
                {
                    if (creature.Zone != ZoneType.Battlefield) continue;

                    // CR 613.1c Layer 7c — +1/+1; CR 514.2 — expires at the
                    // cleanup step. Shape-only safety: without a live
                    // ContinuousEffectsService on the creature the pump
                    // silently no-ops rather than NRE'ing (same posture as
                    // ZealousPersecutionFactory).
                    creature.ActiveEffects?.Register(
                        new PumpUntilEndOfTurnEffect(creature, 1, 1));

                    // "Untap them." Untap throws on an already-untapped
                    // permanent, so guard on IsTapped.
                    if (creature.IsTapped) creature.Untap();
                }
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { pumpEffect },
            // CR 603.6a — only active while Valley Floodcaller is on the
            // battlefield (also why casting it doesn't self-trigger).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }

    private static bool IsPumped(Creature creature)
    {
        foreach (var subtype in PumpedSubtypes)
        {
            if (creature.HasSubtype(subtype)) return true;
        }
        return false;
    }
}
