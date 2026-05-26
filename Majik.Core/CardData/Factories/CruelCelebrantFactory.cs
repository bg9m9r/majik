using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cruel Celebrant (War of the Spark, {W}{B}).
///
/// Creature — Vampire 1/2. Oracle text (Scryfall, verified):
///   "Whenever Cruel Celebrant or another creature or planeswalker you
///    control dies, each opponent loses 1 life and you gain 1 life."
///
/// ## Implemented (v1)
/// - 1/2 Creature — Vampire {W}{B}; owner / controller wired.
/// - <b>Aristocrat death trigger (CR 603.1 + CR 700.4)</b>: a single
///   <see cref="TriggeredAbility"/> fires on
///   <see cref="CardMovedEvent"/> with FromZone = Battlefield + ToZone =
///   Graveyard where the moved card is controlled by the Celebrant's
///   controller AND is either a <see cref="CardType.Creature"/> or a
///   <see cref="CardType.Planeswalker"/>. The wording "Cruel Celebrant or
///   another creature or planeswalker you control" collapses to "a
///   creature or planeswalker you control" because the Celebrant itself
///   is a creature you control — there is no third-trigger nuance. CR
///   603.10 — controller is read off the moved card (engine keeps
///   <see cref="Permanent.Controller"/> across the zone move, so the
///   LKI snapshot is effectively the live read; same shape as The
///   Meathook Massacre's dies-triggers).
/// - <b>Drain effect</b>: on resolution each opponent loses 1 life and
///   the controller gains 1 life. Opponents are enumerated via the
///   optional <paramref name="opponentResolver"/> (mirrors The Meathook
///   Massacre's resolver shape — single-arg <c>Create(owner)</c>
///   silently no-ops the opponent-drain side, the lifegain side always
///   fires).
///
/// ## Deferred (v1 gaps)
/// - <b>Last-known-information for the dying permanent</b>: CR 603.10 —
///   the moved card's controller must be read from LKI at the moment of
///   death. The engine currently keeps <see cref="Permanent.Controller"/>
///   on the card after the zone move, so this v1 implementation reads
///   it directly. A future LKI snapshot pass would replace the
///   controller read with a captured value. Same shape as The Meathook
///   Massacre's own-dies trigger.
/// - <b>Lifelink semantics</b>: the printed text does NOT use lifelink
///   — the lifegain and lifeloss are separate effects on the same
///   trigger (CR 119.3 — each is a discrete life-change event). This
///   matters for lifegain-payoff triggers (Heliod, Sun-Crowned) and
///   life-loss-matters effects (Sanguine Bond / Vito).
/// </summary>
[CardName("Cruel Celebrant")]
public static class CruelCelebrantFactory
{
    public const string CardName = "Cruel Celebrant";
    public const string PrintedManaCost = "{W}{B}";
    public const int Power = 1;
    public const int Toughness = 2;
    public const int DrainAmount = 1;
    public const int GainAmount = 1;

    /// <summary>
    /// Construct Cruel Celebrant with no live runtime services. The
    /// death-trigger is attached to the card shape but not registered
    /// with a <see cref="TriggerManager"/>, and no opponent resolver is
    /// wired (so the opponent-drain side is a no-op while the lifegain
    /// side still fires). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, opponentResolver: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Cruel Celebrant with optional runtime services.
    /// <paramref name="opponentResolver"/> supplies the player list the
    /// death-trigger drains 1 life from (typically every
    /// <c>Game.Players</c> entry that isn't the controller).
    /// <paramref name="triggers"/> registers the triggered ability so
    /// the bus drives it automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Vampire });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Death trigger — CR 603.1 + CR 700.4.
        //   "Whenever Cruel Celebrant or another creature or planeswalker
        //    you control dies, each opponent loses 1 life and you gain 1
        //    life."
        // The "or another" wording reads as a single trigger over the
        // union {self, your other creatures, your planeswalkers}. Since
        // the Celebrant itself is a creature-you-control, the union
        // collapses to "a creature or planeswalker you control" — one
        // predicate, one trigger.
        // ----------------------------------------------------------------
        var diesCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            if (!e.Card.HasType(CardType.Creature)
                && !e.Card.HasType(CardType.Planeswalker)) return false;
            return ReferenceEquals(e.Card.Controller, owner);
        });

        var drainEffect = new Effect(
            $"{CardName}: each opponent loses 1 life + controller gains 1 life",
            () =>
            {
                var opponents = opponentResolver?.Invoke();
                if (opponents != null)
                {
                    foreach (var opp in opponents)
                    {
                        if (ReferenceEquals(opp, owner)) continue;
                        opp.LoseLife(DrainAmount);
                    }
                }
                owner.GainLife(GainAmount);
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: diesCondition,
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
