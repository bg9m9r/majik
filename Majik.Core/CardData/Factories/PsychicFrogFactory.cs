using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Psychic Frog (Modern Horizons 3, {U}{B}).
///
/// Creature — Frog 1/2. Oracle text:
///   "Flying.
///    Whenever Psychic Frog deals combat damage to a player, draw that
///    many cards, then discard that many cards.
///    Discard a card: Put a +1/+1 counter on Psychic Frog."
///
/// ## Implemented (v1)
///
/// - <b>1/2 Creature — Frog at {U}{B}</b>. The Frog
///   <see cref="CardSubtype"/> entry registered alongside
///   the existing creature-subtype roster (CR 205.3m).
/// - <b>Flying</b> — wired as a <see cref="KeywordAbility"/> marker so
///   combat code (block-restriction at CR 509.1b) reads it the same way it
///   reads every other printed Flying creature (mirrors
///   <see cref="MurktideRegentFactory"/>).
/// - <b>Combat-damage-to-a-player "loot N" trigger (CR 510 / CR 603.1)</b>
///   — fires on a <see cref="CombatDamageDealtEvent"/> whose
///   <see cref="CombatDamageDealtEvent.Source"/> is Psychic Frog AND whose
///   <see cref="DamageDealtEvent.TargetPlayer"/> is non-null. The damage
///   amount is captured off the event in a closure shared with the effect
///   (CR 603.3 evaluates the trigger condition before the ability hits the
///   stack, so the captured count is fresh by the time the effect
///   resolves — mirrors the closure-capture pattern in
///   <see cref="SwordOfFeastAndFamineFactory"/> and
///   <see cref="RagavanNimblePilfererFactory"/>). On resolution the
///   controller draws <c>N</c> cards, then discards <c>N</c> cards (v1
///   deterministic first-card-in-hand pick for each discard — same v1
///   policy as <see cref="FaithlessLootingFactory"/> / Sword of Feast and
///   Famine). Empty-library halts the draw loop and stamps the loss
///   condition via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>
///   (CR 704.5b / 120.3); empty-hand halts the discard loop cleanly.
/// - <b>"Discard a card" activated ability — +1/+1 counter (CR 602)</b>
///   — wired via <see cref="DiscardACardCost"/> (any card from hand)
///   followed by an effect that places a <see cref="CounterType.PlusOnePlusOne"/>
///   counter on Psychic Frog. Repeatable so long as the controller has a
///   card to discard. No mana cost — discard is the sole activation
///   cost.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits service
/// wiring (no <see cref="TriggerManager"/> registration) and produces the
/// correct card shape for factory-shape / dispatch tests. The combat-
/// damage trigger is attached to the card but not registered with a
/// <see cref="TriggerManager"/>; callers may invoke the effect directly
/// in tests via <c>trigger.Effects[i].Execute()</c>, or use the full
/// overload for bus-driven firing.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Discard prompt</b> on the loot half and the activation cost
///   (CR 701.16a — discarding player chooses) — v1 deterministically
///   picks the first card in hand. Agent-driven prompts are deferred
///   behind the same queue as Liliana of the Veil + Faithless Looting +
///   Sword of Feast and Famine.
/// </summary>
[CardName("Psychic Frog")]
public static class PsychicFrogFactory
{
    public const string CardName = "Psychic Frog";
    public const string Cost = "{U}{B}";

    /// <summary>
    /// Constructs Psychic Frog with no live <see cref="TriggerManager"/>
    /// wiring. Combat-damage trigger + activated ability are attached for
    /// shape; the trigger is NOT registered. Suitable for factory-shape
    /// / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Constructs Psychic Frog. When <paramref name="triggers"/> is
    /// supplied, the combat-damage trigger is registered so a
    /// <see cref="CombatDamageDealtEvent"/> from Psychic Frog to a player
    /// automatically queues the ability. When <paramref name="replacements"/>
    /// is supplied, the discard-activated +1/+1 counter placement is routed
    /// through <see cref="CountersService.Add"/> so Hardened Scales / Doubling
    /// Season replacements can rewrite the count (CR 614).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: 1,
            toughness: 2,
            subtypes: new[] { CardSubtype.Frog });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.9 — Flying. KeywordAbility marker; combat code reads it.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Combat-damage-to-a-player "loot N" trigger — CR 510, CR 603.1.
        //   "Whenever Psychic Frog deals combat damage to a player, draw
        //    that many cards, then discard that many cards."
        // The damage amount is captured off the event in a closure shared
        // with the effect (CR 603.3). Mirrors the closure-capture shape
        // in SwordOfFeastAndFamineFactory + RagavanNimblePilfererFactory.
        // ----------------------------------------------------------------
        int capturedAmount = 0;

        var lootEffect = new Effect(
            $"{CardName}: draw N + discard N (N = combat damage dealt to player)",
            () =>
            {
                var n = capturedAmount;
                if (n <= 0) return;

                // 1) Draw N cards. Empty-library stops the loop and stamps
                //    the loss condition (CR 704.5b / 120.3).
                for (var i = 0; i < n; i++)
                {
                    var top = owner.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        owner.MarkTriedToDrawFromEmptyLibrary();
                        break;
                    }
                    owner.Zones.Library.RemoveCard(top);
                    owner.Zones.Hand.AddCard(top);
                }

                // 2) Discard N cards. Empty-hand halts the loop cleanly.
                //    v1 deterministic first-card-in-hand pick per discard
                //    (CR 701.16a — agent-driven choice deferred).
                for (var i = 0; i < n; i++)
                {
                    var pick = owner.Zones.Hand.GetCards().FirstOrDefault();
                    if (pick == null) break;
                    owner.Zones.Hand.RemoveCard(pick);
                    owner.Zones.Graveyard.AddCard(pick);
                }
            });

        var lootTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Source, card)) return false;
                if (e.TargetPlayer == null) return false;
                capturedAmount = e.Amount;
                return true;
            }),
            effects: new IEffect[] { lootEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(lootTrigger);
        triggers?.RegisterTriggeredAbility(lootTrigger);

        // ----------------------------------------------------------------
        // Activated ability — "Discard a card: Put a +1/+1 counter on
        // Psychic Frog." CR 602 — repeatable while the controller has a
        // card in hand. The DiscardACardCost is the sole activation cost
        // (no mana). +1/+1 counter via CounterCollection.Add (CR 122 /
        // 122.1c — no SBA gating, the counter is placed directly).
        // ----------------------------------------------------------------
        // RE-SOURCE-SAFE (agatha-bespoke-source-migration-creature-tail-batch):
        // the effect puts the +1/+1 counter on the live ResolutionContext.Source
        // (this ability's own source at resolution) rather than capturing `card`,
        // falling back to `card` only on the context-less legacy sync path
        // (ResolutionContext.Legacy, Source = null). Marked RebindSafe so Agatha's
        // Soul Cauldron re-homes this REAL "Discard a card: put a +1/+1 counter on
        // it" ability to a counter-bearing bearer via ActivatedAbility.RebindTo
        // (CR 707.2 / 613.1f): the counter lands on the BEARER, never the exiled
        // Psychic Frog. DiscardACardCost is a player-resource cost (no captured
        // source), passed through unchanged by RebindTo.
        var pumpEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on it",
            ctx =>
            {
                var subject = (ctx.Source as Permanent) ?? card;
                CountersService.Add(subject, CounterType.PlusOnePlusOne, 1, replacements);
                return ValueTask.CompletedTask;
            });

        var pumpAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new DiscardACardCost() },
            effects: new IEffect[] { pumpEffect },
            rebindSafe: true);

        card.AddAbility(pumpAbility);

        return card;
    }
}
