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

    /// <summary>
    /// STAGE 1 (re-sourceable abilities) — the battlefield permanent that is
    /// the SOURCE of the resolving ability (CR 113.7 / 608.2g). Lets an effect
    /// read "its source" generically off the live context instead of capturing
    /// a specific permanent in a closure at authoring time. This is the
    /// context-side hook later stages use to migrate effect authoring so a
    /// re-sourced ability (e.g. an activated ability granted by Agatha's Soul
    /// Cauldron and re-homed to a bearer) affects the bearer rather than the
    /// originally-captured permanent.
    ///
    /// <para>
    /// Populated by <see cref="ActivatedAbility.ResolveAsync"/> and
    /// <see cref="TriggeredAbility.ResolveAsync"/> from the ability's own
    /// <c>Source</c> when it is a <see cref="Permanent"/>. Null on the spell
    /// path, the legacy synchronous path, and whenever the ability's source is
    /// not a <see cref="Permanent"/>.
    /// </para>
    /// </summary>
    public Permanent? Source { get; init; }

    /// <summary>
    /// SPELL path — the underlying <see cref="Card"/> of the resolving SPELL
    /// (CR 608, the stack object's card). This is the spell-side analogue of
    /// <see cref="Source"/> (which is the battlefield permanent for the
    /// ability paths): a sorcery / instant has no <see cref="Source"/>
    /// permanent, but its resolution effect may still need to read per-cast
    /// state stamped on the card at payment time — most importantly the
    /// mana-provenance ledger
    /// <see cref="Card.PendingCastColors"/> /
    /// <see cref="Card.PendingCastColorCounts"/> (CR 106.4 / CR 202.2 —
    /// "the number of colors of mana spent to cast this spell"), the
    /// Converge count that gates Prismatic Ending / Bring to Light.
    ///
    /// <para>
    /// Populated by <see cref="Majik.Core.Spells.Spell.ResolveAsync"/> from the
    /// resolving spell's <see cref="Majik.Core.Spells.Spell.Card"/>. Null on the
    /// ability paths (use <see cref="Source"/>), the legacy synchronous path,
    /// and any caller that resolves an effect without a spell frame.
    /// </para>
    /// </summary>
    public ICard? SourceCard { get; init; }

    /// <summary>
    /// GAP 2 — the X chosen for a variable-X ({X}-cost) ACTIVATED ability,
    /// threaded from <see cref="ActivatedAbility.ChosenX"/> by
    /// <see cref="ActivatedAbility.ResolveAsync"/>. Lets the resolution effect
    /// read the real chosen X (Steel Hellkite's destroy-mv-X sweep, Lair of the
    /// Hydra's X/X body, Tameshi's mv ≤ X reanimation) off the live context
    /// instead of a captured <c>Func&lt;int&gt;</c> closure that resolves to 0 on
    /// the production routed build. Null when no {X} was chosen (the spell path,
    /// the legacy sync path, non-X abilities); effects treat null as 0.
    /// </summary>
    public int? ChosenX { get; init; }

    /// <summary>
    /// CR 700.2d / CR 601.2b — the mode index(es) chosen for a MODAL triggered
    /// ability ("choose one —" / "choose two —"), threaded from
    /// <see cref="TriggeredAbility.ChosenModes"/> by
    /// <see cref="TriggeredAbility.ResolveAsync"/>. The engine prompts the
    /// controller's agent for the mode at STACK-ENTRY time (Rule 603.3, in
    /// <see cref="TriggerManager.PutPendingTriggersOnStackAsync"/>) and records
    /// it on the stack object the way <see cref="ChosenTargets"/> and
    /// <see cref="ChosenX"/> already are — so a modal ETB effect body reads the
    /// real agent-chosen mode off the live context instead of a factory-captured
    /// closure. This is the "true agent-driven mode prompt" the v1 modal-ETB
    /// factories (Knight of Autumn, Charming Prince) deferred.
    /// <para>
    /// Convenience accessor <see cref="ChosenMode"/> returns the first chosen
    /// mode (the "choose one" common case). Null when no mode was recorded (the
    /// spell path, the legacy sync path, a non-modal trigger, or the no-agent
    /// dispatcher path); effect bodies fall back to a factory default in that
    /// case.
    /// </para>
    /// </summary>
    public IReadOnlyList<int>? ChosenModes { get; init; }

    /// <summary>
    /// Convenience: the FIRST chosen mode index (CR 700.2d "choose one"), or
    /// null when no mode was recorded. See <see cref="ChosenModes"/>.
    /// </summary>
    public int? ChosenMode => ChosenModes is { Count: > 0 } m ? m[0] : (int?)null;

    /// <summary>
    /// CR 601.2d / CR 119.4 — the per-target damage split the controller's
    /// agent announced for a "deals N damage divided as you choose among …"
    /// TRIGGERED ability (Inferno Titan's enters-or-attacks trigger, Fury's
    /// ETB), threaded from <see cref="TriggeredAbility.ChosenDamageDivision"/>
    /// by <see cref="TriggeredAbility.ResolveAsync"/>. The engine prompts the
    /// controller's agent for the split at STACK-ENTRY time (Rule 603.3, in
    /// <see cref="TriggerManager.PutPendingTriggersOnStackAsync"/>) and records
    /// it on the stack object the way <see cref="ChosenTargets"/> /
    /// <see cref="ChosenModes"/> already are — so a divided-damage trigger
    /// effect deals the announced amounts (per chosen target) off the live
    /// context instead of a captured even-split closure. This is the
    /// triggered/activated-dispatch analogue of the spell path's
    /// <see cref="ChosenSpellParams.DamageDivision"/>.
    /// <para>
    /// Null on the spell path (use <see cref="ChosenSpellParams.DamageDivision"/>),
    /// the legacy sync path, a non-divided trigger, or the no-agent dispatcher
    /// path; effect bodies fall back to an even split in that case.
    /// </para>
    /// </summary>
    public IReadOnlyList<Game.DamageAllocation>? DamageDivision { get; init; }

    /// <summary>
    /// Non-null view of <see cref="DamageDivision"/> — empty when the resolving
    /// trigger announced no divided-damage split. See CR 601.2d.
    /// </summary>
    public IReadOnlyList<Game.DamageAllocation> DamageDivisionOrEmpty =>
        DamageDivision ?? Array.Empty<Game.DamageAllocation>();

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
        CancellationToken ct = default,
        Permanent? source = null,
        int? chosenX = null,
        ICard? sourceCard = null)
    {
        var targets = chosenTargets ?? EmptyTargets;
        return new(controller, agent, game, targets, ct)
        {
            SharedSlotControllers = SnapshotSharedSlotControllers(targets),
            Source = source,
            ChosenX = chosenX,
            SourceCard = sourceCard,
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
