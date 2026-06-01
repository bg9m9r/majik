using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.1c / 613.7b — generic "animate a land (or other permanent) into a
/// creature" continuous effect. The manland / Earthbend primitive.
///
/// <para>Registered against a
/// <see cref="ContinuousEffectsService"/>, it makes <c>target</c> ALSO a
/// creature while staying whatever it printed as (CR 701.59a — "that's still
/// a land"):</para>
/// <list type="bullet">
///   <item>Layer 4 (<see cref="Layer.Type"/>) — adds
///     <see cref="CardType.Creature"/> plus an optional creature subtype
///     (e.g. <see cref="CardSubtype.Elemental"/>) to the effective type set.
///     The printed Land/Artifact type is left intact.</item>
///   <item>Layer 7b (<see cref="Layer.PT_SetBase"/>) — sets base P/T to
///     <see cref="BasePower"/>/<see cref="BaseToughness"/> (default 0/0 for
///     Earthbend). The Layer-4 grant triggers
///     <see cref="ContinuousEffectsService.Compute(Permanent)"/>'s
///     creature-row upgrade, so this set-base actually lands on a creature
///     row and the P/T surfaces through combat math. +1/+1 counters layer on
///     top at 7c (Earthbend N → an N/N).</item>
///   <item>Layer 6 (<see cref="Layer.Abilities"/>) — optionally grants Haste
///     (CR 702.10 / 701.59a) to the effective keyword set.</item>
/// </list>
///
/// <para>A single registered instance applies all three sublayers: the
/// service groups effects by <see cref="Layer"/>, and each
/// <see cref="ContinuousEffect"/> reports exactly one layer. So we register
/// three lightweight sub-effects sharing the same target/lifetime. This
/// class is the Layer-4 head; <see cref="Register"/> wires the matching 7b
/// and (optional) 6 companions through the service in one call.</para>
///
/// <para>Lifetime: by default the animation is permanent while the target is
/// on the battlefield (Earthbend has no duration — CR 701.59). Pass
/// <paramref name="expiresAtEndOfTurn"/> = true for the until-EOT manland
/// activations (Creeping Tar Pit style) — though those keep their bespoke
/// effect classes for their colour/shroud riders.</para>
/// </summary>
public sealed class AnimateLandEffect : ContinuousEffect
{
    private readonly Permanent _target;
    private readonly CardSubtype? _subtype;
    private readonly bool _expiresAtEndOfTurn;

    /// <summary>Base power the animated permanent becomes (CR 613.7b).</summary>
    public int BasePower { get; }

    /// <summary>Base toughness the animated permanent becomes (CR 613.7b).</summary>
    public int BaseToughness { get; }

    /// <summary>True if the animation also grants Haste (CR 702.10).</summary>
    public bool GrantsHaste { get; }

    private AnimateLandEffect(
        Permanent target,
        CardSubtype? subtype,
        int basePower,
        int baseToughness,
        bool grantsHaste,
        bool expiresAtEndOfTurn)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _subtype = subtype;
        BasePower = basePower;
        BaseToughness = baseToughness;
        GrantsHaste = grantsHaste;
        _expiresAtEndOfTurn = expiresAtEndOfTurn;
    }

    /// <summary>The permanent being animated.</summary>
    public Permanent Target => _target;

    // Layer-4 head (type grant).
    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _target;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool ExpiresAtEndOfTurn => _expiresAtEndOfTurn;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        // Layer 4 — add Creature type (and optional subtype). Printed type
        // stays ("still a land", CR 701.59a).
        chars.Types.Add(CardType.Creature);
        if (_subtype is { } st) chars.Subtypes.Add(st);
    }

    /// <summary>
    /// Register a complete animate-land effect on <paramref name="service"/>:
    /// the Layer-4 type grant (this class), a Layer-7b set-base P/T companion,
    /// and an optional Layer-6 Haste-grant companion. All three share the
    /// target and lifetime. Returns the Layer-4 head for inspection / later
    /// unregister.
    /// </summary>
    public static AnimateLandEffect Register(
        ContinuousEffectsService service,
        Permanent target,
        CardSubtype? subtype,
        int basePower,
        int baseToughness,
        bool grantsHaste,
        bool expiresAtEndOfTurn = false)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(target);

        var head = new AnimateLandEffect(
            target, subtype, basePower, baseToughness, grantsHaste, expiresAtEndOfTurn);
        service.Register(head);
        service.Register(new AnimateLandSetPTEffect(target, basePower, baseToughness, expiresAtEndOfTurn));
        if (grantsHaste)
        {
            service.Register(new AnimateLandKeywordEffect(target, "Haste", expiresAtEndOfTurn));
        }
        return head;
    }
}

/// <summary>
/// CR 613.7b — Layer-7b set-base P/T companion of <see cref="AnimateLandEffect"/>.
/// Sets the animated permanent's base P/T. Lands on a creature row because the
/// paired Layer-4 grant drives the
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> creature-row
/// upgrade.
/// </summary>
public sealed class AnimateLandSetPTEffect : ContinuousEffect
{
    private readonly Permanent _target;
    private readonly bool _expiresAtEndOfTurn;

    public int NewPower { get; }
    public int NewToughness { get; }

    public AnimateLandSetPTEffect(Permanent target, int power, int toughness, bool expiresAtEndOfTurn = false)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        NewPower = power;
        NewToughness = toughness;
        _expiresAtEndOfTurn = expiresAtEndOfTurn;
    }

    public override Layer Layer => Layer.PT_SetBase;
    public override Permanent? Source => _target;
    public override bool IsActive() => _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;
    public override bool ExpiresAtEndOfTurn => _expiresAtEndOfTurn;
    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);
    public override bool AppliesTo(Permanent permanent) => ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power = NewPower;
        chars.Toughness = NewToughness;
    }
}

/// <summary>
/// CR 613.1f — Layer-6 keyword-grant companion of <see cref="AnimateLandEffect"/>.
/// Adds a keyword (Haste) to the animated permanent's effective keyword set so
/// it can attack the turn it is animated.
/// </summary>
public sealed class AnimateLandKeywordEffect : ContinuousEffect
{
    private readonly Permanent _target;
    private readonly string _keyword;
    private readonly bool _expiresAtEndOfTurn;

    public string Keyword => _keyword;

    public AnimateLandKeywordEffect(Permanent target, string keyword, bool expiresAtEndOfTurn = false)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _keyword = keyword ?? throw new ArgumentNullException(nameof(keyword));
        _expiresAtEndOfTurn = expiresAtEndOfTurn;
    }

    public override Layer Layer => Layer.Abilities;
    public override Permanent? Source => _target;
    public override bool IsActive() => _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;
    public override bool ExpiresAtEndOfTurn => _expiresAtEndOfTurn;
    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);
    public override bool AppliesTo(Permanent permanent) => ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars) => Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        chars.Keywords.Add(_keyword);
    }
}
