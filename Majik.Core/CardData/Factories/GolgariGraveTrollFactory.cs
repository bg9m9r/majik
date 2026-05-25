using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Golgari Grave-Troll (Ravnica: City of Guilds, {3}{B}{G}).
///
/// Creature — Zombie Troll 0/0. Oracle text:
///   "Golgari Grave-Troll enters with a +1/+1 counter on it for each
///    creature card in your graveyard.
///    Dredge 6"
///
/// ## Implemented (v1)
/// - 0/0 Creature — Zombie Troll, mana cost {3}{B}{G}.
/// - ETB trigger (CR 603.6a / CR 614.1d) wired over
///   <see cref="CardMovedEvent"/> Battlefield ← (other) for this card.
///   Resolve body counts Creature cards in controller's graveyard at
///   ETB time and adds that many +1/+1 counters to the card. Modeled as
///   a trigger (not a true replacement) because the count depends on
///   the resolve-time graveyard state and matches the same shape used
///   by Murktide Regent's delve-counter retrofit
///   (<see cref="MurktideRegentFactory"/>). Counter placement routes
///   through <see cref="Permanent.Counters"/> directly — same posture
///   as Murktide's resolve body. SBAs after the trigger resolves clean
///   up the 0/0 state if no creatures were in graveyard at ETB
///   (CR 704.5f — toughness ≤ 0 dies as an SBA).
/// - <b>Dredge 6</b> (CR 702.52) via <see cref="DredgeFactory.Build"/>.
///   Marker keyword + graveyard-anchored draw replacement registered
///   when a <see cref="ReplacementBus"/> is supplied.
///
/// ## v1 gaps
/// - The ETB-counters clause is technically an "enters with" replacement
///   (CR 614.1d) — modeling it as a trigger means the card briefly hits
///   the battlefield at 0/0 before the trigger resolves. SBA timing
///   doesn't run between ETB and trigger resolution (CR 116.5 — SBAs
///   only check when a player would get priority), so 0 counters → dies
///   to SBA after the trigger resolves with no counters added; that
///   matches the printed behavior. The <see cref="EntersWithCountersReplacement"/>
///   shape doesn't support variable counts off graveyard state in v1,
///   so the trigger model is the canonical v1 path (same retrofit
///   posture as Murktide Regent).
/// </summary>
[CardName("Golgari Grave-Troll")]
public static class GolgariGraveTrollFactory
{
    public const string CardName = "Golgari Grave-Troll";
    public const string PrintedManaCost = "{3}{B}{G}";
    public const int BasePower = 0;
    public const int BaseToughness = 0;
    public const int DredgeValue = 6;

    /// <summary>
    /// Construct Golgari Grave-Troll with no runtime wiring. Card
    /// identity + ability shape only.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Golgari Grave-Troll with optional runtime wiring. When
    /// <paramref name="triggers"/> is supplied the ETB-with-counters
    /// trigger is registered; when <paramref name="replacements"/> is
    /// supplied the Dredge 6 draw replacement is registered.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: BasePower,
            toughness: BaseToughness,
            subtypes: new[] { CardSubtype.Zombie, CardSubtype.Troll });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a / CR 614.1d.
        //   "Golgari Grave-Troll enters with a +1/+1 counter on it for
        //    each creature card in your graveyard."
        // Modelled as a trigger that counts Creature cards in the
        // controller's graveyard at resolution time and adds the matching
        // number of +1/+1 counters. Mirrors Murktide Regent's resolve-time
        // counter calculation.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: enters with a +1/+1 counter for each Creature card in your graveyard",
            () =>
            {
                var controller = card.Controller ?? owner;
                var creatureCount = controller.Zones.Graveyard.GetCards()
                    .Count(c => c.HasType(CardType.Creature));
                if (creatureCount <= 0) return;
                card.Counters.Add(CounterType.PlusOnePlusOne, creatureCount);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>(
                (e, _) => ReferenceEquals(e.Card, card)
                          && e.ToZone == ZoneType.Battlefield
                          && e.FromZone != ZoneType.Battlefield),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // CR 702.52 — Dredge 6. Keyword marker + graveyard-anchored draw
        // replacement (gated on Library.Count >= 6 + agent yes/no).
        DredgeFactory.Build(card, DredgeValue, replacements);

        return card;
    }
}
