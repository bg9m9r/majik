using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Unstoppable Slasher (Duskmourn, {2}{B}).
///
/// Creature — Zombie Assassin 2/3. Oracle text (Scryfall, verified 2026-06-23):
///   "Deathtouch
///    Whenever this creature deals combat damage to a player, they lose half
///    their life, rounded up.
///    When this creature dies, if it had no counters on it, return it to the
///    battlefield tapped under its owner's control with two stun counters on
///    it."
///
/// The base shape (name / Creature — Zombie Assassin / {2}{B} / 2/3) is
/// materialised from the embedded JSON definition
/// (<c>unstoppable-slasher.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Deathtouch keyword marker and
/// the two triggered abilities are layered on here (the NamedCardFactory path
/// does not run <see cref="Majik.Core.CardData.Parsing.KeywordBinder"/>, so
/// attach inline — same posture as <see cref="FloodpitsDrownerFactory"/> /
/// <see cref="NighthawkScavengerFactory"/>).
///
/// ## Implemented (v1)
/// - <b>2/3 Creature — Zombie Assassin, {2}{B}</b>, owner / controller wired.
/// - <b>Deathtouch (CR 702.2)</b> as a <see cref="KeywordAbility"/> marker
///   (<c>CombatAbilities.HasDeathtouch</c> reads it for the lethal-damage SBA /
///   combat-assignment rules).
/// - <b>Combat-damage-to-a-player trigger (CR 510 / CR 603.1)</b>: an
///   <see cref="EventTriggerCondition{TEvent}"/> over
///   <see cref="CombatDamageDealtEvent"/> gated on
///   (<see cref="CombatDamageDealtEvent.Source"/> == this creature) AND
///   (<see cref="DamageDealtEvent.TargetPlayer"/> != null). On resolution the
///   damaged player loses <c>ceil(currentLife / 2)</c> life via
///   <see cref="Player.LoseLife"/>. Mirrors <see cref="QuietusSpikeFactory"/>'s
///   payoff — half is computed against the LIVE life total at resolution
///   (CR 608.2b / CR 107.1).
/// - <b>Dies trigger (CR 700.4 / CR 603.6e)</b>: fired by a
///   <see cref="CardMovedEvent"/> from Battlefield to Graveyard whose card is
///   this creature. The intervening-if "if it had no counters on it"
///   (CR 603.4) re-checks that the creature's counter bag is empty when the
///   trigger would go on the stack — ANY counter type suppresses it (this is
///   the broader reading than Undying's +1/+1-only check). On resolution the
///   creature returns from the graveyard to the battlefield TAPPED under its
///   OWNER's control (CR 110.2 — "owner's control") with two stun counters on
///   it (CR 122.1 / CR 122.1g — the stun counters honour the untap-step
///   replacement in <c>TurnDriver.UntapStep</c>). This is an Undying-shaped
///   trigger (mirrors <see cref="Majik.Core.Keywords.UndyingFactory"/>) with
///   three differences: the counter check is over the whole bag, the return is
///   tapped, and the grant is two stun counters rather than one +1/+1 counter.
///
/// ## Notes
/// - Counters survive on the <see cref="Permanent.Counters"/> bag after the
///   creature leaves the battlefield (the engine does not clear it on move), so
///   the intervening-if accurately reflects the counters the creature had when
///   it died (same source-of-truth Undying relies on). On the return, the bag
///   is cleared first (CR 121.2 — counters leave with the permanent) before the
///   two stun counters are placed, so a second death after a return correctly
///   evaluates "no counters" only if those stun counters have since been
///   removed.
/// </summary>
[CardName("Unstoppable Slasher")]
public static class UnstoppableSlasherFactory
{
    public const string CardName = "Unstoppable Slasher";
    public const string Slug = "unstoppable-slasher";
    public const string PrintedManaCost = "{2}{B}";
    public const int StunCountersOnReturn = 2;

    private const string DeathtouchKeyword = "Deathtouch";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Unstoppable Slasher owned and controlled by
    /// <paramref name="owner"/>. The base shape is materialised from the
    /// embedded JSON definition; the Deathtouch marker and the two triggered
    /// abilities are layered on here. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.2 — Deathtouch marker. CombatAbilities.HasDeathtouch reads it.
        card.AddAbility(new KeywordAbility(DeathtouchKeyword, card, owner));

        // "Whenever this creature deals combat damage to a player, they lose
        //  half their life, rounded up." (CR 510 / CR 603.1).
        card.AddAbility(BuildCombatDamageTrigger(card, owner));

        // "When this creature dies, if it had no counters on it, return it to
        //  the battlefield tapped … with two stun counters on it." (CR 603.6e).
        card.AddAbility(BuildDiesTrigger(card, owner));

        return card;
    }

    // --- Combat damage to a player → lose half life, rounded up -------------

    private static TriggeredAbility BuildCombatDamageTrigger(Creature card, Player owner)
    {
        // Resolution reads the LIVE life total of the damaged player (CR 608.2b
        // — half is computed at resolution, not at trigger time). The triggering
        // player is captured by the predicate (the trigger has only this card as
        // the relevant source, and no chosen target, so the captured field is
        // the simplest faithful carrier — same shape as QuietusSpikeFactory).
        Player? lastDamaged = null;

        var damageEffect = new Effect(
            $"{CardName}: damaged player loses half their life, rounded up",
            () =>
            {
                var target = lastDamaged;
                if (target == null) return;

                // CR 107.1 / printed "half rounded up". Math.Ceiling over the
                // live LifeTotal gives the printed semantics for positive life.
                var amount = (int)Math.Ceiling(target.LifeTotal / 2.0);
                if (amount <= 0) return;
                target.LoseLife(amount);
            });

        return new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (e.TargetPlayer == null) return false;
                if (!ReferenceEquals(e.Source, card)) return false;

                lastDamaged = e.TargetPlayer;
                return true;
            }),
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield });
    }

    // --- Dies → if no counters, return tapped with two stun counters --------

    private static TriggeredAbility BuildDiesTrigger(Creature card, Player owner)
    {
        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            ReferenceEquals(e.Card, card)
            && e.FromZone == ZoneType.Battlefield
            && e.ToZone == ZoneType.Graveyard);

        var returnEffect = new Effect(
            $"{CardName} — return to battlefield tapped with two stun counters",
            () =>
            {
                // Guard: creature must still be in the graveyard (a replacement
                // effect could have moved it elsewhere).
                if (card.Zone != ZoneType.Graveyard) return;

                var cardOwner = card.Owner;
                if (cardOwner == null) return;

                // CR 110.2 — return under its OWNER's control.
                cardOwner.Zones.Graveyard.RemoveCard(card);
                cardOwner.Zones.Battlefield.AddCard(card);
                card.SetZone(ZoneType.Battlefield);
                card.SetController(cardOwner);

                // CR 121.2 — counters left the battlefield with the permanent;
                // clear the bag so the intervening-if of a SECOND death reads
                // an accurate count.
                card.Counters.Clear();

                // CR 122.1 / CR 122.1g — two stun counters on the returned
                // permanent. (Honoured by TurnDriver.UntapStep's untap
                // replacement.)
                card.Counters.Add(CounterType.Stun, StunCountersOnReturn);

                // "tapped" — CR 701.20a. Tap after re-entering the battlefield.
                Fx.Tap(card);

                // Permanent ETB bookkeeping (new object on the battlefield).
                card.MarkEnteredBattlefield();
            });

        // interveningIf: "if it had no counters on it" (CR 603.4). ANY counter
        // type suppresses the return — broader than Undying's +1/+1-only check.
        // Counters survive on the graveyard object, so this accurately reflects
        // the state at death.
        return new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { returnEffect },
            interveningIf: () => !card.Counters.HasAny,
            // {Battlefield, Graveyard} so SyncCardRegistration keeps the trigger
            // registered after the move and IsTriggered's zone-guard passes with
            // the creature in the graveyard (same posture as UndyingFactory).
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
    }
}
