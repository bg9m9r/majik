using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Quirion Ranger (Visions, {G}).
///
/// Creature — Elf Ranger 1/1. Oracle text (verified against Scryfall):
///   "Return a Forest you control to its owner's hand: Untap target creature.
///    Activate only once each turn."
///
/// The base shape (name, Creature, Elf Ranger subtypes, {G}, 1/1) is
/// materialised from the embedded JSON definition
/// (<c>quirion-ranger.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="HeritageDruidFactory"/>. The activated ability is layered on
/// here because the JSON <c>AbilityDefinition</c> schema doesn't express a
/// return-a-Forest activation cost.
///
/// ## Implemented (v1)
/// - 1/1 Creature — Elf Ranger at printed cost {G}, owner/controller wired.
/// - <b>Activated ability (CR 602.1)</b>: cost = "Return a Forest you control
///   to its owner's hand"; effect = "Untap target creature". This is NOT a
///   mana ability (CR 605.1 — it produces no mana). Modelled as a standard
///   <see cref="ActivatedAbility"/> with a single
///   <see cref="ReturnAForestYouControlCost"/> in its <c>Costs</c> list and a
///   1..1 "target creature" <see cref="TargetRequest"/>. The engine pays the
///   cost through <see cref="CostPayment.PayCosts"/> during
///   <see cref="AbilityActivator.ActivateAbility"/>.
/// - <b>Return-a-Forest cost (CR 118)</b>: the cost is illegal unless the
///   activating player controls at least one Forest on the battlefield. On
///   payment the first eligible Forest the player controls (or
///   <see cref="ReturnAForestYouControlCost.ChosenForest"/> if pre-set by an
///   agent / test) moves from the battlefield to its owner's hand
///   (CR 701.10). Routes through the registered
///   <see cref="ZoneService"/> when one exists so LTB / CardMovedEvent fire;
///   falls back to raw zone manipulation otherwise (mirrors
///   <see cref="AdditionalCost"/>'s Sacrifice payment).
/// - <b>"Activate only once each turn" (CR 602.5e)</b>: a
///   <c>int[1] { 0 }</c> per-turn-lock closure shared between the cost's
///   <see cref="ReturnAForestYouControlCost.CanPay"/> gate and the
///   <see cref="TurnStartedEvent"/> reset handler installed by the
///   <c>(owner, eventBus)</c> overload (same pattern as
///   <see cref="WallOfRootsFactory"/>). The single-arg dispatcher path
///   attaches the ability with the lock active but never resets it —
///   suitable for shape / single-activation tests.
/// - <b>Untap-target-creature effect (CR 701.27)</b>: on resolution the
///   chosen target is re-validated against CR 608.2b (still on the
///   battlefield, still a Creature) and then <see cref="Permanent.Untap"/>'d.
///   Idempotent on an already-untapped creature (untap of an untapped
///   permanent is a no-op — same posture as <see cref="ArborElfFactory"/>).
///
/// ## Notes
/// - The Forest does not have to be untapped, and the untapped target
///   creature can be the very creature whose Forest was returned, or any
///   creature including an opponent's (printed text says "target creature",
///   no controller restriction) — the canonical Stompy line untaps a tapped
///   attacker / a creature with a tap ability.
/// - The classic mana-engine line returns a Forest to untap a creature, then
///   replays the Forest from hand for {G}; the engine sequences these as the
///   independent cost + a normal land drop.
///
/// ## Deferred (v1 gaps)
/// - <b>Choose-time target enumeration</b>:
///   <see cref="TargetRequest.LegalCandidates"/> is left empty — the
///   production agent enumerates the live battlefield itself (same posture as
///   <see cref="ArborElfFactory"/> / Solitude). Resolve-time recheck enforces
///   the creature predicate.
/// - <b>Forest choice</b>: when more than one Forest is controlled and no
///   explicit <see cref="ReturnAForestYouControlCost.ChosenForest"/> is set,
///   the cost returns the first eligible Forest deterministically.
/// </summary>
[CardName("Quirion Ranger")]
public static class QuirionRangerFactory
{
    public const string CardName = "Quirion Ranger";
    public const string Slug = "quirion-ranger";

    /// <summary>
    /// Construct Quirion Ranger with no event-bus wiring. The once-per-turn
    /// lock is attached but never reset — the first activation's cost
    /// succeeds, subsequent activations within the same Creature lifetime are
    /// blocked. Suitable for card-shape / dispatcher tests and single-turn
    /// scenarios.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Quirion Ranger with optional <see cref="TurnStartedEvent"/>
    /// reset wiring. When <paramref name="eventBus"/> is supplied, the
    /// per-turn activation lock is reset at the start of every turn
    /// (CR 500.1) — mirrors <see cref="WallOfRootsFactory"/>.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Elf
        // Ranger subtypes, {G}, 1/1). The JSON carries no abilities — the
        // activated ability is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 602.5e — "Activate only once each turn." Closure shared between
        // the cost's CanPay gate and the TurnStartedEvent reset handler.
        var usedThisTurn = new int[] { 0 };

        // CR 118 — "Return a Forest you control to its owner's hand" is the
        // entire activation cost. The cost also enforces the once-per-turn
        // lock so the engine's generic cost-legality check (CostPayment /
        // CanPayCosts) refuses a second activation the same turn.
        var returnForestCost = new ReturnAForestYouControlCost(owner, usedThisTurn);

        // CR 701.27 — "Untap target creature." Re-validate the chosen target
        // at resolution (CR 608.2b) and untap if still a creature on the
        // battlefield. Untap of an already-untapped permanent is a no-op.
        ActivatedAbility? untapAbility = null;
        var untapEffect = new Effect(
            $"{CardName}: untap target creature",
            () =>
            {
                if (untapAbility == null) return;
                var chosen = untapAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution. Target must still be on
                // the battlefield AND still be a Creature.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Creature)) return;

                // CR 701.27 — untap. Permanent.Untap() throws when not tapped,
                // so guard with IsTapped (idempotent no-op when untapped).
                if (target.IsTapped) target.Untap();
            });

        untapAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { returnForestCost },
            effects: new IEffect[] { untapEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(untapAbility);

        // CR 500.1 — reset the per-turn activation lock at the start of each
        // turn. When no event bus is supplied the lock remains set after the
        // first activation — acceptable for shape / single-turn tests.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => usedThisTurn[0] = 0);
        }

        return card;
    }
}

/// <summary>
/// Quirion Ranger's activation cost — "Return a Forest you control to its
/// owner's hand" (CR 118). Also gates the printed "Activate only once each
/// turn" restriction (CR 602.5e) via a shared per-turn-lock closure so the
/// engine's generic <see cref="CostPayment"/> legality check refuses a second
/// activation in the same turn.
/// </summary>
public sealed class ReturnAForestYouControlCost : ICost
{
    private readonly Player _controller;
    private readonly int[] _usedThisTurn;

    /// <summary>
    /// Optional explicit Forest to return. When null, the cost returns the
    /// first eligible Forest the controller controls (deterministic).
    /// </summary>
    public Permanent? ChosenForest { get; set; }

    public string Description => "Return a Forest you control to its owner's hand";

    internal ReturnAForestYouControlCost(Player controller, int[] usedThisTurn)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _usedThisTurn = usedThisTurn ?? throw new ArgumentNullException(nameof(usedThisTurn));
    }

    /// <summary>
    /// CR 118 / 602.5e — legal only while the once-per-turn lock is open AND
    /// the player controls at least one eligible Forest on the battlefield.
    /// </summary>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        if (_usedThisTurn[0] != 0) return false;
        return PickForest(player) != null;
    }

    /// <summary>
    /// CR 701.10 — move the chosen Forest from the battlefield to its owner's
    /// hand, then flip the per-turn lock (CR 602.5e). Routes through the
    /// registered <see cref="ZoneService"/> when one exists so LTB /
    /// CardMovedEvent fire; falls back to raw zone manipulation otherwise
    /// (mirrors <see cref="AdditionalCost"/>'s Sacrifice payment).
    /// </summary>
    public void Pay(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var forest = PickForest(player)
            ?? throw new Majik.Core.Domain.Exceptions.InvalidPlayerActionException(
                "Cannot pay cost: no Forest you control to return.");

        var owner = forest.Owner ?? player;
        var holder = forest.Controller ?? owner;

        var zones = ZoneServiceRegistry.Get(holder);
        if (zones != null)
        {
            zones.MoveCard(forest, ZoneType.Battlefield, ZoneType.Hand, owner);
        }
        else
        {
            holder.Zones.Battlefield.RemoveCard(forest);
            owner.Zones.Hand.AddCard(forest);
            forest.SetZone(ZoneType.Hand);
        }

        // CR 602.5e — record this turn's single permitted activation.
        _usedThisTurn[0] = 1;
    }

    /// <summary>
    /// The Forest to return: the explicit <see cref="ChosenForest"/> if it is
    /// a currently-eligible Forest the player controls, else the first
    /// eligible Forest on the player's battlefield.
    /// </summary>
    private Permanent? PickForest(Player player)
    {
        bool Eligible(Permanent p) =>
            p.Zone == ZoneType.Battlefield
            && ReferenceEquals(p.Controller ?? p.Owner, player)
            && p.HasType(CardType.Land)
            && p.HasSubtype(CardSubtype.Forest);

        if (ChosenForest != null && Eligible(ChosenForest)) return ChosenForest;

        return player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault(Eligible);
    }
}
