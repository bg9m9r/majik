using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Omnath, Locus of Creation (Zendikar Rising,
/// {1}{R}{G}{W}{U}).
///
/// Legendary Creature — Elemental 4/4. Oracle text:
///   "When this creature enters, draw a card.
///    Landfall — Whenever a land enters the battlefield under your
///    control, if this is the first time this ability has resolved this
///    turn, you gain 4 life. If it's the second time, add {R}{G}{W}{U}.
///    If it's the third time, Omnath, Locus of Creation deals 4 damage
///    to each opponent and each planeswalker you don't control."
///
/// ## Implemented (v1)
/// - 4/4 Legendary Creature — Elemental, mana cost {1}{R}{G}{W}{U}.
/// - <b>ETB triggered ability (CR 603.6a)</b> via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>: on resolve the
///   controller draws 1 card via <see cref="Fx.DrawCards"/>.
/// - <b>Landfall trigger (CR 614 / Zendikar)</b> via
///   <see cref="Triggers.OnLandEntersUnderControl"/>. Each resolution
///   increments a per-turn counter held in a closure; the resolved
///   effect branches on the new count:
///     1 → controller gains 4 life via <see cref="Fx.GainLife"/>.
///     2 → controller's mana pool gains {R}{G}{W}{U} via
///         <see cref="Player.AddManaToPool"/>.
///     3 → Omnath deals 4 damage to each opponent (supplied by an
///         optional <c>opponentResolver</c>) and each planeswalker the
///         controller doesn't control (supplied by an optional
///         <c>foreignPlaneswalkerResolver</c>).
///     4+ → no further effect this turn (CR 603.10 — the printed
///         oracle has no clause for the 4th resolution onwards).
/// - <b>Per-turn counter reset</b> on <see cref="TurnStartedEvent"/>
///   when an event bus is supplied (CR 500.1) — matches Ledger Shredder
///   / Arclight Phoenix per-turn closure shape.
///
/// ## Deferred (v1 gaps)
/// - <b>Live "each opponent" / "each planeswalker you don't control"
///   enumeration without resolvers</b>: same gap as Sheoldred / Meathook
///   Massacre — <see cref="Player"/> doesn't expose opponent list at
///   construction time. Single-arg dispatcher path attaches both
///   triggers structurally; the 3rd-resolution damage half no-ops
///   without resolvers. Use the fully-wired overload for end-to-end
///   behaviour.
/// - <b>Damage routing</b>: damage routes through
///   <see cref="Fx.DealDamageAny"/> per target (Player /
///   Planeswalker — Omnath itself is the source for any future
///   lifelink / damage-prevention layering; not yet observed by any
///   factory).
/// </summary>
[CardName("Omnath, Locus of Creation")]
public static class OmnathLocusOfCreationFactory
{
    public const string CardName = "Omnath, Locus of Creation";
    public const string PrintedManaCost = "{1}{R}{G}{W}{U}";

    /// <summary>Mana produced on the 2nd landfall resolution.</summary>
    public const string SecondLandfallManaProduced = "RGWU";

    /// <summary>Damage dealt on the 3rd landfall resolution.</summary>
    public const int ThirdLandfallDamage = 4;

    /// <summary>Life gained on the 1st landfall resolution.</summary>
    public const int FirstLandfallLifeGain = 4;

    /// <summary>
    /// Construct Omnath with no live bus / trigger-manager wiring and no
    /// resolvers. Both triggers attach to the card so structural / dispatcher
    /// tests observe their shape; the landfall counter is never reset by
    /// turn boundaries and the 3rd-resolution damage half no-ops without
    /// an opponent resolver. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner,
            opponentResolver: null,
            foreignPlaneswalkerResolver: null,
            eventBus: null,
            triggers: null);

    /// <summary>
    /// Construct Omnath fully wired. <paramref name="opponentResolver"/>
    /// supplies "each opponent" for the 3rd-resolution damage clause.
    /// <paramref name="foreignPlaneswalkerResolver"/> supplies "each
    /// planeswalker you don't control" (typically every planeswalker on
    /// the battlefield whose controller != Omnath's controller).
    /// <paramref name="eventBus"/> drives the per-turn counter reset.
    /// <paramref name="triggers"/> registers both abilities so live bus
    /// events queue them.
    /// </summary>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        Func<IReadOnlyList<Planeswalker>>? foreignPlaneswalkerResolver,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 4,
            toughness: 4,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Elemental });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB — "When this creature enters, draw a card." CR 603.6a.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: ETB — draw a card",
            () => Fx.DrawCards(owner, 1));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Landfall (CR 614 / ZNR) — per-turn counter held in a closure
        // shared between the resolve effect and the TurnStartedEvent
        // reset (CR 500.1).
        // ----------------------------------------------------------------
        var landfallResolutionsThisTurn = new int[] { 0 };

        var landfallEffect = new Effect(
            $"{CardName}: landfall — 1st gains 4 life, 2nd adds {{R}}{{G}}{{W}}{{U}}, 3rd deals 4 to each opp + foreign planeswalker",
            () =>
            {
                landfallResolutionsThisTurn[0]++;
                var n = landfallResolutionsThisTurn[0];

                if (n == 1)
                {
                    Fx.GainLife(owner, FirstLandfallLifeGain);
                    return;
                }

                if (n == 2)
                {
                    // CR 106.4 — mana goes into the pool. Each pip is
                    // a single coloured mana; produce {R}{G}{W}{U}.
                    owner.AddManaToPool(ManaCost.Parse(SecondLandfallManaProduced));
                    return;
                }

                if (n == 3)
                {
                    // CR 119 — damage to each opponent (player) + each
                    // planeswalker the controller doesn't control.
                    var opponents = opponentResolver?.Invoke();
                    if (opponents is not null)
                    {
                        foreach (var opp in opponents)
                        {
                            if (ReferenceEquals(opp, owner)) continue;
                            Fx.DealDamageAny(opp, ThirdLandfallDamage);
                        }
                    }

                    var foreignPws = foreignPlaneswalkerResolver?.Invoke();
                    if (foreignPws is not null)
                    {
                        foreach (var pw in foreignPws)
                        {
                            if (ReferenceEquals(pw.Controller, owner)) continue;
                            Fx.DealDamageAny(pw, ThirdLandfallDamage);
                        }
                    }
                    return;
                }

                // n >= 4: no further effect this turn — CR 603.10. The
                // counter keeps incrementing only so the predicate stays
                // observable; the cap is implicit in the if-cascade.
            });

        var landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { landfallEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);

        // CR 500.1 — reset the per-turn count when a new turn starts.
        if (eventBus is not null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => landfallResolutionsThisTurn[0] = 0);
        }

        return card;
    }
}
