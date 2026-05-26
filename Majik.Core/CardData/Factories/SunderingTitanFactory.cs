using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sundering Titan (Mirrodin, {8}).
///
/// Artifact Creature — Phyrexian Golem 7/10. Oracle text:
///   "When Sundering Titan enters, choose one land of each basic land type,
///    then destroy those lands. When Sundering Titan leaves the battlefield,
///    choose one land of each basic land type, then destroy those lands."
///
/// ## Implemented (v1)
/// - 7/10 Artifact Creature — Phyrexian Golem at {8}. Both card types layered
///   on (CR 301.1 / 302.1) and both subtypes attached.
/// - <b>ETB triggered ability (CR 603.6a)</b>: "choose one land of each basic
///   land type, then destroy those lands." For each of the five basic land
///   types (Plains/Island/Swamp/Mountain/Forest per CR 305.6) the effect
///   scans every player's battlefield, picks the first land carrying that
///   subtype, and routes it through <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/>. Indestructible (CR 702.12)
///   and regeneration (CR 701.15) gates apply naturally via that primitive.
/// - <b>LTB triggered ability (CR 603.6c / CR 603.10c)</b>: same shape as
///   the ETB but fires whenever Sundering Titan moves OUT of the battlefield
///   (any destination — dies / bounce / flicker, same pattern as Skyclave
///   Apparition). Uses an inline <see cref="EventTriggerCondition{TEvent}"/>
///   over <see cref="CardMovedEvent"/>.
///
/// ## v1 simplifications
/// - <b>No 5-target modal</b>: the oracle "choose one land of each basic land
///   type" is a five-target picker (one per type). v1 deterministically picks
///   the FIRST land of each type found across all players' battlefields,
///   rather than prompting the controller via an agent for each type. This
///   matches the doc's explicit fallback ("If 5-target modal is complex,
///   simplify: for each basic type, destroy first-found land of that type.")
///   and the same posture used by Show and Tell / Sun Titan's default picker.
/// - <b>Dual-type lands</b>: a land that has multiple basic subtypes (e.g.
///   Tundra = Plains+Island) qualifies for both passes. The first-found
///   scan will destroy it on whichever pass reaches it first; the second
///   pass for the other type will then pick a different land. Matches the
///   printed wording — the chooser can pick the same land for at most one
///   type slot anyway.
/// - <b>No basic-type → no destroy</b>: if no land of a given basic type is
///   on any battlefield, that type's slot simply contributes nothing (no
///   destroy fires). Matches CR 117.x — a choice that can't be made is
///   skipped.
///
/// ## Deferred (v1 gaps)
/// - <b>Controller-driven five-target picker</b>: the printed wording lets
///   the controller pick which Mountain (when there are two on the table)
///   gets destroyed. Same agent-prompt queue as Show and Tell / Sun Titan.
/// - <b>Wastes</b>: CR 305.6 lists Wastes as a basic land subtype but the
///   oracle text says "basic land type" (singular), which by precedent
///   (Sundering Titan was printed pre-Wastes) means the five colours' lands
///   only. v1 honours the printed list.
/// </summary>
[CardName("Sundering Titan")]
public static class SunderingTitanFactory
{
    public const string CardName = "Sundering Titan";
    public const string PrintedManaCost = "{8}";
    public const int Power = 7;
    public const int Toughness = 10;

    /// <summary>
    /// The five basic land types per CR 305.6 (Plains/Island/Swamp/
    /// Mountain/Forest). Wastes is excluded — Sundering Titan predates
    /// Wastes and the printed oracle wording stays restricted to the
    /// classic five.
    /// </summary>
    private static readonly CardSubtype[] BasicLandTypes =
    {
        CardSubtype.Plains,
        CardSubtype.Island,
        CardSubtype.Swamp,
        CardSubtype.Mountain,
        CardSubtype.Forest,
    };

    /// <summary>
    /// Construct Sundering Titan with no live <see cref="TriggerManager"/>
    /// and a default players-snapshot that contains only <paramref name="owner"/>.
    /// ETB + LTB triggered abilities are attached to the card shape but
    /// not registered with the bus. Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, allPlayers: null, triggers: null);

    /// <summary>
    /// Construct Sundering Titan with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / controller.</param>
    /// <param name="allPlayers">Resolver for the live set of players whose
    /// battlefields should be scanned for basic-typed lands at ETB / LTB
    /// resolution. When <c>null</c>, scans only <paramref name="owner"/>'s
    /// battlefield — sufficient for one-player shape tests. Real game
    /// callers should supply <c>() =&gt; game.Players</c>.</param>
    /// <param name="triggers">Optional <see cref="TriggerManager"/>. When
    /// supplied, the ETB + LTB triggered abilities are registered so their
    /// respective <see cref="CardMovedEvent"/>s land them on the stack
    /// automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayers,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Default snapshot resolver — single-player fallback so shape tests
        // don't need to wire a game aggregate. Production callers pass a
        // game.Players-backed resolver so cross-player battlefields are
        // visible to the destroy scan.
        Func<IReadOnlyList<Player>> playersFn = allPlayers ?? (() => new[] { owner });

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Golem });

        // CR 301.1 / 302.1 — Artifact Creature shares both card types.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — "When Sundering Titan enters, choose one
        // land of each basic land type, then destroy those lands."
        // CR 603.6a + CR 701.7.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: destroy one land of each basic land type (ETB)",
            () => DestroyOneLandOfEachBasicType(playersFn()));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // LTB triggered ability — "When Sundering Titan leaves the
        // battlefield, choose one land of each basic land type, then
        // destroy those lands." CR 603.6c / CR 603.10c.
        //
        // No Triggers.OnLeaveBattlefieldSelf helper exists; matches
        // Skyclave Apparition's inline EventTriggerCondition shape.
        // ActiveZones=Battlefield uses the "looks back" semantics — CR
        // 603.6d resolves the LTB against the permanent as it last existed
        // on the battlefield.
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{CardName}: destroy one land of each basic land type (LTB)",
            () => DestroyOneLandOfEachBasicType(playersFn()));

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return card;
    }

    /// <summary>
    /// Walk the five basic land types in order; for each, find the first
    /// land carrying that subtype on any player's battlefield and route it
    /// through <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/> as a
    /// destroy effect (CR 701.7). Indestructible (CR 702.12) and
    /// regeneration (CR 701.15) gates apply via the primitive.
    ///
    /// v1 deterministic picker — first match found per type. A land that
    /// has just been destroyed (no longer on the battlefield) is skipped
    /// on subsequent passes; this handles the dual-land case (Tundra =
    /// Plains+Island) naturally — the second pass for the second type
    /// will skip the freshly-destroyed Tundra and look for a different
    /// match.
    /// </summary>
    private static void DestroyOneLandOfEachBasicType(IReadOnlyList<Player> players)
    {
        if (players == null || players.Count == 0) return;

        foreach (var basicType in BasicLandTypes)
        {
            ICard? pick = null;
            foreach (var p in players)
            {
                foreach (var c in p.Zones.Battlefield.GetCards())
                {
                    if (!c.HasType(CardType.Land)) continue;
                    if (!c.HasSubtype(basicType)) continue;
                    pick = c;
                    break;
                }
                if (pick != null) break;
            }

            if (pick == null) continue;

            // CR 608.2b — re-check at the moment of destroy. The earlier
            // iteration's destroy may have already changed the board (rare
            // — a Mountain destroy can't displace a Plains pick — but the
            // dual-land case is real).
            if (pick.Zone != ZoneType.Battlefield) continue;

            Fx.MoveToGraveyard(pick, ZoneMoveReason.Destroy);
        }
    }
}
