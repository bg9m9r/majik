using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for a "Equip abilities you activate that target
/// this creature cost {N} less to activate" static cost-reduction effect.
///
/// Models the third printed ability on Fervent Champion (Throne of Eldraine,
/// {R}): "Equip abilities you activate that target this creature cost {3}
/// less to activate." CR 117.7 / 702.6c — a cost-modification static scoped
/// to a specific permanent (the creature the Equip targets), not to the
/// controller.
///
/// ## Why this mirrors <see cref="ZeroEquipCostEffect"/>
///
/// Equip-cost modification already has one process-wide registry consulted
/// at activation/pay time: <see cref="ZeroEquipCostEffect"/>
/// (Puresteel-Paladin-style "Equipment you control have equip {0}"). That
/// registry is keyed on the equipment's <i>controller</i>. Fervent Champion's
/// reduction is the same shape of effect but keyed on the <i>target creature</i>
/// — "that target this creature". So this binder is a sibling registry, keyed
/// on the <see cref="Creature"/> bound at construction, exposing one read API:
/// <see cref="ReductionForTarget(Creature)"/>. The shared equip cost provider
/// (<see cref="Majik.Core.CardData.Factories.PuresteelPaladinFactory.ZeroEquipCostProvider"/>)
/// consults BOTH registries: it floors to {0} when a zero-equip override is
/// active for the controller, otherwise subtracts the summed
/// <see cref="ReductionForTarget"/> for the equip ability's chosen target from
/// the printed generic cost (CR 117.7c — never below zero; coloured pips
/// untouched).
///
/// ## Generic-only reduction (CR 117.7c)
///
/// The reduction lowers only the generic-mana portion of the equip cost (e.g.
/// Cori-Steel Cutter's Equip {1}{R} reduced by {3} becomes {R} — the {R} pip
/// survives, the {1} generic is removed). Floor-at-zero is enforced by the
/// consumer.
///
/// ## Multiple sources stack additively
///
/// Two Fervent Champions both bound to the same equip target each contribute
/// their reduction (CR 117.7 — multiple reductions sum). The registry sums
/// every registered binder whose bound creature is the queried target.
///
/// ## Lifecycle
///
/// Mirrors <see cref="ZeroEquipCostEffect"/>: subscribe to
/// <see cref="CardMovedEvent"/>, register/unregister via a shared static
/// registry on the source's ETB / LTB. While registered,
/// <see cref="ReductionForTarget"/> returns the reduction for the bound
/// creature iff the source is on the battlefield.
/// </summary>
public sealed class EquipCostReductionEffect
{
    /// <summary>Fervent Champion's printed reduction amount ("{3} less").</summary>
    public const int DefaultReduction = 3;

    private readonly Permanent? _source;
    private readonly Creature _target;
    private readonly int _reduction;
    private readonly IEventBus? _eventBus;
    private readonly Action<CardMovedEvent> _handler;
    private bool _attached;
    private bool _registered;

    /// <summary>
    /// Build a Fervent-Champion-style equip-cost reduction lifecycle.
    /// </summary>
    /// <param name="source">The permanent whose presence on the battlefield
    /// gates the effect (Fervent Champion itself). May be null — pair with
    /// <paramref name="eventBus"/> = null for fixture scaffolding where the
    /// binder is attached unconditionally.</param>
    /// <param name="target">The creature the reduction is keyed on ("that
    /// target this creature"). For Fervent Champion this is the source itself
    /// — an Equip targeting Fervent Champion costs {3} less. Modelled as a
    /// separate parameter so the registry isn't restricted to self-targeting
    /// reducers.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be null —
    /// the lifecycle still syncs once on <see cref="Attach"/>.</param>
    /// <param name="reduction">Generic-mana reduction applied to an equip
    /// cost that targets <paramref name="target"/>. Defaults to
    /// <see cref="DefaultReduction"/> (3) per Fervent Champion's wording.</param>
    public EquipCostReductionEffect(
        Permanent? source,
        Creature target,
        IEventBus? eventBus,
        int reduction = DefaultReduction)
    {
        _source = source;
        _target = target ?? throw new ArgumentNullException(nameof(target));
        if (reduction < 0) throw new ArgumentOutOfRangeException(nameof(reduction));
        _reduction = reduction;
        _eventBus = eventBus;
        _handler = OnEvent;
    }

    /// <summary>Whether the binder is currently registered (source on the
    /// battlefield).</summary>
    public bool IsRegistered => _registered;

    /// <summary>The creature the reduction is keyed on — exposed for
    /// diagnostics/tests.</summary>
    public Creature Target => _target;

    /// <summary>
    /// Query: total generic-mana equip-cost reduction currently active for an
    /// equip ability that targets <paramref name="target"/>. Sums every
    /// registered <see cref="EquipCostReductionEffect"/> whose bound creature
    /// is <paramref name="target"/>. Returns 0 when none apply (so callers can
    /// subtract unconditionally).
    /// </summary>
    public static int ReductionForTarget(Creature target)
    {
        if (target == null) return 0;
        var total = 0;
        lock (s_lock)
        {
            foreach (var lifecycle in s_registered)
            {
                if (!ReferenceEquals(lifecycle._target, target)) continue;
                // Live battlefield gate (CR 604.3) — the reducer only applies
                // while its source is on the battlefield. Read live so a
                // registered binder whose source has since left contributes
                // nothing even before the LTB zone-move event is processed
                // (and so the event-bus-less shape path works once the card's
                // Zone is set to Battlefield manually).
                if (!lifecycle.ActiveGate()) continue;
                total += lifecycle._reduction;
            }
        }
        return total;
    }

    /// <summary>
    /// Subscribe to zone-move events and register if the source is already on
    /// the battlefield. Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.Subscribe(_handler);
        // Register unconditionally — applicability is decided live in
        // ReductionForTarget via ActiveGate (battlefield check). This keeps the
        // event-bus-less shape path working: the binder is in the registry from
        // construction, and the query gates on the card's current Zone. With a
        // bus, the subscription still fires for diagnostics / future hooks.
        Register();
    }

    /// <summary>Unsubscribe and unregister. Idempotent.</summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.Unsubscribe(_handler);
        Unregister();
    }

    private bool ActiveGate()
    {
        if (_source == null) return true;
        return _source.Zone == ZoneType.Battlefield;
    }

    private void OnEvent(CardMovedEvent e)
    {
        // No-op for registration (the binder is always registered post-Attach);
        // applicability is a live ActiveGate() read at query time. Retained so a
        // bus-wired source still routes its own zone-move events here without
        // throwing in DEBUG fail-fast mode.
        _ = e;
    }

    private void Register()
    {
        if (_registered) return;
        lock (s_lock) s_registered.Add(this);
        _registered = true;
    }

    private void Unregister()
    {
        if (!_registered) return;
        lock (s_lock) s_registered.Remove(this);
        _registered = false;
    }

    // ----------------------------------------------------------------
    // Process-wide registry. Mirrors ZeroEquipCostEffect — tests should
    // call ResetForTests() in setup if they depend on a clean slate.
    // ----------------------------------------------------------------

    private static readonly HashSet<EquipCostReductionEffect> s_registered = new();
    private static readonly object s_lock = new();

    /// <summary>Test hook: clear all registrations.</summary>
    public static void ResetForTests()
    {
        lock (s_lock) s_registered.Clear();
    }
}
