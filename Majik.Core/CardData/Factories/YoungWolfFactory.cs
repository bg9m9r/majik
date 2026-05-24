using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Young Wolf (Innistrad, {G}).
///
/// Creature — Wolf 1/1. Oracle text:
///   "Undying. (When this creature dies, if it had no +1/+1 counters on it,
///    return it to the battlefield under its owner's control with a +1/+1
///    counter on it.)"
///
/// ## Implemented (v1)
/// - 1/1 Creature — Wolf, mana cost {G}.
/// - <b>Undying keyword marker</b> (CR 702.93) — a <see cref="KeywordAbility"/>
///   so data-side tooling sees the keyword on the card.
/// - <b>Undying mechanic</b> (CR 702.93b) — wired via
///   <see cref="UndyingFactory.Build"/>: triggers on a Battlefield → Graveyard
///   <see cref="CardMovedEvent"/>, with an intervening-if (CR 603.4) that
///   checks the creature had no +1/+1 counters at death. On resolve it
///   raw-moves Young Wolf graveyard → battlefield, clears the counter bag
///   (CR 121.2), and adds exactly one +1/+1 counter. Active zones include
///   Graveyard so the trigger evaluates after ZoneService stamps Zone =
///   Graveyard before publishing.
///
/// ## Wiring
/// - <see cref="Create(Player)"/> attaches the keyword marker + Undying
///   triggered ability to the card shape without bus-driven registration.
///   Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, TriggerManager)"/> additionally registers
///   the Undying trigger with the live <see cref="TriggerManager"/> so a
///   Battlefield → Graveyard <see cref="CardMovedEvent"/> places it on the
///   stack automatically (mirrors NihilSpellbombFactory's two-arg pattern).
///
/// ## Comparison with Kitchen Finks (Persist, CR 702.78)
/// Persist is the mirror of Undying — returns on death without the
/// corresponding counter type and adds one of that counter. Undying uses
/// +1/+1 counters and fires when the creature has zero +1/+1 counters.
/// Young Wolf is the canonical 1-drop Undying creature; Kitchen Finks
/// inlines its Persist mechanic, but Young Wolf delegates to the shared
/// <see cref="UndyingFactory"/> helper (CR 702.93).
/// </summary>
[CardName("Young Wolf")]
public static class YoungWolfFactory
{
    public const string CardName = "Young Wolf";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Young Wolf with the Undying triggered ability attached to
    /// the card shape but not registered with a <see cref="TriggerManager"/>.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Young Wolf with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the Undying
    /// trigger is registered so a Battlefield → Graveyard
    /// <see cref="CardMovedEvent"/> places it on the stack automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Wolf });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.93 — Undying. KeywordAbility marker so data-side tooling
        // (KeywordRegistry / OracleTextParser) sees it on the card; the
        // gameplay mechanic itself is wired by UndyingFactory.Build below.
        card.AddAbility(new KeywordAbility("Undying", card, owner));

        // CR 702.93b — Undying triggered ability. Returns Young Wolf to the
        // battlefield with a +1/+1 counter when it dies without one.
        var undying = UndyingFactory.Build(card);
        card.AddAbility(undying);
        triggers?.RegisterTriggeredAbility(undying);

        return card;
    }
}
