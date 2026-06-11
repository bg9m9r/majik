using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.Simulation;

/// <summary>
/// Result of <see cref="GameStateCloner.Clone"/>: cloned players plus the
/// remap tables the sandbox builder needs to re-link subsystems.
/// </summary>
public sealed class ClonedGame
{
    public required IReadOnlyList<Player> Players { get; init; }
    public required IReadOnlyDictionary<Player, Player> PlayerMap { get; init; }   // original -> clone
    public required IReadOnlyDictionary<Guid, ICard> CardMap { get; init; }        // InstanceId -> cloned card

    /// <summary>
    /// Cloned stack, or null if no live stack was provided to
    /// <see cref="GameStateCloner.Clone"/>. Only <see cref="Majik.Core.Spells.Spell"/>
    /// stack objects are cloned; activated/triggered abilities are not carried over
    /// (see GameStateCloner for the escalation note).
    /// </summary>
    public Majik.Core.Stack.Stack? Stack { get; init; }

    /// <summary>
    /// Cloned per-turn tally, or null if no live TurnState was provided.
    /// </summary>
    public TurnState? TurnState { get; init; }

    /// <summary>
    /// Fresh <see cref="ContinuousEffectsService"/> built for this sandbox,
    /// containing re-registered sim-cloneable effects (e.g.
    /// <see cref="LordStaticEffect"/>) that were active on the live battlefield.
    /// Null when the original board had no live CES (no permanent had
    /// <see cref="Majik.Core.Cards.Permanent.ActiveEffects"/> wired).
    /// All cloned battlefield permanents have their
    /// <see cref="Majik.Core.Cards.Permanent.ActiveEffects"/> pointed at this
    /// service so anthems / lords apply correctly in the search sandbox.
    /// </summary>
    public ContinuousEffectsService? Effects { get; init; }

    public Player PlayerFor(Player original) => PlayerMap[original];
}
