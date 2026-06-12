using System.Text.Json.Serialization;

namespace Majik.Core.Api.BotReplay;

/// <summary>
/// The 15 primitive <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
/// decision kinds a bot seat answers in-engine. Interface DEFAULT methods
/// (ChooseFromHand / ChooseFromRevealed / ChooseModes / …) funnel into these
/// primitives, so recording the primitives records the whole prompt surface.
/// <c>CodecCoverageTripwireTests</c> pins this enum against the
/// <c>BotPlayerAgent</c> override list.
/// </summary>
public enum BotDecisionKind
{
    Priority,
    Mulligan,
    CardsToBottom,
    Targets,
    X,
    Mode,
    TriggerOrder,
    ManaSources,
    Attackers,
    Blockers,
    Scry,
    Surveil,
    LibraryPick,
    YesNo,
    Choose,
}

/// <summary>One recorded bot answer. Payload is kind-specific, Id-level only
/// (InstanceId for cards/permanents, Player.Id for players, scalars verbatim) —
/// never object references; rebinding resolves against the rebuilt facade under
/// the same DeterministicIdScope that minted the original ids.</summary>
public sealed record BotDecisionRecord(int BotSeq, BotDecisionKind Kind, BotDecisionPayload Payload);

/// <summary>
/// Kind-specific payload union. Serialized polymorphically with a string
/// discriminator — the same System.Text.Json wire encoding
/// <see cref="Majik.Core.Api.Commands.GameCommand"/> uses for the durable
/// command log, so every payload shape round-trips through the Mongo store
/// without bespoke BSON class maps.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PassPayload), "pass")]
[JsonDerivedType(typeof(CastSpellPayload), "cast")]
[JsonDerivedType(typeof(PlayLandPayload), "play-land")]
[JsonDerivedType(typeof(ActivateAbilityPayload), "activate")]
[JsonDerivedType(typeof(ActivateLoyaltyAbilityPayload), "loyalty")]
[JsonDerivedType(typeof(ActivateManaAbilityPayload), "mana-ability")]
[JsonDerivedType(typeof(MulliganPayload), "mulligan")]
[JsonDerivedType(typeof(CardsToBottomPayload), "bottom")]
[JsonDerivedType(typeof(TargetsPayload), "targets")]
[JsonDerivedType(typeof(XPayload), "x")]
[JsonDerivedType(typeof(ModePayload), "mode")]
[JsonDerivedType(typeof(TriggerOrderPayload), "trigger-order")]
[JsonDerivedType(typeof(ManaSourcesPayload), "mana-sources")]
[JsonDerivedType(typeof(AttackersPayload), "attackers")]
[JsonDerivedType(typeof(BlockersPayload), "blockers")]
[JsonDerivedType(typeof(ScryPayload), "scry")]
[JsonDerivedType(typeof(SurveilPayload), "surveil")]
[JsonDerivedType(typeof(LibraryPickPayload), "library-pick")]
[JsonDerivedType(typeof(YesNoPayload), "yes-no")]
[JsonDerivedType(typeof(ChoosePayload), "choose")]
public abstract record BotDecisionPayload;

/// <summary>What a <see cref="RefTag"/> element refers to.</summary>
public enum RefKind
{
    /// <summary>A card / permanent — <see cref="RefTag.Id"/> is the InstanceId.</summary>
    Card,

    /// <summary>A player — <see cref="RefTag.Id"/> is <c>Player.Id</c>.</summary>
    Player,

    /// <summary>A boxed int scalar (e.g. a mode index candidate).</summary>
    Int,

    /// <summary>A boxed bool scalar (e.g. the YesNo "true" sentinel).</summary>
    Bool,

    /// <summary>A string scalar candidate.</summary>
    String,
}

/// <summary>
/// Tagged union for one target / choice element: cards and players go by Id,
/// scalar candidates verbatim. Decoding resolves Ids by dictionary lookup
/// against the rebuilt facade (the <c>RebindForReplay</c> precedent).
/// </summary>
public sealed record RefTag(
    RefKind Kind,
    Guid Id = default,
    int IntValue = 0,
    bool BoolValue = false,
    string? StringValue = null);

/// <summary>
/// CR 118.9 — Id/scalar descriptor of an elected alternative cost. Only the
/// concrete types the bot emits today are encodable ("exile-cast"); any other
/// concrete <see cref="Majik.Core.Costs.IAlternativeCost"/> throws
/// <see cref="UnsupportedBotDecisionException"/> at ENCODE time (a logged
/// degrade — never a corrupt record).
/// </summary>
public sealed record AltCostDescriptor(string Type, string Description, string ManaCost);

/// <summary>
/// CR 601.2f — Id/scalar descriptor of an additional-cost rider. Only the
/// concrete types the bot emits today are encodable ("multikicker"); others
/// throw <see cref="UnsupportedBotDecisionException"/> at encode time.
/// </summary>
public sealed record AdditionalCostDescriptor(string Type, Guid CardId, string ManaCost, int Times);

// ---------------------------------------------------------------------------
// Priority (one payload per PriorityAction case)
// ---------------------------------------------------------------------------

/// <summary>Pass priority.</summary>
public sealed record PassPayload : BotDecisionPayload;

/// <summary>Cast a spell (card by InstanceId; targets as tags; alt/additional
/// costs as descriptors).</summary>
public sealed record CastSpellPayload(
    Guid CardId,
    IReadOnlyList<RefTag> Targets,
    bool HoldPriority,
    AltCostDescriptor? AlternativeCost,
    IReadOnlyList<AdditionalCostDescriptor>? AdditionalCosts) : BotDecisionPayload;

/// <summary>Play a land from hand.</summary>
public sealed record PlayLandPayload(Guid LandId, bool HoldPriority) : BotDecisionPayload;

/// <summary>Activate a non-mana activated ability — the ability's own
/// deterministic Id plus its source card for a fallback lookup.</summary>
public sealed record ActivateAbilityPayload(
    Guid AbilityId,
    Guid SourceCardId,
    IReadOnlyList<RefTag> Targets,
    bool HoldPriority) : BotDecisionPayload;

/// <summary>Activate a loyalty ability. <see cref="LoyaltyAbility"/> ids are
/// NOT deterministic (Guid.NewGuid), so the ability is addressed positionally:
/// source card InstanceId + index within the card's loyalty-ability list.</summary>
public sealed record ActivateLoyaltyAbilityPayload(
    Guid SourceCardId,
    int AbilityIndex,
    IReadOnlyList<RefTag> Targets,
    bool HoldPriority) : BotDecisionPayload;

/// <summary>Activate a mana ability — source card InstanceId + index within
/// the source's mana-ability list.</summary>
public sealed record ActivateManaAbilityPayload(Guid SourceCardId, int AbilityIndex) : BotDecisionPayload;

// ---------------------------------------------------------------------------
// Remaining kinds
// ---------------------------------------------------------------------------

public sealed record MulliganPayload(bool Keep) : BotDecisionPayload;

public sealed record CardsToBottomPayload(IReadOnlyList<Guid> CardIds) : BotDecisionPayload;

public sealed record TargetsPayload(IReadOnlyList<RefTag> Targets) : BotDecisionPayload;

public sealed record XPayload(int X) : BotDecisionPayload;

public sealed record ModePayload(int ModeIndex) : BotDecisionPayload;

/// <summary>Trigger order by the abilities' deterministic Ids (resolved
/// against the presented list at replay time).</summary>
public sealed record TriggerOrderPayload(IReadOnlyList<Guid> AbilityIds) : BotDecisionPayload;

public sealed record ManaSourcesPayload(IReadOnlyList<Guid> SourceIds, bool IsCancelled) : BotDecisionPayload;

public sealed record AttackerPair(Guid AttackerId, RefTag Defender);

public sealed record AttackersPayload(IReadOnlyList<AttackerPair> Attackers) : BotDecisionPayload;

public sealed record BlockerPair(Guid BlockerId, Guid AttackerId);

public sealed record BlockersPayload(IReadOnlyList<BlockerPair> Pairs) : BotDecisionPayload;

public sealed record ScryPayload(IReadOnlyList<Guid> ToBottom, IReadOnlyList<Guid> TopOrder) : BotDecisionPayload;

public sealed record SurveilPayload(IReadOnlyList<Guid> ToGraveyard, IReadOnlyList<Guid> TopOrder) : BotDecisionPayload;

public sealed record LibraryPickPayload(Guid? SelectedId) : BotDecisionPayload;

public sealed record YesNoPayload(bool Answer) : BotDecisionPayload;

public sealed record ChoosePayload(IReadOnlyList<RefTag> Selected) : BotDecisionPayload;

/// <summary>
/// Thrown at ENCODE time when a bot answer carries a shape the codec
/// deliberately does not cover yet (an exotic alternative/additional cost
/// type, an unknown choice-candidate type). The recording layer treats this
/// as a logged degrade: the decision is not recorded (a later rehydrate
/// stops gracefully) but the LIVE game continues unharmed.
/// </summary>
public sealed class UnsupportedBotDecisionException : InvalidOperationException
{
    public UnsupportedBotDecisionException(string message) : base(message) { }
}
