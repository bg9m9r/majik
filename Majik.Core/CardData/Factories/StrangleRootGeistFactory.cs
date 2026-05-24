using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Strangleroot Geist (Dark Ascension, {G}{G}).
///
/// Creature — Spirit 2/1. Oracle text:
///   "Haste.
///    Undying (When this creature dies, if it had no +1/+1 counters on
///    it, return it to the battlefield under its owner's control with a
///    +1/+1 counter on it.)"
///
/// ## Implemented (v1)
/// - 2/1 Creature — Spirit at <c>{G}{G}</c>, owner/controller wired.
/// - <b>Haste</b> (CR 702.10) wired as a <see cref="KeywordAbility"/>
///   marker; <c>CombatAbilities.HasHaste</c> reads it. Same shape as
///   Earthshaker Khenra / Goblin Chieftain / Mantis Rider.
/// - <b>Undying</b> (CR 702.93) wired via <see cref="UndyingFactory.Build"/>
///   (the canonical helper used by every Undying creature — same path as
///   Young Wolf in <see cref="Majik.Core.Tests.Keywords.UndyingTests"/>).
///   The trigger fires on <see cref="Majik.Core.Events.CardMovedEvent"/>
///   Battlefield → Graveyard for this card; on resolution it returns the
///   creature to its owner's battlefield, clears the counter bag
///   (CR 121.2 — counters do not persist across zone changes), and adds
///   exactly one <see cref="Majik.Core.Counters.CounterType.PlusOnePlusOne"/>
///   counter. <see cref="TriggeredAbility.InterveningIf"/> (CR 603.4)
///   re-checks "if it had no +1/+1 counters on it" at stack-entry, so the
///   second death after an Undying return stays dead.
/// - Also adds an <c>"Undying"</c> <see cref="KeywordAbility"/> marker
///   alongside the live <see cref="UndyingFactory"/> trigger so dispatcher
///   / shape tests can observe the keyword on the card without inspecting
///   the trigger predicate (mirrors how Reach / Flash / Haste are surfaced
///   structurally — Persist on Kitchen Finks omits a parallel marker
///   because that factory rolls its own trigger; here we follow the
///   keyword-marker convention used by all printed-Undying cards routed
///   through <see cref="Majik.Core.CardData.KeywordBinder"/>).
///
/// ## Lifecycle
/// The single-arg <see cref="Create(Player)"/> path attaches both keyword
/// markers and the Undying trigger to the card shape for dispatcher /
/// shape tests without <see cref="TriggerManager"/> registration. Use the
/// <see cref="Create(Player, TriggerManager?)"/> overload for fully-wired
/// bus-driven behaviour — the Undying trigger is registered so a death
/// CardMovedEvent automatically queues the ability.
///
/// ## Comes-back-with-Haste interaction
/// CR 702.10 + CR 702.93 — on an Undying return the creature re-enters
/// the battlefield as the same object with both keyword markers intact, so
/// <c>CombatAbilities.HasHaste</c> still reads "true" and
/// <see cref="Majik.Core.Combat.CombatValidator.CanAttack"/> allows it to
/// attack the same turn it returns (the Haste keyword bypasses the
/// summoning-sickness check at CR 302.1). <see cref="UndyingFactory"/>
/// calls <see cref="Permanent.MarkEnteredBattlefield"/> on return but does
/// not reset summoning sickness — Haste makes that moot.
/// </summary>
public static class StrangleRootGeistFactory
{
    public const string CardName = "Strangleroot Geist";
    public const string PrintedManaCost = "{G}{G}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Strangleroot Geist with no live <see cref="TriggerManager"/>
    /// wiring. Both keyword markers (Haste, Undying) and the Undying
    /// triggered ability are attached to the card shape; the trigger is
    /// not yet registered for bus-driven firing.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Strangleroot Geist with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the Undying
    /// trigger is registered so a qualifying
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> (Battlefield →
    /// Graveyard, source matches this card) automatically queues the
    /// ability on death.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spirit });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste. KeywordAbility marker; CombatAbilities.HasHaste
        // reads it. Same shape as Earthshaker Khenra / Goblin Chieftain.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // CR 702.93 — Undying. Marker keyword surfaced for shape /
        // dispatcher tests (parallels Haste / Reach / Flash markers).
        card.AddAbility(new KeywordAbility("Undying", card, owner));

        // CR 702.93b — Undying triggered ability. UndyingFactory.Build is
        // the canonical helper shared by every printed-Undying creature
        // (Young Wolf, Geralf's Messenger, Butcher Ghoul, etc.) — same
        // CardMovedEvent Battlefield→Graveyard condition, same
        // counters-zero interveningIf at stack-entry, same return-with-
        // +1/+1 counter resolution body.
        var undyingTrigger = UndyingFactory.Build(card);
        card.AddAbility(undyingTrigger);

        // Optional bus-driven wiring — when a TriggerManager is supplied
        // the trigger fires on the next qualifying CardMovedEvent. Without
        // it the trigger is still attached structurally for shape tests.
        triggers?.RegisterTriggeredAbility(undyingTrigger);

        return card;
    }
}
