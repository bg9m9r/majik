using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bloodghast (Zendikar, {1}{B}).
///
/// Creature — Vampire Spirit 2/1. Oracle text:
///   "Bloodghast can't block.
///    Landfall — Whenever a land enters the battlefield under your control,
///    you may return Bloodghast from your graveyard to the battlefield.
///    Bloodghast has haste as long as an opponent has 10 or less life."
///
/// ## Implemented (v1)
/// - 2/1 Vampire Spirit with mana cost {1}{B}, owner/controller assigned.
/// - <b>Can't block</b> — registered as a permanent
///   <see cref="CombatRestrictionEffect"/> (<see cref="CombatRestriction.CannotBlock"/>,
///   <c>expiresAtEndOfTurn = false</c>) on the supplied
///   <see cref="ContinuousEffectsService"/>. Without the service (single-arg
///   dispatcher path), the restriction is not registered — shape tests only.
/// - <b>Landfall trigger (CR 603.6d — graveyard-resident trigger)</b>:
///   watches <see cref="CardMovedEvent"/>; fires when a Land card enters
///   the battlefield under Bloodghast's controller's control (destination =
///   Battlefield, card has type Land, card's Controller is Bloodghast's
///   owner). Active only while Bloodghast is in its owner's Graveyard
///   (<c>activeZones = {Graveyard}</c>). On resolve: if a
///   <see cref="ZoneService"/> is wired, uses
///   <see cref="ZoneService.MoveCard"/> so ETB triggers fire; otherwise
///   performs a raw zone move. "You may" is auto-accepted (same posture as
///   Arclight Phoenix — v1 simplification).
/// - <b>Haste conditional (v1 simplification)</b>: when an
///   <c>opponentLifeProvider</c> <see cref="Func{int}"/> is supplied,
///   Bloodghast receives a <see cref="KeywordAbility"/> "Haste" marker if
///   the function returns ≤ 10 at construction time (snapshot). The
///   condition is not re-evaluated dynamically (no continuous layer check
///   against live life totals). Single-arg and no-provider paths omit
///   haste entirely.
///
/// ## Deferred (v1 gaps)
/// - Dynamic haste re-evaluation: the oracle text is "has haste as long as
///   an opponent has 10 or less life", implying the Haste keyword comes and
///   goes as life totals change mid-game. V1 snapshots the condition once.
///   A proper continuous effect (Layer 6 keyword grant gated on a live
///   life-total predicate) is deferred until the conditional-keyword CDA
///   surface exists.
/// - "You may" prompt: auto-accepted (same gap as Arclight Phoenix /
///   Sneak Attack / Tireless Tracker).
/// - Can't-block enforcement without a ContinuousEffectsService: the
///   restriction is not registered on the single-arg dispatcher path, so
///   shape tests that need it should use the full-wiring overload.
/// </summary>
[CardName("Bloodghast")]
public static class BloodghastFactory
{
    public const string CardName = "Bloodghast";

    /// <summary>
    /// Construct Bloodghast with no runtime service wiring. The card has the
    /// correct shape (name, type, P/T, mana cost, subtypes) and the landfall
    /// trigger is attached for structural inspection, but the can't-block
    /// restriction is not registered and the trigger is not enrolled with a
    /// <see cref="TriggerManager"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, zoneService: null, triggers: null, opponentLifeProvider: null, agent: null);

    /// <summary>
    /// Construct Bloodghast with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for the permanent
    /// can't-block restriction. May be null — restriction is not enforced
    /// without it.</param>
    /// <param name="zoneService">Zone-service used by the landfall trigger
    /// to move Bloodghast from graveyard to battlefield so ETB triggers
    /// fire (CR 603.6a). May be null — raw zone move performed instead.</param>
    /// <param name="triggers">Trigger manager for graveyard-resident trigger
    /// registration (CR 603.6d). May be null — trigger is attached to the
    /// card for shape but not registered with the bus.</param>
    /// <param name="opponentLifeProvider">Func returning the minimum opponent
    /// life total at snapshot time. When non-null and the result ≤ 10,
    /// a Haste keyword marker is granted (v1 snapshot — see class xmldoc).
    /// May be null — haste is omitted.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        ZoneService? zoneService,
        TriggerManager? triggers,
        Func<int>? opponentLifeProvider)
        => Create(owner, effects, zoneService, triggers, opponentLifeProvider, agent: null);

    /// <summary>
    /// Construct Bloodghast with the agent-prompt MVP wiring. When
    /// <paramref name="agent"/> is non-null the landfall "you may return"
    /// trigger consults <see cref="IPlayerAgent.ChooseYesNoAsync"/>
    /// (<see cref="BotIntent.Reanimate"/>); false declines the return.
    /// Null preserves the legacy auto-accept v1 posture.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        ZoneService? zoneService,
        TriggerManager? triggers,
        Func<int>? opponentLifeProvider,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: "{1}{B}",
            power: 2,
            toughness: 1,
            subtypes: new[] { CardSubtype.Vampire, CardSubtype.Spirit });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Can't block — CR 509.1c.
        // Permanent restriction (expiresAtEndOfTurn = false) registered on
        // the ContinuousEffectsService so CombatValidator.CanBlock returns
        // false for this creature.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            effects.Register(new CombatRestrictionEffect(
                CombatRestriction.CannotBlock,
                target: card,
                expiresAtEndOfTurn: false));
        }

        // ----------------------------------------------------------------
        // Haste conditional (v1 snapshot, CR 702.10).
        // "Bloodghast has haste as long as an opponent has 10 or less life."
        // V1 simplification: check once at construction. A full dynamic
        // Layer 6 conditional keyword grant is deferred — see class xmldoc.
        // ----------------------------------------------------------------
        if (opponentLifeProvider != null && opponentLifeProvider() <= 10)
        {
            card.AddAbility(new KeywordAbility("Haste", card, owner));
        }

        // ----------------------------------------------------------------
        // Landfall trigger — CR 603.1 / 603.6d.
        //   "Whenever a land enters the battlefield under your control, you
        //    may return Bloodghast from your graveyard to the battlefield."
        // Active only while Bloodghast is in its owner's Graveyard.
        // Fires on CardMovedEvent filtered to:
        //   - ToZone == Battlefield
        //   - card is a Land (CR 205.3f)
        //   - card.Controller == owner (entering under your control)
        //
        // Cast-marker note (CR 113.5 / CR 400.7): the printed return-trigger
        // does NOT itself care whether the triggering land was cast — any
        // land ETB triggers it. The reason the cast-marker primitive
        // matters for Bloodghast is the reverse direction: when Bloodghast
        // itself returns to the battlefield via this trigger, its
        // Card.WasCast remains false (it's a put-onto-battlefield path,
        // not a cast), so a future Containment Priest sitting opposite
        // would correctly exile the return. The clear-on-LTB in
        // ZoneService also means a previously-cast Bloodghast loses its
        // WasCast stamp on the way to the graveyard, so the subsequent
        // landfall return starts with a clean WasCast = false.
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: return from graveyard to battlefield (landfall trigger)",
            async ctx =>
            {
                // CR 603.6d — re-check zone at resolution.
                // "You may" — when an agent is wired, consult
                // ChooseYesNoAsync(Reanimate); else legacy auto-accept.
                if (card.Zone != ZoneType.Graveyard) return;
                if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;
                if (agent != null)
                {
                    var yes = (await agent.ChooseYesNoAsync(
                        "Return Bloodghast from graveyard to battlefield?",
                        BotIntent.Reanimate).ConfigureAwait(false));
                    if (!yes) return;
                }

                if (zoneService != null)
                {
                    // ZoneService.MoveCard fires ETB triggers + replacements
                    // (CR 603.6a).
                    zoneService.MoveCard(card, ZoneType.Graveyard, ZoneType.Battlefield, owner);
                }
                else
                {
                    // Raw zone move — no ETB event published.
                    owner.Zones.Graveyard.RemoveCard(card);
                    owner.Zones.Battlefield.AddCard(card);
                    card.SetZone(ZoneType.Battlefield);
                    card.SetController(owner);
                }
            });

        var landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
            {
                if (e.ToZone != ZoneType.Battlefield) return false;
                if (!e.Card.HasType(CardType.Land)) return false;
                // "under your control" — entering card's controller must be
                // Bloodghast's owner at the moment the event fires (CR 614.6
                // — controller is assessed on the live battlefield state after
                // the move completes).
                return ReferenceEquals(e.Card.Controller, owner);
            }),
            effects: new IEffect[] { returnEffect },
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);

        return card;
    }
}
