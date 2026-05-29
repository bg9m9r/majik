using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Writhing Chrysalis (Battle for Zendikar, {2}{R}{G}).
///
/// Creature — Eldrazi Drone 2/3. Oracle text (Scryfall, verified):
///   "Devoid (This card has no color.)
///    When you cast this spell, create two 0/1 colorless Eldrazi Spawn
///    creature tokens with \"Sacrifice this token: Add {C}.\"
///    Reach
///    Whenever you sacrifice another Eldrazi, put a +1/+1 counter on this
///    creature."
///
/// ## Implemented (v1)
/// - 2/3 Creature — Eldrazi Drone at {2}{R}{G}; owner / controller wired.
/// - <b>Devoid (CR 702.114)</b> — stamps <see cref="Card.SetDevoid"/> so
///   <see cref="Majik.Core.Cards.CardColors"/> reports colourless despite the
///   {R}{G} pips, plus a <see cref="KeywordAbility"/> marker (same posture as
///   <see cref="SowingMycospawnFactory"/>).
/// - <b>Reach (CR 702.17)</b> — attached as a <see cref="KeywordAbility"/>
///   marker (same shape as <see cref="CanopySpiderFactory"/> / World Breaker).
/// - <b>Cast trigger (CR 603.6a / CR 603.6b)</b> — "When you cast this spell,
///   create two 0/1 colorless Eldrazi Spawn..." A <see cref="TriggeredAbility"/>
///   over a self-cast <see cref="SpellCastEvent"/>, active on the Stack (same
///   posture as <see cref="SowingMycospawnFactory"/>'s cast triggers). The
///   resolution body creates two Eldrazi Spawn tokens via the shared
///   <see cref="TokenFactory.CreateEldraziSpawn"/> helper (0/1 colourless with
///   the deferred "Sacrifice this token: Add {C}." mana ability).
/// - <b>Sacrifice trigger (CR 603.1)</b> — "Whenever you sacrifice another
///   Eldrazi, put a +1/+1 counter on this creature." A
///   <see cref="TriggeredAbility"/> over a <see cref="CardMovedEvent"/>
///   filtered to (another Eldrazi, Battlefield -> Graveyard); resolution
///   places one +1/+1 counter (CR 122) on Writhing Chrysalis via
///   <see cref="Fx.PlaceCounter"/>. Same sacrifice-detection posture as
///   <see cref="MayhemDevilFactory"/>.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only; triggers attached but not
///   registered with any <see cref="TriggerManager"/>. Suitable for shape /
///   dispatcher tests.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully
///   wired; the triggers register so the bus drives them. The Spawn tokens'
///   ETB routes through <paramref name="zones"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>"Sacrifice this token: Add {C}." cost</b> on the Eldrazi Spawn
///   tokens: <see cref="ManaAbility"/> doesn't carry a sac cost yet (same gap
///   as Eldrazi Skyspawner's Scion / Treasure / Food). The Spawn produces
///   {C} without enforcing the sacrifice — see
///   <see cref="TokenFactory.CreateEldraziSpawn"/>.
/// - <b>Sacrifice-only firing semantics</b>: the engine doesn't yet
///   distinguish "sacrificed" from "destroyed" / "died from SBA" at the
///   <see cref="CardMovedEvent"/> level (it carries only zones, no
///   <see cref="ZoneMoveReason"/>). The v1 condition fires on ANY Eldrazi
///   moving Battlefield -> Graveyard — the same over/under-fire footprint
///   documented on <see cref="MayhemDevilFactory"/>. A dedicated
///   <c>PermanentSacrificedEvent</c> would close it with no change to this
///   factory beyond swapping the condition type.
/// </summary>
[CardName("Writhing Chrysalis")]
public static class WrithingChrysalisFactory
{
    public const string CardName = "Writhing Chrysalis";
    public const string PrintedManaCost = "{2}{R}{G}";
    public const int Power = 2;
    public const int Toughness = 3;
    public const int SpawnCount = 2;

    private const string DevoidKeyword = "Devoid";
    private const string ReachKeyword = "Reach";

    /// <summary>
    /// Construct Writhing Chrysalis with no live wiring. Triggers are
    /// attached for shape observability; not registered with any
    /// <see cref="TriggerManager"/>, no <see cref="ZoneService"/> wiring.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Writhing Chrysalis with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the Eldrazi Spawn tokens' ETB
    /// routes through <see cref="ZoneService.MoveCardTo"/> so
    /// <see cref="CardMovedEvent"/> publishes for zone-change subscribers.</param>
    /// <param name="triggers">When supplied, both triggers register with the
    /// bus so the corresponding events land them on the stack automatically
    /// (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Eldrazi, CardSubtype.Drone });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.114 — Devoid. Stamp IsDevoid so CardColors.GetColors returns
        // empty despite the {R}{G} pips, plus a keyword marker for scans.
        card.SetDevoid(true);
        card.AddAbility(new KeywordAbility(DevoidKeyword, card, owner));

        // CR 702.17 — Reach. Keyword marker only (combat-block validator
        // reads the keyword off the card's abilities).
        card.AddAbility(new KeywordAbility(ReachKeyword, card, owner));

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.6a / CR 603.6b.
        //   "When you cast this spell, create two 0/1 colorless Eldrazi
        //    Spawn creature tokens with \"Sacrifice this token: Add {C}.\""
        // Fires while Writhing Chrysalis is on the stack (SpellCastEvent is
        // published as the spell moves to the stack), so activeZones = Stack
        // (same posture as Sowing Mycospawn / Devourer of Destiny).
        // No targets — pure token creation.
        // ----------------------------------------------------------------
        var castCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) => ReferenceEquals(e.Spell.Card, card));

        var castEffect = new Effect(
            $"{CardName}: create two 0/1 colourless Eldrazi Spawn creature tokens with \"Sacrifice this token: Add {{C}}.\"",
            () =>
            {
                var controller = card.Controller ?? owner;
                for (var i = 0; i < SpawnCount; i++)
                {
                    TokenFactory.CreateEldraziSpawn(controller, zones);
                }
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { castEffect },
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        // ----------------------------------------------------------------
        // Sacrifice trigger — CR 603.1.
        //   "Whenever you sacrifice another Eldrazi, put a +1/+1 counter on
        //    this creature."
        // v1 condition: another Eldrazi moving Battlefield -> Graveyard
        // (see class xmldoc gap note re: sacrifice-vs-death detection, same
        // footprint as Mayhem Devil). "another" excludes Writhing Chrysalis
        // itself (CR 603.2 — the source does not count as "another").
        // ----------------------------------------------------------------
        var sacCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            if (ReferenceEquals(e.Card, card)) return false; // "another"
            return e.Card.HasSubtype(CardSubtype.Eldrazi);
        });

        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on this creature",
            () => Fx.PlaceCounter(card, CounterType.PlusOnePlusOne, 1));

        var sacTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: sacCondition,
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(sacTrigger);
        triggers?.RegisterTriggeredAbility(sacTrigger);

        return card;
    }
}
