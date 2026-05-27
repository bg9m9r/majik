using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;

namespace Majik.Core.CardData.Factories;

// =========================================================================
// Shared continuous-effect primitives for the Worldwake / Battle for
// Zendikar / Oath of the Gatewatch "manland" cycle (Celestial Colonnade,
// Stirring Wildwood, Lumbering Falls, Shambling Vent, Needle Spires,
// Hissing Quagmire). Mirrors the per-card shape used by
// CreepingTarPitFactory + FaerieConclaveFactory + LavaclawReachesFactory
// (each of which keeps its own per-card effect classes for historical
// reasons), but consolidated here for the new cycle to avoid 18 nearly
// identical effect classes.
//
// Each factory in the cycle registers:
//   - ManlandCycleAnimateEffect  — Layer 4: add Creature type + Elemental
//                                  subtype + a fixed set of keyword grants.
//                                  Printed Land type stays
//                                  (CR 613.1c — "It's still a land").
//   - ManlandCycleBecomesPTEffect — Layer 7b: set base P/T to the printed
//                                  animated body.
// Both are flagged ExpiresAtEndOfTurn so ContinuousEffectsService.
// ExpireEndOfTurn (CR 514.2 cleanup step) lifts the animation.
//
// Same v1 caveats as the rest of the manland cycle:
//   - Colour identity layer (Layer 5) is not yet implemented, so the
//     "white and blue" / "black and green" / etc. text is recorded only
//     in the factory's effect-name string. Keyword grants and P/T still
//     apply via Compute().
//   - ContinuousEffectsService.Compute(Permanent) seeds a plain
//     PermanentCharacteristics row with no P/T fields for a Land runtime
//     instance, so the P/T is recorded for inspection but doesn't surface
//     through Compute() yet (matches the Mutavault / Creeping Tar Pit /
//     Faerie Conclave / Hive of the Eye Tyrant shim posture).
// =========================================================================

/// <summary>
/// Manland cycle — activated-ability resolution: Layer 4 type-adding
/// effect. Adds <see cref="CardType.Creature"/>,
/// <see cref="CardSubtype.Elemental"/>, and a fixed set of keyword grants
/// (e.g. "Flying", "Vigilance", "Reach", "Hexproof", "Lifelink",
/// "Double Strike", "Deathtouch") to the land's effective characteristics.
/// Expires at end of turn (CR 514.2).
///
/// CR 613.1c — types and subtypes are added on top of printed values;
/// the printed <see cref="CardType.Land"/> remains intact, matching the
/// oracle's "It's still a land" rider.
/// </summary>
public sealed class ManlandCycleAnimateEffect : ContinuousEffect
{
    private readonly Permanent _target;
    private readonly string[] _keywords;
    private readonly CardSubtype[] _subtypes;
    private readonly CardType[] _extraTypes;

    public ManlandCycleAnimateEffect(Permanent target, IEnumerable<string> keywords)
        : this(target, keywords, subtypes: null, extraTypes: null)
    {
    }

    /// <summary>
    /// Construct an animate effect with non-default subtype / extra type
    /// grants. Used by manlands that animate to a non-Elemental body (e.g.
    /// Crawling Barrens — Construct, Mishra's Factory — Assembly-Worker)
    /// or that also gain an extra type (e.g. Mishra's Factory's "artifact
    /// creature" requires Artifact in addition to Creature). When
    /// <paramref name="subtypes"/> is null the default
    /// <see cref="CardSubtype.Elemental"/> is granted (cycle default).
    /// When <paramref name="extraTypes"/> is null only Creature is added;
    /// both are additive on top of the printed Land (CR 613.1c — "It's
    /// still a land").
    /// </summary>
    public ManlandCycleAnimateEffect(
        Permanent target,
        IEnumerable<string> keywords,
        IEnumerable<CardSubtype>? subtypes,
        IEnumerable<CardType>? extraTypes)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _keywords = (keywords ?? throw new ArgumentNullException(nameof(keywords))).ToArray();
        _subtypes = subtypes != null
            ? subtypes.ToArray()
            : new[] { CardSubtype.Elemental };
        _extraTypes = extraTypes != null
            ? extraTypes.ToArray()
            : Array.Empty<CardType>();
    }

    /// <summary>The permanent being animated.</summary>
    public Permanent Target => _target;

    /// <summary>Keyword strings granted by the animation (e.g. "Flying").</summary>
    public IReadOnlyList<string> Keywords => _keywords;

    /// <summary>Subtypes granted by the animation (default: Elemental).</summary>
    public IReadOnlyList<CardSubtype> Subtypes => _subtypes;

    /// <summary>Extra card types granted on top of Creature (e.g. Artifact).</summary>
    public IReadOnlyList<CardType> ExtraTypes => _extraTypes;

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _target;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool ExpiresAtEndOfTurn => true;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        chars.Types.Add(CardType.Creature);
        foreach (var t in _extraTypes)
        {
            chars.Types.Add(t);
        }
        foreach (var st in _subtypes)
        {
            chars.Subtypes.Add(st);
        }
        foreach (var k in _keywords)
        {
            chars.Keywords.Add(k);
        }
    }
}

/// <summary>
/// Manland cycle — activated-ability resolution: Layer 7b set-base P/T
/// effect recording the body the manland turns into. Same "Land runtime
/// instance" shim posture as <see cref="CreepingTarPitBecomesPTEffect"/>
/// / <see cref="FaerieConclaveBecomesPTEffect"/> /
/// <see cref="HiveOfTheEyeTyrantBecomesPTEffect"/>:
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a
/// plain <see cref="PermanentCharacteristics"/> with no P/T fields, so
/// the effect is registered for layer-system correctness and exposes
/// <see cref="NewPower"/> / <see cref="NewToughness"/> for inspection
/// until Compute() upgrades the chars row when Layer 4 grants Creature
/// type.
/// </summary>
public sealed class ManlandCycleBecomesPTEffect : ContinuousEffect
{
    private readonly Permanent _target;

    /// <summary>The base power the target becomes (CR 613.7b).</summary>
    public int NewPower { get; }

    /// <summary>The base toughness the target becomes (CR 613.7b).</summary>
    public int NewToughness { get; }

    public ManlandCycleBecomesPTEffect(Permanent target, int power, int toughness)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        NewPower = power;
        NewToughness = toughness;
    }

    public override Layer Layer => Layer.PT_SetBase;

    public override Permanent? Source => _target;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool ExpiresAtEndOfTurn => true;

    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);

    public override bool AppliesTo(Permanent permanent) => ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power = NewPower;
        chars.Toughness = NewToughness;
    }

    public override void Apply(PermanentCharacteristics chars)
    {
        // Layer 7b on a non-Creature row is observationally a no-op in
        // the current pipeline. See class xmldoc.
    }
}
