using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ocelot Pride (Modern Horizons 3, {W}).
///
/// Legendary Creature — Cat 1/1. Oracle text:
///   "Lifelink"
///   "Whenever Ocelot Pride attacks, create a 1/1 white Cat creature token.
///    If you have the city's blessing, instead create two of those tokens."
///   "At the beginning of your end step, if a creature you controlled dealt
///    combat damage to a player this turn, exile this card, then return it
///    to the battlefield under its owner's control."
///
/// ## Implemented (v1)
/// - 1/1 Legendary Creature — Cat at {W}, owner / controller set.
/// - <see cref="KeywordAbility"/> Lifelink marker (CR 702.15), consumed by
///   the standard combat-damage life-gain pipeline.
/// - <b>Attack trigger</b> (CR 508.1f / CR 603.1): "Whenever Ocelot Pride
///   attacks, create a 1/1 white Cat creature token. If you have the
///   city's blessing, instead create two of those tokens." Wired via
///   <see cref="Triggers.OnAttackSelf"/>. Tokens are built via
///   <see cref="TokenFactory.CreateOnBattlefield"/> with
///   <see cref="CardSubtype.Cat"/> and route through <see cref="ZoneService"/>
///   when supplied so <see cref="CardMovedEvent"/> fires for downstream
///   ETB listeners. The doubled branch reads <see cref="Player.HasCitysBlessing"/>
///   (CR 702.131) at resolution.
/// - <b>End-step flicker trigger</b> (CR 500.4 / CR 603.1 + CR 701.20
///   exile / CR 110.2 owner-control): "At the beginning of your end step,
///   if a creature you controlled dealt combat damage to a player this
///   turn, exile this card, then return it to the battlefield under its
///   owner's control." Wired via <see cref="Triggers.OnStepBegin"/> filtered
///   to the controller's End step. Intervening-if (CR 603.4 "if X" check)
///   is evaluated at resolution by inspecting a per-turn "dealt combat
///   damage to a player" latch maintained via a
///   <see cref="CombatDamageDealtEvent"/> subscription gated on
///   <c>TargetPlayer != null</c> and source-creature controller match. The
///   latch resets on every <see cref="TurnStartedEvent"/>. On resolve, the
///   card moves battlefield → exile → battlefield via the supplied
///   <see cref="ZoneService"/> (raw zone moves when no service is wired),
///   restoring controller to owner — mirrors the Ephemerate / Restoration
///   Angel flicker shape using the same Permanent instance (no clone — the
///   v1 simplification, identity preserved; the bus-level "leaves /
///   enters" pair fires through ZoneService.MoveCard).
///
/// ## Deferred (v1 gaps)
/// - <b>True new-object flicker semantics</b>: CR 701.20a treats the
///   returning permanent as a new object. v1 returns the same
///   <see cref="Card"/> instance (preserves abilities + Lifelink marker)
///   — mirrors the existing engine's lack of a "new object on re-entry"
///   primitive (same v1 simplification Skyclave Apparition would face if
///   the exiled permanent ever came back).
/// </summary>
[CardName("Ocelot Pride")]
public static class OcelotPrideFactory
{
    public const string CardName = "Ocelot Pride";
    public const string PrintedManaCost = "{W}";

    /// <summary>
    /// Construct Ocelot Pride with no live ZoneService / event-bus /
    /// TriggerManager wiring. Lifelink keyword + attack trigger + end-step
    /// flicker trigger are attached for shape inspection; triggers are NOT
    /// registered with a manager and no per-turn combat-damage latch is
    /// subscribed. Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Ocelot Pride with optional runtime services. When
    /// <paramref name="zoneService"/> is supplied, spawned tokens + the
    /// exile-and-return flicker move route through <see cref="ZoneService"/>
    /// so <see cref="CardMovedEvent"/> fires. When <paramref name="eventBus"/>
    /// is supplied, a per-turn "creature you controlled dealt combat damage
    /// to a player" latch is subscribed (resets on <see cref="TurnStartedEvent"/>)
    /// so the end-step flicker intervening-if gate is observable. When
    /// <paramref name="triggers"/> is supplied, both the attack trigger and
    /// the end-step flicker trigger are registered for bus-driven firing.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 1,
            toughness: 1,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Cat });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.15 — Lifelink. KeywordAbility marker consumed by the
        // standard combat-damage life-gain pipeline.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // ----------------------------------------------------------------
        // Attack trigger — CR 508.1f / CR 603.1.
        //   "Whenever Ocelot Pride attacks, create a 1/1 white Cat creature
        //    token. If you have the city's blessing, instead create two of
        //    those tokens."
        // City's blessing (Ascend, CR 702.131) reads
        // Player.HasCitysBlessing at resolution — latched true once the
        // controller has had 10+ permanents at any point in the game.
        // ----------------------------------------------------------------
        var attackEffect = new Effect(
            $"{CardName}: create a 1/1 white Cat creature token on attack (two with the city's blessing)",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateCatTokens(controller, count: controller.HasCitysBlessing ? 2 : 1, zoneService);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        // ----------------------------------------------------------------
        // End-step flicker trigger — CR 500.4 / CR 603.1 + CR 701.20.
        //   "At the beginning of your end step, if a creature you controlled
        //    dealt combat damage to a player this turn, exile this card,
        //    then return it to the battlefield under its owner's control."
        //
        // Per-turn "dealt combat damage to a player" latch — subscribed
        // when an event bus is supplied. Closure-captured so the trigger
        // body inspects it at resolution time.
        // ----------------------------------------------------------------
        var damagedAPlayerThisTurn = new bool[] { false };

        if (eventBus != null)
        {
            eventBus.Subscribe<CombatDamageDealtEvent>(e =>
            {
                if (e.TargetPlayer == null) return;
                // "a creature you controlled" — the source must currently be
                // controlled by the Ocelot Pride controller. CR 603.4 treats
                // the controller check at the moment the damage is dealt.
                var ocelotController = card.Controller ?? owner;
                if (!ReferenceEquals(e.Source.Controller, ocelotController)) return;
                damagedAPlayerThisTurn[0] = true;
            });

            eventBus.Subscribe<TurnStartedEvent>(_ => damagedAPlayerThisTurn[0] = false);
        }

        var flickerEffect = new Effect(
            $"{CardName}: end-step exile-and-return if a creature you controlled dealt combat damage to a player",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (!damagedAPlayerThisTurn[0]) return;

                FlickerToOwner(card, zoneService);
            });

        var flickerTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, PhaseStateType.End),
            effects: new IEffect[] { flickerEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(flickerTrigger);
        triggers?.RegisterTriggeredAbility(flickerTrigger);

        return card;
    }

    /// <summary>
    /// CR 603.1 attack-effect — create <paramref name="count"/> 1/1 white
    /// Cat creature tokens under <paramref name="controller"/>'s control.
    /// CR 105 / CR 111.4 — white stamped via
    /// <see cref="TokenFactory.TokenSpec.Colors"/>.
    /// </summary>
    private static void CreateCatTokens(Player controller, int count, ZoneService? zones)
    {
        var spec = new TokenFactory.TokenSpec(
            Name: "Cat",
            Power: 1,
            Toughness: 1,
            Subtypes: new[] { CardSubtype.Cat },
            // CR 105 / CR 111.4 — printed "1/1 white Cat creature token".
            Colors: new[] { Majik.Core.ValueObjects.ManaColor.White });

        for (var i = 0; i < count; i++)
        {
            TokenFactory.CreateOnBattlefield(spec, controller, zones);
        }
    }

    /// <summary>
    /// CR 701.20 — exile then return under owner's control. Mirrors the
    /// Ephemerate / Restoration Angel flicker shape. v1 reuses the same
    /// <see cref="Card"/> instance (preserves abilities + Lifelink marker)
    /// — no new-object semantics (CR 701.20a) yet.
    /// </summary>
    private static void FlickerToOwner(Creature card, ZoneService? zones)
    {
        var owner = card.Owner ?? card.Controller;
        if (owner == null) return;

        if (zones != null)
        {
            zones.MoveCard(card, ZoneType.Battlefield, ZoneType.Exile, owner);
            zones.MoveCard(card, ZoneType.Exile, ZoneType.Battlefield, owner);
        }
        else
        {
            var currentController = card.Controller;
            if (currentController != null)
            {
                currentController.Zones.Battlefield.RemoveCard(card);
            }
            owner.Zones.Exile.AddCard(card);
            card.SetZone(ZoneType.Exile);

            owner.Zones.Exile.RemoveCard(card);
            owner.Zones.Battlefield.AddCard(card);
            card.SetZone(ZoneType.Battlefield);
        }

        card.SetController(owner);
    }
}
