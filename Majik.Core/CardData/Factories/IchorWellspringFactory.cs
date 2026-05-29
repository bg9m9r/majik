using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ichor Wellspring (Scars of Mirrodin / reprints,
/// {2}).
///
/// Artifact. Oracle text:
///   "When this artifact enters or is put into a graveyard from the
///    battlefield, draw a card."
///
/// Closest analogue is <see cref="ChromaticStarFactory"/> — same
/// Battlefield → Graveyard <see cref="CardMovedEvent"/>-driven cantrip,
/// wired via <see cref="Triggers.OnDies"/>. Ichor Wellspring adds the
/// symmetric ETB cantrip (<see cref="Triggers.OnEnterBattlefieldSelf"/>),
/// so it is two independent draw triggers rather than one.
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {2}, owner / controller wiring).
/// - <b>ETB draw trigger</b> — <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> (CR 603.6a — fires on the
///   self Hand/anywhere → Battlefield <see cref="CardMovedEvent"/>).
///   <c>activeZones = {Battlefield}</c> (the ability is on the battlefield
///   when it triggers and resolves). Resolves to
///   <see cref="Fx.DrawCards"/>(controller, 1).
/// - <b>LTB draw trigger</b> — <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnDies"/> (CR 700.4 / 603.6 — Battlefield →
///   Graveyard self-move; <c>OnDies</c> is permanent-agnostic despite the
///   creature-flavoured name). <c>activeZones = {Battlefield, Graveyard}</c>
///   so the trigger still matches whether the engine evaluates the zone gate
///   just-before the move (source still on battlefield, CR 603.10c
///   last-known-information) or just-after (source already in graveyard) —
///   mirrors <see cref="ChromaticStarFactory"/>'s LTB wiring. Resolves to
///   <see cref="Fx.DrawCards"/>(controller, 1).
///
/// Both legs are independent triggered abilities (CR 603.3) — entering and
/// leaving are mutually exclusive events on a single object, so they never
/// stack together off one zone change.
///
/// ## Deferred (v1 gaps)
/// - <b>Live TriggerManager wiring</b>: the single-arg factory attaches both
///   triggers to the card for shape inspection but does not register them
///   with a <see cref="TriggerManager"/>. The overload registers them so the
///   bus surfaces them automatically (mirrors Chromatic Star / The One Ring).
/// </summary>
[CardName("Ichor Wellspring")]
public static class IchorWellspringFactory
{
    public const string CardName = "Ichor Wellspring";
    public const string PrintedManaCost = "{2}";

    /// <summary>
    /// Construct Ichor Wellspring with no live trigger-manager wiring. Both
    /// draw triggers are attached to <see cref="Card.Abilities"/> so shape
    /// tests can observe them; for end-to-end firing pass a live
    /// <see cref="TriggerManager"/> via the overload.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Ichor Wellspring with optional trigger-manager wiring. When
    /// <paramref name="triggers"/> is supplied, both draw triggers are
    /// registered so the bus surfaces them automatically (mirrors Chromatic
    /// Star's two-arg pattern).
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var well = new Artifact(CardName, PrintedManaCost);
        well.SetOwner(owner);
        well.SetController(owner);

        // ----------------------------------------------------------------
        // When this artifact enters, draw a card. CR 603.6a — self-ETB
        // trigger over the (anywhere → Battlefield) CardMovedEvent.
        // activeZones={Battlefield}: the ability resolves while the
        // artifact sits on the battlefield.
        // ----------------------------------------------------------------
        var etbDraw = new Effect(
            "Ichor Wellspring: draw a card on enter-the-battlefield",
            () => Fx.DrawCards(owner, 1));

        var etbTrigger = new TriggeredAbility(
            source: well,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(well),
            effects: new IEffect[] { etbDraw },
            activeZones: new[] { ZoneType.Battlefield });

        well.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // ...or is put into a graveyard from the battlefield, draw a card.
        // CR 700.4 / 603.6 — Battlefield → Graveyard self-move. Triggers.
        // OnDies is shape-generic over CardMovedEvent (FromZone=Battlefield
        // → ToZone=Graveyard for the source). activeZones={Battlefield,
        // Graveyard} so the gate matches whether the engine evaluates pre-
        // or post-move (mirrors Chromatic Star's LTB trigger).
        // ----------------------------------------------------------------
        var ltbDraw = new Effect(
            "Ichor Wellspring: draw a card on LTB battlefield->graveyard",
            () => Fx.DrawCards(owner, 1));

        var ltbTrigger = new TriggeredAbility(
            source: well,
            controller: owner,
            condition: Triggers.OnDies(well),
            effects: new IEffect[] { ltbDraw },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        well.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return well;
    }
}
