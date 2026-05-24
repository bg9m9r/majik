using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.Abilities;

/// <summary>
/// First-class primitive for Equipment's printed equip activated ability
/// (CR 702.6): "Equip {cost}: Attach to target creature you control.
/// Activate only as a sorcery."
///
/// <para>
/// Replaces the per-factory hand-rolled <see cref="ActivatedAbility"/>
/// + <see cref="Effect"/> wiring that every Equipment was duplicating.
/// Encapsulates four things in one shape:
/// </para>
///
/// <list type="number">
///   <item><description><b>Printed equip cost</b> (mana) — exposed via
///   <see cref="EquipCost"/>. Tests inspecting
///   <see cref="ActivatedAbility.Costs"/> still see a
///   <see cref="ManaCostCost"/>, preserving the legacy
///   <c>Costs.OfType&lt;ManaCostCost&gt;().Single()</c> shape assertions.</description></item>
///   <item><description><b>Dynamic cost provider</b> — a per-permanent
///   <see cref="CostProvider"/> delegate consulted at cost-pay time. Default
///   returns the printed <see cref="EquipCost"/>; Puresteel-Paladin-style
///   "Equipment you control have equip {0} if you control three or more
///   artifacts" overrides this to <see cref="ManaCost.Zero"/> via the
///   <see cref="ZeroEquipCostEffect"/> registry consult.</description></item>
///   <item><description><b>Sorcery-speed gate</b> (CR 117.1a / 307.5) —
///   <see cref="ActivatedAbility.IsSorcerySpeed"/> = true. The
///   <see cref="Majik.Core.Rules.ActionValidator"/> enforces it.</description></item>
///   <item><description><b>Target-creature-you-control</b> shape (CR 702.6b)
///   — exposed via <see cref="TargetCreature"/>. Min/Max = 1; legal
///   candidates are scanned live from the activating player's battlefield
///   at agent-prompt time via the request's
///   <see cref="TargetRequest.CandidateGatherer"/>. v1 falls back to the
///   deterministic-first-creature picker when an agent doesn't supply
///   <see cref="ActivatedAbility.ChosenTargets"/> (so existing
///   <c>activated.Resolve()</c> shape-tests don't need refactoring).</description></item>
/// </list>
///
/// <para>
/// On <see cref="Resolve"/>, the Equipment <see cref="ActivatedAbility.Source"/>
/// is attached (CR 701.3) to the chosen target via <see cref="Permanent.AttachTo"/>;
/// if it was already attached to a different creature, the move is automatic
/// (Permanent.AttachTo calls Unattach() first).
/// </para>
///
/// <para>
/// LTB-unattach (CR 704.5n) is handled elsewhere — when the equipped
/// creature leaves the battlefield, the existing zone-move pipeline drops
/// the AttachedTo edge. This primitive is purely about the activation flow.
/// </para>
/// </summary>
public sealed class EquipActivatedAbility : ActivatedAbility
{
    /// <summary>
    /// The printed equip cost (e.g. {2} on Sword of Fire and Ice).
    /// </summary>
    public ManaCost EquipCost { get; }

    /// <summary>
    /// Effective-cost provider consulted at cost-pay time. Receives the
    /// equipment <see cref="Permanent"/> source and returns the
    /// <see cref="ManaCost"/> that must actually be paid. Defaults to
    /// <c>_ =&gt; EquipCost</c>; static cost-modification effects
    /// (Puresteel-Paladin-style zero-equip) override this.
    /// </summary>
    public Func<Permanent, ManaCost> CostProvider { get; }

    /// <summary>
    /// The "creature you control" target request (CR 702.6b). Exposed for
    /// agent-prompt pipelines; equivalent to the single entry in
    /// <see cref="ActivatedAbility.TargetRequests"/>.
    /// </summary>
    public TargetRequest TargetCreature { get; }

    private readonly Permanent _equipmentSource;
    private readonly Action<Permanent, Creature>? _onAttached;

    /// <summary>
    /// Construct an Equip activated ability for <paramref name="source"/>
    /// with the printed cost <paramref name="cost"/>. The default
    /// <see cref="CostProvider"/> always returns the printed cost; pass
    /// <paramref name="costProvider"/> to override (e.g. Puresteel zero-equip).
    /// <paramref name="onAttached"/> fires after a successful attach
    /// (CR 701.3) and is used by cards like Sword of Feast and Famine to
    /// re-sync per-bearer lifecycles after a re-equip.
    /// </summary>
    public EquipActivatedAbility(
        Permanent source,
        ManaCost cost,
        Func<Permanent, ManaCost>? costProvider = null,
        Action<Permanent, Creature>? onAttached = null)
        : this(source, cost, costProvider, onAttached, BuildTargetRequest(source))
    {
    }

    /// <summary>String-cost convenience overload. Parses the cost via
    /// <see cref="ManaCost.Parse"/>.</summary>
    public EquipActivatedAbility(
        Permanent source,
        string cost,
        Func<Permanent, ManaCost>? costProvider = null,
        Action<Permanent, Creature>? onAttached = null)
        : this(source, ManaCost.Parse(cost ?? string.Empty), costProvider, onAttached)
    {
    }

    private EquipActivatedAbility(
        Permanent source,
        ManaCost cost,
        Func<Permanent, ManaCost>? costProvider,
        Action<Permanent, Creature>? onAttached,
        TargetRequest targetRequest)
        : base(
            source: source ?? throw new ArgumentNullException(nameof(source)),
            controller: (source.Controller ?? source.Owner)
                ?? throw new ArgumentException("Equipment source must have a controller or owner", nameof(source)),
            costs: new ICost[] { new EquipManaCostCost(source, cost, costProvider) },
            effects: BuildEffects(source, onAttached),
            targetRequests: new[] { targetRequest },
            sorcerySpeed: true)
    {
        _equipmentSource = source;
        _onAttached = onAttached;
        EquipCost = cost ?? throw new ArgumentNullException(nameof(cost));
        CostProvider = costProvider ?? (_ => cost);
        TargetCreature = targetRequest;
    }

    private static TargetRequest BuildTargetRequest(Permanent source)
    {
        var controller = source.Controller ?? source.Owner;
        return new TargetRequest(
            Description: "Attach to target creature you control",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            CandidateGatherer: _ =>
            {
                // CR 702.6b — legal targets are creatures the Equipment's
                // controller controls. Read live from the equipment's
                // CURRENT controller (so a controller-change still
                // enumerates correctly).
                var ctrl = source.Controller ?? controller;
                if (ctrl == null) return Array.Empty<object>();
                return ctrl.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Where(c => ReferenceEquals(c.Controller, ctrl))
                    .Cast<object>()
                    .ToList();
            });
    }

    private static IEffect[] BuildEffects(
        Permanent source,
        Action<Permanent, Creature>? onAttached)
    {
        return new IEffect[]
        {
            new Effect(
                $"{source.Name}: equip — attach to target creature you control",
                () => AttachOnResolve(source, onAttached))
        };
    }

    /// <summary>
    /// Resolution shape (CR 608) — attach <paramref name="source"/> to the
    /// chosen target if one was supplied; otherwise fall back to the
    /// deterministic-first-controller-creature picker that the legacy
    /// hand-rolled equip ability used (so shape-only tests that build an
    /// equipment and call <c>activated.Resolve()</c> without an agent loop
    /// still observe an attach).
    /// </summary>
    private static void AttachOnResolve(
        Permanent source,
        Action<Permanent, Creature>? onAttached)
    {
        var owner = source.Controller ?? source.Owner;
        if (owner == null) return;

        // Pull the chosen target from the most-recently-activated equip
        // ability hung off the source, if any. Effects don't have a
        // back-pointer to their owning ability, so the fallback path is
        // identical to the legacy factory shape.
        Creature? bearer = null;

        foreach (var ability in source.Abilities)
        {
            if (ability is EquipActivatedAbility eq
                && ReferenceEquals(eq.Source, source)
                && eq.ChosenTargets.Count > 0
                && eq.ChosenTargets[0].Count > 0
                && eq.ChosenTargets[0][0] is Creature chosen
                && ReferenceEquals(chosen.Controller, owner))
            {
                bearer = chosen;
                break;
            }
        }

        bearer ??= owner.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => ReferenceEquals(c.Controller, owner));

        if (bearer == null) return; // No legal target → no-op (CR 608.2b).
        source.AttachTo(bearer);
        onAttached?.Invoke(source, bearer);
    }

    /// <summary>
    /// Dynamic mana-cost cost shim — re-evaluates the effective cost via
    /// the <see cref="EquipActivatedAbility.CostProvider"/> on every
    /// <see cref="CanPay"/> / <see cref="Pay"/>. Surfaces as a normal
    /// <see cref="ManaCostCost"/> via the base type so existing shape
    /// assertions (<c>Costs.OfType&lt;ManaCostCost&gt;().Single().Cost.Generic</c>)
    /// continue to read the PRINTED cost — Puresteel's zero-cost rider is
    /// applied only at pay time, not at the printed-shape level.
    /// </summary>
    private sealed class EquipManaCostCost : ManaCostCost
    {
        private readonly Permanent _source;
        private readonly Func<Permanent, ManaCost>? _costProvider;

        public EquipManaCostCost(
            Permanent source,
            ManaCost printedCost,
            Func<Permanent, ManaCost>? costProvider)
            : base(printedCost)
        {
            _source = source;
            _costProvider = costProvider;
        }

        private ManaCost EffectiveCost()
        {
            if (_costProvider == null) return Cost;
            try
            {
                return _costProvider(_source) ?? Cost;
            }
            catch
            {
                return Cost;
            }
        }

        public override bool CanPay(Player player)
        {
            if (player == null) return false;
            var effective = EffectiveCost();
            if (effective.IsZero) return true;
            return player.ManaPool.CanPay(effective);
        }

        public override void Pay(Player player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            var effective = EffectiveCost();
            if (effective.IsZero) return; // CR 117.5 — pay nothing.
            if (!player.ManaPool.CanPay(effective))
                throw new Domain.Exceptions.InvalidPlayerActionException(
                    $"Cannot pay equip cost: {effective}");
            if (!player.PayMana(effective))
                throw new Domain.Exceptions.InvalidPlayerActionException(
                    $"Failed to pay equip cost: {effective}");
        }
    }
}
