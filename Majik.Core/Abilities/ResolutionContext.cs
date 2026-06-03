using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.Abilities;

/// <summary>
/// PLAN 01 — the live context handed to an <see cref="IEffect.ExecuteAsync"/>
/// call when a spell / activated ability / triggered ability resolves
/// (CR 608). The stack object constructs it at resolve time from its own
/// <see cref="Controller"/> + chosen targets and the resolver-supplied
/// <see cref="Agent"/> / <see cref="Game"/> / <see cref="Ct"/>.
///
/// <para>
/// <see cref="Agent"/> and <see cref="Game"/> are nullable to support the
/// legacy context-free synchronous execution path (<see cref="IEffect.Execute"/>
/// and effects built from the legacy <c>Effect(string, Action)</c> ctor that
/// capture everything in a closure and never read the context). New async
/// effects that DO need the agent / live game should read them off this
/// record instead of reaching for <see cref="Players.Agents.AgentRegistry"/>
/// or a captured-null <see cref="GameContext"/>.
/// </para>
/// </summary>
public sealed record ResolutionContext(
    Player Controller,
    IPlayerAgent? Agent,
    GameContext? Game,
    IReadOnlyList<IReadOnlyList<object>> ChosenTargets,
    CancellationToken Ct = default)
{
    private static readonly IReadOnlyList<IReadOnlyList<object>> EmptyTargets =
        Array.Empty<IReadOnlyList<object>>();

    /// <summary>
    /// CR 608.2g — LAST-KNOWN-information snapshot of each chosen target slot's
    /// controller, captured at resolution START (before any effect mutates the
    /// game). Keyed by slot index; only slots whose [0] pick was a
    /// battlefield <see cref="Permanent"/> at resolution start are recorded
    /// (with that permanent's controller), so a target that left in response
    /// records nothing and any rider keyed on it fizzles (CR 608.2b).
    ///
    /// <para>
    /// This exists for SHARED-SLOT riders on the ABILITY path — the "its
    /// controller loses N life" half of a Vapor-Snag-style bounce, where the
    /// host (bounce) runs FIRST in printed order and moves the shared target
    /// off the battlefield, so a naive resolution-time controller read by the
    /// rider would see the post-bounce zone. Reading this snapshot is the
    /// ability-path analogue of the SPELL bridge's pre-host snapshot
    /// (<c>CardDefRuntime.BuildLoseLifeRiderSnapshot</c>). Players target
    /// themselves and need no snapshot.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<int, Player> SharedSlotControllers { get; private init; } =
        EmptySharedSlot;

    /// <summary>
    /// CR 603.3 — the "that player" / triggering player a TRIGGERED ability
    /// identified from its event as it matched (e.g. the caster of the spell
    /// that fired Ash Zealot's "Whenever a player casts a spell from a
    /// graveyard, this creature deals 3 damage to that player"). Carried from
    /// <see cref="TriggeredAbility.TriggeringPlayer"/> so an UNTARGETED resolve
    /// effect (<see cref="Majik.Core.CardData.Definitions.DealDamageToTriggeringPlayerEffectDef"/>)
    /// can punish the triggering player without a chosen <c>TargetRequest</c> —
    /// the declarative analogue of the hand-rolled boxed-closure idiom. Null on
    /// the spell / activated-ability paths (no triggering player) and when the
    /// trigger never identified one.
    /// </summary>
    public Player? TriggeringPlayer { get; init; }

    private static readonly IReadOnlyDictionary<int, Player> EmptySharedSlot =
        new Dictionary<int, Player>();

    /// <summary>
    /// Context for the legacy synchronous execution path — no controller,
    /// agent, game or chosen targets. Used by <see cref="IEffect.Execute"/>
    /// and any caller that re-runs a self-contained sync effect without a
    /// live resolution frame (e.g. spell-copy, nested-effect composition).
    /// Effects built from the legacy <c>Action</c> ctor ignore the context
    /// entirely, so the null fields are never dereferenced on that path.
    /// </summary>
    public static ResolutionContext Legacy { get; } =
        new(Controller: null!, Agent: null, Game: null, ChosenTargets: EmptyTargets);

    /// <summary>
    /// Build a resolution context for a stack object resolving now, defaulting
    /// the chosen-targets list to empty when none were supplied.
    /// </summary>
    public static ResolutionContext For(
        Player controller,
        IPlayerAgent? agent,
        GameContext? game,
        IReadOnlyList<IReadOnlyList<object>>? chosenTargets,
        CancellationToken ct = default)
    {
        var targets = chosenTargets ?? EmptyTargets;
        return new(controller, agent, game, targets, ct)
        {
            SharedSlotControllers = SnapshotSharedSlotControllers(targets),
        };
    }

    /// <summary>CR 608.2g — capture each chosen slot's controller NOW (resolution
    /// start), while every still-legal targeted permanent is on the battlefield,
    /// so a shared-slot rider that resolves AFTER its host moved the target reads
    /// the pre-host controller. Only battlefield permanents are recorded.</summary>
    private static IReadOnlyDictionary<int, Player> SnapshotSharedSlotControllers(
        IReadOnlyList<IReadOnlyList<object>> targets)
    {
        Dictionary<int, Player>? snapshot = null;
        for (var i = 0; i < targets.Count; i++)
        {
            var slot = targets[i];
            if (slot.Count == 0) continue;
            if (slot[0] is Permanent permanent
                && permanent.Zone == ZoneType.Battlefield
                && permanent.Controller is { } controller)
            {
                (snapshot ??= new Dictionary<int, Player>())[i] = controller;
            }
        }
        return snapshot ?? EmptySharedSlot;
    }
}
