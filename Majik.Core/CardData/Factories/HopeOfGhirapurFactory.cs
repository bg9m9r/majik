using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hope of Ghirapur (Aether Revolt, {0}).
///
/// Legendary Artifact Creature — Thopter 1/1. Oracle text:
///   "Flying"
///   "Sacrifice Hope of Ghirapur: Until your next turn, target player
///    who was dealt combat damage by Hope of Ghirapur this turn can't
///    cast noncreature spells."
///
/// A 0-mana evasive flyer that, after connecting once in combat,
/// locks the player it hit out of noncreature spells for a full
/// rotation. Modern Cheerios / Affinity / Hardened Scales splash for the
/// free artifact body + the tempo bullet.
///
/// ## Implementation
///
/// - <b>Identity</b>: Legendary supertype, Artifact + Creature card
///   types, Thopter subtype, 1/1, mana cost {0}. Same convention as
///   <see cref="MoxOpalFactory"/> / <see cref="MemniteFactory"/> for the
///   literal {0} string.
/// - <b>Flying</b> (CR 702.9) surfaced as
///   <see cref="KeywordAbility"/>("Flying"); combat code reads the marker
///   via <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/>.
/// - <b>Per-turn damage-recipient tracking</b>: when an event bus is
///   supplied the factory subscribes to
///   <see cref="CombatDamageDealtEvent"/> and stamps the targeted
///   player's id into <see cref="_damagedThisTurn"/> whenever Hope deals
///   combat damage to that player (CR 510). A
///   <see cref="TurnStartedEvent"/> handler clears the set at the start
///   of every turn (printed scope: "this turn"). The set is keyed on
///   <see cref="Player.Id"/> so it survives Hope leaving the battlefield
///   (the activated ability sacrifices Hope before the restriction
///   registers — the closure consults the set, not Hope's live state).
/// - <b>Activated ability (CR 602 / 601.3)</b>:
///     - Cost: <see cref="AdditionalCost.Sacrifice"/>(self) — no mana
///       pip. Resolution body performs the battlefield → graveyard
///       mutation (matching the
///       <see cref="RangerCaptainOfEosFactory"/> / Glen Elendra Archmage
///       posture — the <c>AdditionalCost.Sacrifice</c> <c>Pay</c> stub
///       is a no-op today).
///     - 1..1 <see cref="TargetRequest"/> for "target player who was
///       dealt combat damage by Hope of Ghirapur this turn"
///       (CR 115.3 — target validity at choose-time + on-resolution).
///       Legal candidates are gathered live from
///       <see cref="_damagedThisTurn"/> (deferred — v1 leaves
///       <c>LegalCandidates</c> empty and lets the agent supply a chosen
///       target; resolution guards re-check membership).
///     - On resolve: register a noncreature-spell restriction against
///       the chosen player via
///       <see cref="CastingRestrictions.AddNoncreatureSpellRestrictionForTurn"/>.
///       The restriction is cleared at the START of the controller's
///       next turn (CR 514.2 — "until your next turn") via a long-lived
///       <see cref="TurnStartedEvent"/> handler. Same registry the
///       Ranger-Captain rider uses; one shared turn-scoped slot.
/// - <b>"Until your next turn" scope</b>: distinct from Ranger-Captain's
///   "this turn" — Hope's restriction must survive across the opponent's
///   turn back to the controller's next turn. v1 implements this by
///   deferring the clear until the next <see cref="TurnStartedEvent"/>
///   whose <c>Player</c> matches Hope's controller (CR 514.2). Same
///   shared <see cref="CastingRestrictions"/> backing store as
///   Ranger-Captain; the clear is delegate-driven so the slot survives
///   intermediate opponent turns.
///
/// ## Lifecycle
///
/// - <see cref="Create(Player)"/> — shape only. Activated ability is
///   attached; no event subscriptions. Suitable for factory-shape /
///   dispatcher tests.
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired. The damage
///   tracker subscribes to <see cref="CombatDamageDealtEvent"/>; the
///   turn-scoped restriction is cleared on
///   <see cref="TurnStartedEvent"/> when the active player is the
///   controller.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Pre-filter "dealt damage by Hope" at target selection</b>: the
///   <see cref="TargetRequest"/>'s <c>LegalCandidates</c> is empty;
///   agents enumerate the live damaged-set themselves. Resolution
///   re-checks membership, so an illegal chosen target collapses to a
///   no-op (CR 608.2b).
/// - <b>Multi-Hope shared registry</b>: the damage tracker is per-card
///   instance; multiple Hopes track their own damaged-players sets and
///   the restriction slot is shared via <see cref="CastingRestrictions"/>
///   (each Hope's activation contributes to the same per-player set —
///   correct behaviour per CR 601.3 — multiple-source restrictions stack
///   via Add).
/// </summary>
[CardName("Hope of Ghirapur")]
public static class HopeOfGhirapurFactory
{
    public const string CardName = "Hope of Ghirapur";
    public const string PrintedManaCost = "{0}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Hope of Ghirapur with no live event-bus wiring (the
    /// shape / dispatcher path). The activated ability is attached but
    /// the damage-recipient tracker and turn-start clear handler are
    /// NOT subscribed; the activated ability's resolution body still
    /// performs the sacrifice + restriction registration, but the
    /// damaged-players set will be empty so the restriction is a no-op
    /// in practice (agents supplying a target see the resolution-time
    /// membership re-check fail). Suitable for factory-shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Hope of Ghirapur. When <paramref name="eventBus"/> is
    /// supplied, a long-lived <see cref="CombatDamageDealtEvent"/>
    /// handler tracks which players Hope has dealt combat damage to
    /// this turn, and a <see cref="TurnStartedEvent"/> handler clears
    /// both the per-turn damage set (start of any turn) AND the
    /// noncreature-spell restriction registry (start of the
    /// controller's next turn — CR 514.2).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Thopter });

        // CR 301.1 / 302.1 — Hope of Ghirapur is an Artifact Creature.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying keyword marker. Combat code reads the marker
        // via Majik.Core.Combat.CombatAbilities.HasFlying.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Per-instance "players Hope has dealt combat damage to this
        // turn" tracker. CR 510 + printed "this turn" scope. The set is
        // keyed on Player.Id so it survives Hope's sacrifice (the
        // activated ability consults this set AFTER moving Hope to the
        // graveyard).
        // ----------------------------------------------------------------
        var damagedThisTurn = new HashSet<Guid>();

        if (eventBus != null)
        {
            eventBus.Subscribe<CombatDamageDealtEvent>(e =>
            {
                if (e.TargetPlayer == null) return;
                if (!ReferenceEquals(e.Source, card)) return;
                damagedThisTurn.Add(e.TargetPlayer.Id);
            });

            // CR 514.x — "this turn" set is per-turn. Clear at the start
            // of every turn so the next combat round starts fresh.
            eventBus.Subscribe<TurnStartedEvent>(_ => damagedThisTurn.Clear());

            // CR 514.2 — "until your next turn" restriction clear. The
            // restriction is cleared at the start of the controller's
            // next turn; intermediate opponent turns leave it intact.
            eventBus.Subscribe<TurnStartedEvent>(e =>
            {
                if (!ReferenceEquals(e.Player, owner)) return;
                CastingRestrictions.ClearNoncreatureRestrictionForTurn();
            });
        }

        // ----------------------------------------------------------------
        // Activated ability — CR 602 / 601.3.
        //   "Sacrifice Hope of Ghirapur: Until your next turn, target
        //    player who was dealt combat damage by Hope of Ghirapur this
        //    turn can't cast noncreature spells."
        // ----------------------------------------------------------------
        ActivatedAbility? sacAbility = null;

        var sacEffect = new Effect(
            $"{CardName}: sacrifice self, target damaged player can't cast noncreature spells until your next turn",
            () =>
            {
                // ---- Sacrifice self (CR 701.16) ----
                if (card.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(card);
                    var sacOwner = card.Owner ?? owner;
                    sacOwner.Zones.Graveyard.AddCard(card);
                    card.SetZone(ZoneType.Graveyard);
                }

                // ---- Register the per-player noncreature restriction ----
                if (sacAbility == null) return;
                var slots = sacAbility.ChosenTargets;
                if (slots.Count == 0 || slots[0].Count == 0) return;
                if (slots[0][0] is not Player chosen) return;

                // CR 608.2b — illegal-on-resolution. Membership re-check
                // against the per-instance damaged-this-turn set. v1
                // gracefully no-ops when the bus was never wired (the
                // set is empty so the membership test fails).
                if (!damagedThisTurn.Contains(chosen.Id)) return;

                CastingRestrictions.AddNoncreatureSpellRestrictionForTurn(chosen);
            });

        sacAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Sacrifice(card) },
            effects: new IEffect[] { sacEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player who was dealt combat damage by Hope of Ghirapur this turn",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(sacAbility);

        return card;
    }
}
