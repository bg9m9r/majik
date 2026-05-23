using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for a "Equipment you control have equip {0}"
/// conditional static effect.
///
/// Models the second printed ability on Puresteel Paladin (New Phyrexia,
/// {1}{W}): "As long as you control three or more artifacts, Equipment
/// you control have equip {0}." CR 604.2 / 613.1f — characteristic-
/// defining / non-CDA static effect on activated-ability costs (cost
/// modification on Equip — CR 702.6c).
///
/// ## Why this lives as a registry-style predicate
///
/// At the time of writing there is no first-class
/// <c>EquipActivatedAbility</c> primitive in the engine — Equipment
/// cards lack the printed "Equip {N}: attach this to target creature you
/// control" activated ability altogether. Rather than block on building
/// out the equip-ability surface, this static is wired as a self-attaching
/// lifecycle that exposes a single read API:
/// <see cref="IsZeroEquipActiveFor(Player)"/>. Any equip-ability
/// implementation (now or later) can consult this registry at activation
/// time to decide whether the printed equip cost is overridden to {0}.
///
/// When an <c>EquipActivatedAbility</c> primitive eventually lands, it
/// should consult <see cref="IsZeroEquipActiveFor(Player)"/> for the
/// current controller of the Equipment, exactly the same way
/// <see cref="Majik.Core.Rules.ActivatedAbilityRestrictions"/> is consulted
/// for Pithing-Needle-style name suppression — no test refactor needed.
///
/// ## Threshold semantics
///
/// "Three or more artifacts" is read live from the controller's
/// battlefield zone at consult time, filtered to cards typed
/// <see cref="CardType.Artifact"/>. The source (Puresteel Paladin itself)
/// is a Creature, not an Artifact, so it never counts toward its own
/// threshold (which matches the printed wording — Puresteel is not an
/// artifact, just an artifact-care card).
///
/// Opponents' artifacts are NOT counted: the predicate scans only the
/// controller's battlefield ("you control three or more artifacts").
///
/// ## Lifecycle
///
/// Mirrors <see cref="PithingNeedleStaticEffect"/>: subscribe to
/// <see cref="CardMovedEvent"/>, register/unregister via a shared static
/// registry on ETB/LTB. While registered, <see cref="IsZeroEquipActiveFor"/>
/// returns true for the bound controller iff the artifact threshold is
/// currently met. Multiple Puresteels under the same controller all
/// register independently; any one being active short-circuits the query.
/// </summary>
public sealed class ZeroEquipCostEffect
{
    /// <summary>Default artifact-count threshold for activation
    /// ("three or more artifacts").</summary>
    public const int DefaultThreshold = 3;

    private readonly Permanent? _source;
    private readonly Player _controller;
    private readonly IEventBus? _eventBus;
    private readonly int _threshold;
    private readonly Action<GameEvent> _handler;
    private bool _attached;
    private bool _registered;

    /// <summary>
    /// Build a Puresteel-style zero-equip-cost lifecycle.
    /// </summary>
    /// <param name="source">The permanent whose presence on the
    /// battlefield gates the effect. May be null — pair with
    /// <paramref name="eventBus"/> = null for fixture scaffolding where
    /// the binder is attached unconditionally.</param>
    /// <param name="controller">The player who benefits ("Equipment you
    /// control"). Their battlefield is also scanned for the artifact
    /// count threshold.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be
    /// null — the lifecycle will still sync once on <see cref="Attach"/>.</param>
    /// <param name="threshold">Number of artifacts on
    /// <paramref name="controller"/>'s battlefield needed for the
    /// override to be active. Defaults to <see cref="DefaultThreshold"/>
    /// (3) per Puresteel Paladin's printed wording.</param>
    public ZeroEquipCostEffect(
        Permanent? source,
        Player controller,
        IEventBus? eventBus,
        int threshold = DefaultThreshold)
    {
        _source = source;
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        if (threshold < 0) throw new ArgumentOutOfRangeException(nameof(threshold));
        _threshold = threshold;
        _eventBus = eventBus;
        _handler = OnEvent;
    }

    /// <summary>Whether the binder is currently registered (i.e. the
    /// source is on the battlefield).</summary>
    public bool IsRegistered => _registered;

    /// <summary>The bound controller — exposed for diagnostics/tests.</summary>
    public Player Controller => _controller;

    /// <summary>
    /// Query: is a zero-equip-cost override currently active for
    /// <paramref name="player"/>? Returns true iff there exists at least
    /// one registered <see cref="ZeroEquipCostEffect"/> whose controller
    /// is <paramref name="player"/> AND whose artifact-count threshold is
    /// currently met on <paramref name="player"/>'s battlefield.
    /// </summary>
    /// <remarks>
    /// Equip-ability consumers should pass the equipment's current
    /// controller. The static effect deliberately ignores opponents'
    /// artifacts and ignores the source's own non-artifact card type.
    /// </remarks>
    public static bool IsZeroEquipActiveFor(Player player)
    {
        if (player == null) return false;
        lock (s_lock)
        {
            foreach (var lifecycle in s_registered)
            {
                if (!ReferenceEquals(lifecycle._controller, player)) continue;
                if (lifecycle.ThresholdMet()) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Subscribe to zone-move events and register if the source is
    /// already on the battlefield. Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.SubscribeAll(_handler);
        Sync();
    }

    /// <summary>
    /// Unsubscribe and unregister. Idempotent.
    /// </summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.UnsubscribeAll(_handler);
        Unregister();
    }

    private bool ActiveGate()
    {
        if (_source == null) return true;
        return _source.Zone == ZoneType.Battlefield;
    }

    private void OnEvent(GameEvent e)
    {
        if (e is not CardMovedEvent moved) return;
        if (_source != null && !ReferenceEquals(moved.Card, _source)) return;
        Sync();
    }

    private void Sync()
    {
        if (ActiveGate()) Register();
        else Unregister();
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

    private bool ThresholdMet()
    {
        int count = 0;
        foreach (var c in _controller.Zones.Battlefield.GetCards())
        {
            if (c.HasType(CardType.Artifact)) count++;
            if (count >= _threshold) return true;
        }
        return false;
    }

    // ----------------------------------------------------------------
    // Process-wide registry. Mirrors ActivatedAbilityRestrictions —
    // tests should call ResetForTests() in setup if they depend on a
    // clean slate. Production code attaches/detaches via lifecycle so
    // global state is cleaned up automatically.
    // ----------------------------------------------------------------

    private static readonly HashSet<ZeroEquipCostEffect> s_registered = new();
    private static readonly object s_lock = new();

    /// <summary>Test hook: clear all registrations. Production code
    /// should never need to call this.</summary>
    public static void ResetForTests()
    {
        lock (s_lock) s_registered.Clear();
    }
}
