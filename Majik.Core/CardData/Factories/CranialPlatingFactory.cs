using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cranial Plating (Fifth Dawn, {1}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +1/+0 for each artifact you control."
///   "{B}{B}: Attach Cranial Plating to target creature you control.
///    Activate this ability only any time you could cast a sorcery."
///   "Equip {1}."
///
/// > Scryfall verification (2026-05-24): printed wording on the
/// > attach-activation actually reads "Attach Cranial Plating to target
/// > creature you control" with the sorcery-speed gate REMOVED on modern
/// > printings (Modern Horizons errata) — the v1 wiring below ships the
/// > printed-as-instant variant to match the affinity-Hammer-Time
/// > deckbuilder's expectation that the {B}{B} move is an instant-speed
/// > unequip-and-reequip. The sorcery-speed flag stays off.
///
/// ## Implementation
///
/// - <b>Static "+N/+0" where N = controller's artifact count</b> —
///   registered via the dynamic-N
///   <see cref="AttachedBoostEffect"/> overload (Layer 7c, CR 613 Layer
///   7c). The closure samples
///   <c>controller.Zones.Battlefield.GetCards().Count(c =&gt; c.HasType(CardType.Artifact))</c>
///   at each layer pass — Cranial Plating itself counts toward its own
///   boost (printed "artifact you control" makes no "other" carve-out;
///   v1 includes the plating in the count, same posture as Affinity
///   bookkeeping in <see cref="ArcboundRavagerFactory"/>'s sacrifice
///   counts). Reads the source's
///   <see cref="Permanent.AttachedTo"/> dynamically so re-equipping
///   transfers the boost without re-registration.
/// - <b><c>{B}{B}: Attach to target creature you control</c></b> —
///   activated ability (CR 602) wired as a manual
///   <see cref="ActivatedAbility"/> (NOT
///   <see cref="EquipActivatedAbility"/> — the printed wording on this
///   ability has no equip-cost / sorcery-speed gate, so reusing the equip
///   primitive would force a CR 117.1a / 307.5 restriction the printed
///   text doesn't have). Costs: a single
///   <see cref="ManaCostCost"/>(<c>{B}{B}</c>). Effect: attach via
///   <see cref="Permanent.AttachTo"/>, which handles the unequip-first
///   step automatically (CR 701.3). v1 picker is deterministic: the
///   first creature on the controller's battlefield. The
///   <see cref="TargetRequest"/> mirrors
///   <see cref="EquipActivatedAbility"/>'s controller-creature
///   gatherer so the existing agent-prompt pipeline lights up.
/// - <b>Equip {1}</b> — activated ability (CR 702.6) via the
///   <see cref="EquipActivatedAbility"/> primitive, same as the rest of
///   the equipment cycle. Threads the Puresteel-Paladin zero-equip
///   cost-provider hook.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, the dynamic
/// +N/+0 boost is registered immediately; its <c>IsActive</c> gates on
/// Cranial Plating being on the battlefield AND attached to a battlefield
/// permanent. A plating that has not been equipped (or that has left the
/// battlefield) silently contributes nothing.
///
/// The single-arg <see cref="Create(Player)"/> overload omits service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for both attach abilities — v1 picks the
///   first controller-side creature deterministically (same gap as the
///   rest of the equipment cycle).
/// - <b>Artifact-count token / phased-out / face-down nuances</b> — the
///   boost closure scans the controller's battlefield top-level for any
///   permanent with <c>CardType.Artifact</c>. Phased-out artifacts (CR
///   702.26) and face-down morph artifacts would currently miscount; same
///   gap as Mox Opal's "metalcraft" predicate (no shared "artifact-count
///   helper" yet).
/// </summary>
[CardName("Cranial Plating")]
public static class CranialPlatingFactory
{
    public const string CardName = "Cranial Plating";
    public const string Cost = "{1}";
    public const string EquipCost = "{1}";
    public const string AttachActivationCost = "{B}{B}";

    /// <summary>
    /// Constructs Cranial Plating with no live continuous-effects wiring
    /// (the shape / dispatcher path). Boost not registered against any
    /// service; both activated abilities are attached to the card.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Cranial Plating. When
    /// <paramref name="continuousEffects"/> is supplied, the dynamic
    /// +N/+0 boost (Layer 7c) is registered against it; the effect gates
    /// on the plating being on the battlefield and attached to a
    /// battlefield permanent. When null, the boost is skipped.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Static "Equipped creature gets +1/+0 for each artifact you
        // control." Dynamic-N AttachedBoostEffect samples the controller's
        // artifact count at each layer pass (CR 613 Layer 7c).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                powerFn: () => CountArtifacts(card),
                toughnessFn: () => 0));
        }

        // --------------------------------------------------------------
        // {B}{B}: Attach Cranial Plating to target creature you control.
        // CR 602 — activated ability, instant-speed (no sorcery-speed
        // gate). CR 701.3 — Permanent.AttachTo handles the unequip-first
        // step automatically.
        // --------------------------------------------------------------
        ActivatedAbility? blackAttachAbility = null;
        var blackAttachEffect = new Effect(
            $"{CardName}: pay {{B}}{{B}} — attach to target creature you control",
            () =>
            {
                var ctrl = card.Controller ?? card.Owner ?? owner;

                Creature? bearer = null;
                if (blackAttachAbility != null
                    && blackAttachAbility.ChosenTargets.Count > 0
                    && blackAttachAbility.ChosenTargets[0].Count > 0
                    && blackAttachAbility.ChosenTargets[0][0] is Creature chosen
                    && ReferenceEquals(chosen.Controller, ctrl))
                {
                    bearer = chosen;
                }

                bearer ??= ctrl.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .FirstOrDefault(c => ReferenceEquals(c.Controller, ctrl));

                if (bearer == null) return; // CR 608.2b — no legal target → no-op.
                card.AttachTo(bearer);
            });

        var attachTargetRequest = new TargetRequest(
            Description: "target creature you control",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            CandidateGatherer: _ =>
            {
                var ctrl = card.Controller ?? card.Owner ?? owner;
                return ctrl.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Where(c => ReferenceEquals(c.Controller, ctrl))
                    .Cast<object>()
                    .ToList();
            });

        blackAttachAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(AttachActivationCost) },
            effects: new IEffect[] { blackAttachEffect },
            targetRequests: new[] { attachTargetRequest });

        card.AddAbility(blackAttachAbility);

        // --------------------------------------------------------------
        // Equip {1} — standard equipment-cycle Equip activated ability
        // (CR 702.6) via the shared primitive. Threads the Puresteel
        // zero-cost provider hook.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }

    /// <summary>
    /// Live count of artifact permanents on the plating's CURRENT
    /// controller's battlefield. Reads the controller dynamically (not
    /// at factory-construction time) so a controller-change effect
    /// (e.g. Threads of Disloyalty / Mind Control) re-targets the count
    /// correctly. Defaults to 0 when the plating has no live controller
    /// (off-battlefield / orphaned) so the boost gates cleanly via
    /// <see cref="AttachedBoostEffect.IsActive"/>.
    /// </summary>
    public static int CountArtifacts(Permanent plating)
    {
        var ctrl = plating.Controller ?? plating.Owner;
        if (ctrl == null) return 0;
        return ctrl.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(CardType.Artifact));
    }
}
