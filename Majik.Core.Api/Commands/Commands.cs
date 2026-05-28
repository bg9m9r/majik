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
[JsonDerivedType(typeof(CancelCastCommand), "cancelCast")]
[JsonDerivedType(typeof(ActivateManaAbilityCommand), "activateManaAbility")]
[JsonDerivedType(typeof(ActivateAbilityCommand), "activateAbility")]
[JsonDerivedType(typeof(OrderTriggersCommand), "order-triggers")]
[JsonDerivedType(typeof(DeclareAttackersCommand), "attackers")]
[JsonDerivedType(typeof(DeclareBlockersCommand), "blockers")]
[JsonDerivedType(typeof(ChooseCardsToBottomCommand), "bottom")]
[JsonDerivedType(typeof(ChooseLibraryPickCommand), "chooseLibraryPick")]
[JsonDerivedType(typeof(ChooseSurveilCommand), "chooseSurveil")]
[JsonDerivedType(typeof(ChooseYesNoCommand), "chooseYesNo")]
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
/// CR 601.2 / CR 727 — bail out of an in-flight cast while the engine is
/// still at the cost-payment step. Only valid as a response to a
/// <see cref="ChooseManaCommand"/> prompt; the engine refunds any
/// pool-deducted mana and returns the spell to the player's hand. No
/// stack push, no <c>SpellCastEvent</c>, no priority change.
/// </summary>
public sealed record CancelCastCommand() : GameCommand;

/// <summary>
/// Activate a mana ability of a permanent the player controls (CR 605.3a —
/// mana abilities don't use the stack and don't pass priority). The same
/// player keeps priority after activation. <see cref="Color"/> selects
/// which mana ability for multi-colour sources (e.g. Overgrown Tomb's
/// {B} vs {G}); valid values are "W", "U", "B", "R", "G", "C". Empty
/// string is allowed for sources with exactly one mana ability.
/// </summary>
public sealed record ActivateManaAbilityCommand(Guid PermanentInstanceId, string Color) : GameCommand;

/// <summary>
/// Activate a non-mana activated ability of a permanent the player controls
/// (CR 602 — costs paid, then the ability goes on the stack). The
/// <see cref="PermanentInstanceId"/> identifies the source; the
/// <see cref="AbilityId"/> is the <see cref="Majik.Core.Stack.IStackObject.Id"/>
/// of the specific <see cref="Majik.Core.Abilities.IActivatedAbility"/> on
/// that permanent (needed when a card has more than one activated ability —
/// e.g. fetchlands carry their {T}, Pay 1 life, Sacrifice ability alongside
/// any printed mana abilities). The engine validates legality (controller,
/// zone, sorcery-speed rider, cost-payability) on submit; this command is
/// the wire shape for the existing <see cref="Majik.Core.Players.Agents.PriorityAction.ActivateAbility"/>
/// dispatch path.
/// </summary>
public sealed record ActivateAbilityCommand(Guid PermanentInstanceId, Guid AbilityId) : GameCommand;

public sealed record OrderTriggersCommand(IReadOnlyList<Guid> StackObjectIdsInOrder) : GameCommand;

public sealed record DeclareAttackersCommand(
    IReadOnlyList<AttackerDeclarationDto> Attackers) : GameCommand;

public sealed record AttackerDeclarationDto(Guid AttackerInstanceId, Guid DefenderId);

public sealed record DeclareBlockersCommand(
    IReadOnlyList<BlockerDeclarationDto> Blockers) : GameCommand;

public sealed record BlockerDeclarationDto(Guid BlockerInstanceId, Guid AttackerInstanceId);

public sealed record ChooseCardsToBottomCommand(IReadOnlyList<Guid> CardInstanceIds) : GameCommand;

/// <summary>
/// CR 701.19a — response to a library-search prompt
/// (<see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseLibraryPickAsync"/>).
/// <see cref="SelectedInstanceId"/> is the <c>InstanceId</c> of the card the
/// player chose from the prompt's candidate list, or <see langword="null"/>
/// to model "find nothing" (legal under CR 701.19a — a player may decline
/// to choose a card from a successful search). The engine rejects
/// instance IDs that aren't in the candidate set with a clear error so the
/// client can never silently smuggle a non-matching pick through.
/// </summary>
public sealed record ChooseLibraryPickCommand(Guid? SelectedInstanceId) : GameCommand;

/// <summary>
/// CR 701.42 — response to a surveil prompt
/// (<see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseSurveilDecisionAsync"/>).
/// The engine peeked N cards from the top of the searching player's library
/// and shipped them in the prompt envelope's <c>SurveilView</c> (in
/// top-to-bottom order). The client partitions those N cards into two
/// disjoint lists:
/// <list type="bullet">
/// <item><see cref="ToGraveyardInstanceIds"/> — the InstanceIds the player
/// chose to send to their graveyard (CR 701.42a).</item>
/// <item><see cref="TopOrderInstanceIds"/> — the remaining peeked cards in
/// the order the player wants them on top of the library, where index 0
/// becomes the new top (CR 701.42b).</item>
/// </list>
/// The engine rejects payloads that don't partition the peeked set exactly
/// once with a clear error so the client can't smuggle a graveyard-bound
/// card back to the top or drop a peeked card on the floor.
/// </summary>
public sealed record ChooseSurveilCommand(
    IReadOnlyList<Guid> ToGraveyardInstanceIds,
    IReadOnlyList<Guid> TopOrderInstanceIds) : GameCommand;

/// <summary>
/// CR 117.x / 605.1 — response to a Yes/No prompt
/// (<see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseYesNoAsync(Majik.Core.Game.GameContext?,string,string?,System.Threading.CancellationToken)"/>).
/// <see cref="Answer"/> is the bool the agent answered with — <c>true</c> to
/// take the optional action ("pay 2 life"), <c>false</c> to decline.
/// Currently produced by the production shock-land binder-chain
/// (<c>ShockLandReplacement</c>); reusable by the same pattern for
/// painlands / slowlands / verge lands / filter lands (deferred — see
/// PR body).
/// </summary>
public sealed record ChooseYesNoCommand(bool Answer) : GameCommand;
