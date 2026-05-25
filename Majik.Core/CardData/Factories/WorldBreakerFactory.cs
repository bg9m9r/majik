using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for World Breaker (Battle for Zendikar, {5}{G}).
///
/// Creature — Eldrazi 5/5. Oracle text (Scryfall, verified):
///   "Reach
///    When this creature enters, exile target nonbasic land.
///    Whenever this creature attacks, exile target permanent that's one
///        or more colors.
///    {G}, Exile this card from your graveyard: Return this card to its
///        owner's hand."
///
/// ## Implemented (v1)
/// - 5/5 Creature — Eldrazi at {5}{G}.
/// - <b>Reach (CR 702.17)</b>: <see cref="KeywordAbility"/> marker —
///   <see cref="Majik.Core.Combat.CombatAbilities.HasReach"/> reads via
///   the same shape Endurance / Kraul Harpooner / Wrenn and Six use.
/// - <b>ETB trigger — "exile target nonbasic land" (CR 603.6a + CR
///   701.21)</b>: triggered ability over <see cref="CardMovedEvent"/>
///   filtered to self → battlefield (<see cref="Triggers.OnEnterBattlefieldSelf"/>).
///   1..1 "target nonbasic land" <see cref="TargetRequest"/> with a live
///   gatherer that walks every player's battlefield and yields any
///   <c>Land</c> that is NOT <see cref="CardSupertype.Basic"/>. On
///   resolution rechecks Land + !Basic + on-battlefield (CR 608.2b) and
///   exiles the target (Battlefield → Exile via
///   <see cref="ZoneService.MoveCard"/> when supplied; raw zone
///   manipulation otherwise — same Fulminator Mage / Wasteland posture).
/// - <b>Attack trigger — "exile target coloured permanent" (CR 508.1f +
///   CR 603.1 + CR 105 + CR 701.21)</b>: triggered ability over
///   <see cref="CreatureAttacksEvent"/> filtered via
///   <see cref="Triggers.OnAttackSelf"/>. 1..1 "target coloured
///   permanent" <see cref="TargetRequest"/> with a live gatherer that
///   yields permanents where
///   <see cref="CardColors.GetColors"/>.Count ≥ 1. Resolution rechecks
///   colour + on-battlefield (CR 608.2b) and exiles the target.
/// - <b>Graveyard-zone activated ability — "{G}, exile from graveyard:
///   return to hand" (CR 113.6 + CR 117.1a)</b>: ActivatedAbility with
///   <see cref="ManaCostCost"/>({G}). Same posture
///   <see cref="PriestOfFellRitesFactory"/> + <see cref="PhlageFactory"/>
///   use — the exile-self-from-graveyard half of the cost is folded
///   into the resolution body (no
///   <c>ExileSelfFromGraveyardCost</c> primitive exists at v1). The
///   guard rechecks "still in owner's graveyard" so spurious
///   activations from other zones are no-op-shaped while engine
///   zone-scoping is deferred (CR 113.6 — printed source zone is
///   graveyard).
///
/// ## Deferred (v1 gaps)
/// - <b>Zone-scoped activated abilities</b>: the graveyard activation is
///   enumerable from any zone — same engine gap Priest of Fell Rites /
///   Phlage / Reanimate Eternal flag.
/// - <b>Card-vs-token distinction on the graveyard activation</b>:
///   tokens don't survive to the graveyard (CR 110.5g — tokens cease to
///   exist when moved to a non-battlefield zone), so the "return this
///   card" wording is naturally satisfied; no token-guard needed.
/// </summary>
[CardName("World Breaker")]
public static class WorldBreakerFactory
{
    public const string CardName = "World Breaker";
    public const string PrintedManaCost = "{5}{G}";
    public const string GraveyardReturnManaCost = "{G}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>
    /// Construct World Breaker with no live wiring. ETB + attack triggers
    /// + graveyard-zone activation are attached for shape observability;
    /// exile moves use raw zone manipulation. Suitable for dispatcher /
    /// structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct World Breaker with optional runtime services. When
    /// <paramref name="zones"/> is supplied, exile and return-to-hand
    /// moves route through <see cref="ZoneService.MoveCard"/> so
    /// <see cref="CardMovedEvent"/> publishes for any zone-change
    /// subscribers (Containment Priest, Tormod's Crypt, etc.). When
    /// <paramref name="triggers"/> is supplied, the ETB + attack triggers
    /// register with the bus so their events land them on the stack
    /// automatically (CR 603.2).
    /// </summary>
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
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.17 — Reach. Keyword marker; CombatAbilities.HasReach
        // reads from the keyword set during attack/block validation.
        card.AddAbility(new KeywordAbility("Reach", card, owner));

        // ----------------------------------------------------------------
        // ETB trigger — "When this creature enters, exile target nonbasic
        // land." CR 603.6a + CR 701.21.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbEffect = new Effect(
            $"{CardName}: exile target nonbasic land (ETB)",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not ICard target) return;

                // CR 608.2b — re-check at resolution.
                if (!target.HasType(CardType.Land)) return;
                if (target.HasSupertype(CardSupertype.Basic)) return;
                if (target.Zone != ZoneType.Battlefield) return;

                ExileFromBattlefield(target, owner, zones);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonbasic land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx =>
                    {
                        var pool = new List<object>();
                        foreach (var p in ctx.AllPlayers)
                        {
                            foreach (var c in p.Zones.Battlefield.GetCards())
                            {
                                if (c.HasType(CardType.Land)
                                    && !c.HasSupertype(CardSupertype.Basic))
                                {
                                    pool.Add(c);
                                }
                            }
                        }
                        return pool;
                    }),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Attack trigger — "Whenever this creature attacks, exile target
        // permanent that's one or more colors." CR 508.1f + CR 105 + CR
        // 701.21.
        // ----------------------------------------------------------------
        TriggeredAbility? attackTrigger = null;
        var attackEffect = new Effect(
            $"{CardName}: exile target coloured permanent (attack)",
            () =>
            {
                if (attackTrigger == null) return;
                var chosen = attackTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not ICard target) return;

                // CR 608.2b — re-check colour + on-battlefield at
                // resolution.
                if (target.Zone != ZoneType.Battlefield) return;
                if (CardColors.GetColors(target).Count == 0) return;

                ExileFromBattlefield(target, owner, zones);
            });

        attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target permanent that's one or more colors",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx =>
                    {
                        var pool = new List<object>();
                        foreach (var p in ctx.AllPlayers)
                        {
                            foreach (var c in p.Zones.Battlefield.GetCards())
                            {
                                if (CardColors.GetColors(c).Count >= 1)
                                {
                                    pool.Add(c);
                                }
                            }
                        }
                        return pool;
                    }),
            });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        // ----------------------------------------------------------------
        // Graveyard-zone activated ability —
        //   "{G}, Exile this card from your graveyard:
        //    Return this card to its owner's hand."
        // CR 113.6 + CR 117.1a. Same posture as Priest of Fell Rites'
        // graveyard activation — the exile-self portion of the cost is
        // performed inside the resolve body (no
        // ExileSelfFromGraveyardCost primitive yet). The mana cost is
        // exposed as ManaCostCost for shape inspection.
        // ----------------------------------------------------------------
        var graveyardEffect = new Effect(
            $"{CardName}: exile from graveyard, return to owner's hand",
            () =>
            {
                if (card.Zone != ZoneType.Graveyard) return;
                if (card.Owner == null) return;
                if (!ReferenceEquals(card.Owner, owner)) return;

                // Cost half — exile self from owner's graveyard.
                if (zones != null)
                {
                    zones.MoveCard(card, ZoneType.Graveyard, ZoneType.Exile);
                }
                else
                {
                    owner.Zones.Graveyard.RemoveCard(card);
                    owner.Zones.Exile.AddCard(card);
                    card.SetZone(ZoneType.Exile);
                }

                // Effect half — return this card from exile to owner's
                // hand. CR 701.20 — "return … to its owner's hand". The
                // exile zone we just moved through is the staging post
                // for the cost; the hand move is the printed effect.
                if (zones != null)
                {
                    zones.MoveCard(card, ZoneType.Exile, ZoneType.Hand, owner);
                }
                else
                {
                    owner.Zones.Exile.RemoveCard(card);
                    owner.Zones.Hand.AddCard(card);
                    card.SetZone(ZoneType.Hand);
                }
            });

        var graveyardAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(GraveyardReturnManaCost) },
            effects: new IEffect[] { graveyardEffect });

        card.AddAbility(graveyardAbility);

        return card;
    }

    /// <summary>
    /// CR 701.21 — exile <paramref name="target"/> from the battlefield
    /// to its owner's exile zone. Routes through
    /// <see cref="ZoneService.MoveCard"/> when <paramref name="zones"/>
    /// is supplied so <see cref="CardMovedEvent"/> fires; raw zone
    /// manipulation otherwise (same shape Karn -3 / Ulamog ETB take).
    /// </summary>
    private static void ExileFromBattlefield(ICard target, Player fallbackOwner, ZoneService? zones)
    {
        if (zones != null)
        {
            zones.MoveCard(target, ZoneType.Battlefield, ZoneType.Exile);
            return;
        }

        var holder = target.Controller ?? target.Owner;
        holder?.Zones.Battlefield.RemoveCard(target);
        var exileOwner = target.Owner ?? fallbackOwner;
        exileOwner.Zones.Exile.AddCard(target);
        target.SetZone(ZoneType.Exile);
    }
}
