using System.Text.Json.Serialization;

namespace Majik.Core.Api.Commands;

/// <summary>
/// Wire-format command submitted by a player. Polymorphism uses
/// System.Text.Json's <see cref="JsonDerivedTypeAttribute"/> with a string
/// discriminator ("$type"). Concrete commands map 1:1 to choice categories
/// in <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PassPriorityCommand), "pass")]
[JsonDerivedType(typeof(PlayLandCommand), "play-land")]
[JsonDerivedType(typeof(CastSpellCommand), "cast")]
[JsonDerivedType(typeof(MulliganCommand), "mulligan")]
[JsonDerivedType(typeof(ChooseTargetsCommand), "targets")]
[JsonDerivedType(typeof(ChooseXCommand), "x")]
[JsonDerivedType(typeof(ChooseModeCommand), "mode")]
[JsonDerivedType(typeof(ChooseManaCommand), "mana")]
[JsonDerivedType(typeof(ActivateManaAbilityCommand), "activateManaAbility")]
[JsonDerivedType(typeof(OrderTriggersCommand), "order-triggers")]
[JsonDerivedType(typeof(DeclareAttackersCommand), "attackers")]
[JsonDerivedType(typeof(DeclareBlockersCommand), "blockers")]
[JsonDerivedType(typeof(ChooseCardsToBottomCommand), "bottom")]
public abstract record GameCommand
{
    /// <summary>The player who submitted the command.</summary>
    public Guid PlayerId { get; init; }
}

public sealed record PassPriorityCommand : GameCommand;

public sealed record PlayLandCommand(Guid LandInstanceId) : GameCommand;

public sealed record CastSpellCommand(
    Guid CardInstanceId,
    IReadOnlyList<Guid> TargetInstanceIds,
    int? XValue,
    int? ModeIndex) : GameCommand;

public sealed record MulliganCommand(bool Keep) : GameCommand;

public sealed record ChooseTargetsCommand(IReadOnlyList<Guid> TargetInstanceIds) : GameCommand;

public sealed record ChooseXCommand(int X) : GameCommand;

public sealed record ChooseModeCommand(int ModeIndex) : GameCommand;

public sealed record ChooseManaCommand(IReadOnlyList<Guid> SourceInstanceIds) : GameCommand;

/// <summary>
/// Activate a mana ability of a permanent the player controls (CR 605.3a —
/// mana abilities don't use the stack and don't pass priority). The same
/// player keeps priority after activation. <see cref="Color"/> selects
/// which mana ability for multi-colour sources (e.g. Overgrown Tomb's
/// {B} vs {G}); valid values are "W", "U", "B", "R", "G", "C". Empty
/// string is allowed for sources with exactly one mana ability.
/// </summary>
public sealed record ActivateManaAbilityCommand(Guid PermanentInstanceId, string Color) : GameCommand;

public sealed record OrderTriggersCommand(IReadOnlyList<Guid> StackObjectIdsInOrder) : GameCommand;

public sealed record DeclareAttackersCommand(
    IReadOnlyList<AttackerDeclarationDto> Attackers) : GameCommand;

public sealed record AttackerDeclarationDto(Guid AttackerInstanceId, Guid DefenderId);

public sealed record DeclareBlockersCommand(
    IReadOnlyList<BlockerDeclarationDto> Blockers) : GameCommand;

public sealed record BlockerDeclarationDto(Guid BlockerInstanceId, Guid AttackerInstanceId);

public sealed record ChooseCardsToBottomCommand(IReadOnlyList<Guid> CardInstanceIds) : GameCommand;
