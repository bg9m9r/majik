using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cori-Steel Cutter (Tarkir: Dragonstorm, {1}{R}).
///
/// Artifact — Equipment. Oracle text (Scryfall, 2025-04-11):
///   "Equipped creature gets +1/+1 and has trample and haste.
///    Flurry — Whenever you cast your second spell each turn, create a 1/1
///    white Monk creature token with prowess. You may attach this Equipment
///    to it.
///    Equip {1}{R}"
///
/// ## Implementation
///
/// - <b>Static "+1/+1 and has trample and haste"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (P/T modification, CR 613
///   Layer 7c) AND a parallel Layer 6 keyword-grant copy (CR 613.1c — ability
///   addition layer) carrying the two granted keyword strings. The boost
///   reads the source's <see cref="Permanent.AttachedTo"/> dynamically so
///   re-equipping transfers without re-registration. Mirrors the dual-layer
///   pattern used by <see cref="SwordOfFireAndIceFactory"/> for +2/+2 + the
///   Colossus Hammer family for the keyword grant flavour.
/// - <b>Flurry trigger (CR 603.1 / new keyword in Tarkir: Dragonstorm —
///   "Whenever you cast your second spell each turn ...")</b>: a closure
///   over a per-turn cast counter on a <see cref="SpellCastEvent"/> watcher
///   filtered to the equipment's controller. Predicate increments the count
///   on every controller spell and matches on the exact transition to 2,
///   mirroring <see cref="LedgerShredderFactory"/>'s second-spell predicate.
///   On resolution: (1) spawn a 1/1 white Monk creature token with the
///   <c>"Prowess"</c> keyword marker via
///   <see cref="TokenFactory.CreateOnBattlefield"/>; (2) attach Cori-Steel
///   Cutter to it ("you may" auto-accepted at v1 — same posture as
///   Eternal Witness / Tireless Tracker / Snapcaster Mage's may-clauses).
///   Per-turn count reset hooks on <see cref="TurnStartedEvent"/> when an
///   event bus is supplied (CR 500.1).
/// - <b>Equip {1}{R}</b> — activated ability (CR 702.6). Cost is
///   <c>{1}{R}</c>. v1 picker is deterministic: the first creature on the
///   controller's battlefield. Same shape as
///   <see cref="ColossusHammerFactory"/> / <see cref="SwordOfFireAndIceFactory"/>.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only — suitable for factory-
/// shape / dispatch tests. The +1/+1 boost + granted keywords are not
/// registered against any <see cref="ContinuousEffectsService"/>; the
/// Flurry trigger is attached for shape but never fires (no event bus →
/// the per-turn count is never incremented and never reset). Use the
/// (owner, continuousEffects, zoneService, eventBus, triggers) overload
/// for fully-wired behaviour.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Prowess pump on the spawned Monk token</b>: the <c>"Prowess"</c>
///   keyword marker is attached to the Monk token so shape inspection sees
///   the printed reminder text, but the
///   <see cref="Majik.Core.Keywords.ProwessFactory"/> triggered-ability
///   pump requires a live <see cref="ContinuousEffectsService"/> threaded
///   through <see cref="TokenFactory"/>. Same v1 gap as
///   <see cref="StormchasersTalentFactory"/>'s Mercenary tokens and
///   <see cref="MonasteryMentorFactory"/>'s Monk tokens.
/// - <b>"You may attach this Equipment to it" prompt</b>: v1 auto-accepts
///   the may-clause and unconditionally attaches Cori-Steel Cutter to the
///   spawned Monk token. A real prompt-driven flow (and the "to it"
///   bookkeeping that pins attachment to *that specific* token rather
///   than any creature) is deferred behind the broader agent-prompt
///   surface — same posture as Eternal Witness / Snapcaster Mage.
/// - <b>Sorcery-speed restriction on Equip activation (CR 702.6a)</b> —
///   same gap as <see cref="ColossusHammerFactory"/> /
///   <see cref="SwordOfFireAndIceFactory"/>.
/// - <b>Attach-target prompt for Equip</b> — v1 picks the first
///   controller-side creature deterministically.
/// </summary>
[CardName("Cori-Steel Cutter")]
public static class CoriSteelCutterFactory
{
    public const string CardName = "Cori-Steel Cutter";
    public const string PrintedManaCost = "{1}{R}";
    public const string EquipCost = "{1}{R}";

    /// <summary>
    /// Constructs Cori-Steel Cutter with no live runtime wiring (the shape /
    /// dispatcher path). The +1/+1 boost + keyword grant is NOT registered
    /// against any service; the Flurry trigger is attached to the card for
    /// shape but never fires (no event bus subscription to reset the per-
    /// turn count). Equip {1}{R} is wired as a vanilla activated ability.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Constructs Cori-Steel Cutter with optional runtime services. When
    /// <paramref name="continuousEffects"/> is supplied the +1/+1 boost
    /// (Layer 7c) and the granted "trample" + "haste" keywords (Layer 6)
    /// are registered as two parallel <see cref="AttachedBoostEffect"/>s;
    /// each gates on the Equipment being on the battlefield AND attached
    /// to a battlefield permanent. When <paramref name="eventBus"/> is
    /// supplied a <see cref="TurnStartedEvent"/> handler resets the per-
    /// turn cast count (CR 500.1). When <paramref name="triggers"/> is
    /// supplied the Flurry trigger is registered for bus-driven firing.
    /// When <paramref name="zoneService"/> is supplied the spawned Monk
    /// token routes through <see cref="TokenFactory.CreateOnBattlefield"/>
    /// using the service so the token publishes
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> on battlefield entry.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Static continuous effects — "Equipped creature gets +1/+1 and
        // has trample and haste." Two AttachedBoostEffects: one at Layer
        // 7c carrying the +1/+1, one at Layer 6 carrying the granted
        // keyword names ("Trample", "Haste"). Both gate on the source
        // being on the battlefield AND attached (AttachedBoostEffect.
        // IsActive).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            // CR 613 Layer 7c — P/T modification.
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 1, toughness: 1));

            // CR 613.1c — Layer 6 ability addition. Granted keywords ride
            // on a P/T-neutral effect so the layers compute reads the
            // keyword names off the equipped creature's working set.
            continuousEffects.Register(
                new AttachedBoostEffect(
                    card,
                    power: 0,
                    toughness: 0,
                    grantedKeywords: new[] { "Trample", "Haste" },
                    layer: Layer.Abilities));
        }

        // --------------------------------------------------------------
        // Flurry trigger — CR 603.1 / Tarkir: Dragonstorm keyword.
        //   "Flurry — Whenever you cast your second spell each turn,
        //    create a 1/1 white Monk creature token with prowess. You
        //    may attach this Equipment to it."
        // Per-turn count closure shared between the predicate and the
        // TurnStartedEvent reset handler (mirrors LedgerShredderFactory).
        // --------------------------------------------------------------
        var spellsCastThisTurn = new int[] { 0 };

        var flurryCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Spell.Controller, owner)) return false;
            spellsCastThisTurn[0]++;
            return spellsCastThisTurn[0] == 2;
        });

        var flurryEffect = new Effect(
            $"{CardName}: Flurry — create a 1/1 white Monk token with prowess and attach this Equipment to it",
            () =>
            {
                // 1) Create the 1/1 white Monk token (CR 105 / CR 111.4 —
                //    white stamped via TokenSpec.Colors). Prowess pump on
                //    the token is deferred — see class xmldoc.
                var controller = card.Controller ?? owner;
                var spec = new TokenFactory.TokenSpec(
                    Name: "Monk",
                    Power: 1,
                    Toughness: 1,
                    Subtypes: new[] { CardSubtype.Monk },
                    Keywords: new[] { "Prowess" },
                    // CR 105 / CR 111.4 — printed "1/1 white Monk creature
                    // token with prowess".
                    Colors: new[] { Majik.Core.ValueObjects.ManaColor.White });
                var token = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

                // 2) "You may attach this Equipment to it." v1 auto-accepts
                //    the may-clause (CR 117 — controller choices made on
                //    resolution). Only attach if Cori-Steel Cutter is still
                //    on the battlefield (CR 702.6 — equip targets a creature
                //    you control; the Equipment must still be in play).
                if (card.Zone == ZoneType.Battlefield)
                {
                    card.AttachTo(token);
                }
            });

        var flurryTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: flurryCondition,
            effects: new IEffect[] { flurryEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(flurryTrigger);
        triggers?.RegisterTriggeredAbility(flurryTrigger);

        // CR 500.1 — reset the per-turn count when a new turn starts.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => spellsCastThisTurn[0] = 0);
        }

        // --------------------------------------------------------------
        // Equip {1}{R} — activated ability (CR 702.6).
        //   "{1}{R}: Attach to target creature you control. Activate only
        //    as a sorcery."
        // v1 picker: deterministic first controller-side creature.
        // Sorcery-speed restriction deferred (see class xmldoc).
        // --------------------------------------------------------------
        var equipEffect = new Effect(
            $"{CardName}: equip — attach to a creature you control",
            () =>
            {
                var bearer = owner.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .FirstOrDefault(c => ReferenceEquals(c.Controller, owner));
                if (bearer == null) return; // No legal target → no-op.
                card.AttachTo(bearer);
            });

        var equipAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(EquipCost) },
            effects: new IEffect[] { equipEffect });

        card.AddAbility(equipAbility);

        return card;
    }
}
