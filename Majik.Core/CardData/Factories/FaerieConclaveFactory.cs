using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Faerie Conclave (Urza's Legacy / many reprints).
///
/// Land. Oracle text:
///   "Faerie Conclave enters tapped.
///    {T}: Add {U}.
///    {1}{U}: Faerie Conclave becomes a 1/1 blue Faerie creature with
///    flying until end of turn. It's still a land."
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes, no supertype).
/// - <b>Unconditional ETB-tapped (CR 614.1c)</b> — registered as an
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Single-arg dispatcher path omits the
///   replacement (land enters untapped in that posture, mirrors every
///   other always-tapped factory — Creeping Tar Pit / Valakut / Geralf's
///   Messenger / Sunscorched Desert).
/// - <b>{T}: Add {U}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1,
///   no stack).
/// - <b>{1}{U}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{1}{U}</c>. Resolution registers two end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/>:
///     - Layer 4 (<see cref="FaerieConclaveAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/> + <see cref="CardSubtype.Faerie"/>
///       subtype and a Flying keyword marker. The printed Land type stays
///       (CR 613.1c — "It's still a land").
///     - Layer 7b (<see cref="FaerieConclaveBecomesPTEffect"/>) — set-base
///       P/T 1/1 (CR 613.7b). Mirrors the
///       <see cref="MutavaultBecomesPTEffect"/> / <see cref="CreepingTarPitBecomesPTEffect"/>
///       / <see cref="HiveOfTheEyeTyrantBecomesPTEffect"/> pattern:
///       Faerie Conclave is a <see cref="Land"/> runtime instance, so the
///       P/T is recorded for inspection but does not surface through
///       <see cref="ContinuousEffectsService.Compute(Permanent)"/> yet.
///   Both effects carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///   = true so <see cref="ContinuousEffectsService.ExpireEndOfTurn"/>
///   (CR 514.2 cleanup step) lifts the animation.
///
/// ## Deferred (v1 gaps)
/// - <b>Blue colour identity of the animated form</b> — same gap as
///   Creeping Tar Pit / Hive of the Eye Tyrant: the engine's colour layer
///   (Layer 5) has no colour-setting effect primitive yet. The Faerie body
///   should be blue while animated; v1 records the intent but doesn't apply.
/// - <b>Combat math through Compute</b>: same gap as every other manland
///   — until <see cref="ContinuousEffectsService.Compute(Permanent)"/>
///   upgrades to a <see cref="CreatureCharacteristics"/> row when Layer 4
///   grants Creature type, the 1/1 doesn't surface for combat resolution.
///   Flying still gates blocking legality via the keyword set.
/// </summary>
[CardName("Faerie Conclave")]
public static class FaerieConclaveFactory
{
    public const string CardName = "Faerie Conclave";

    /// <summary>
    /// Construct Faerie Conclave with no <see cref="ContinuousEffectsService"/>
    /// or <see cref="ReplacementBus"/> wired. The mana ability + animate
    /// ActivatedAbility are attached; the animate effect-registration step
    /// is gated on a non-null effects service, and the ETB-tapped
    /// replacement is omitted (single-arg shape-only path).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct Faerie Conclave.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the always-enters-tapped
    /// restriction (CR 614.1c). May be null — land enters untapped in that
    /// posture (mirrors how every other always-tapped factory defers this
    /// to the production binder).</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic land, no supertype, no printed subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB-tapped restriction (CR 614.1c) — "Faerie Conclave enters
        // tapped." Unconditional; no gate.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {U}
        // CR 605.1 — mana ability, no stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("U")));

        // ----------------------------------------------------------------
        // {1}{U}: Until end of turn, Faerie Conclave becomes a 1/1 blue
        // Faerie creature with flying. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {1}{U}, no tap rider. Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes 1/1 blue Faerie creature with flying until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Faerie subtype + Flying
                // keyword. Printed Land type stays.
                effects.Register(new FaerieConclaveAnimateEffect(land));

                // Layer 7b — set base P/T 1/1.
                effects.Register(new FaerieConclaveBecomesPTEffect(land, 1, 1));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{U}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}

/// <summary>
/// Faerie Conclave — activated-ability resolution: Layer 4 type-adding
/// effect. Adds <see cref="CardType.Creature"/> to the permanent's
/// effective types, grants <see cref="CardSubtype.Faerie"/> subtype, and
/// adds a Flying keyword marker. Expires at end of turn (CR 514.2).
///
/// CR 613.1c — types and subtypes are added on top of printed values;
/// Faerie Conclave's printed <see cref="CardType.Land"/> remains intact,
/// matching the oracle's "It's still a land" rider.
/// </summary>
public sealed class FaerieConclaveAnimateEffect : ContinuousEffect
{
    private readonly Permanent _target;

    public FaerieConclaveAnimateEffect(Permanent target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>The permanent being animated.</summary>
    public Permanent Target => _target;

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
        chars.Subtypes.Add(CardSubtype.Faerie);
        chars.Keywords.Add("Flying");
    }
}

/// <summary>
/// Faerie Conclave — activated-ability resolution: Layer 7b set-base P/T
/// effect recording the 1/1 the manland turns into. Same "Land runtime
/// instance" shim posture as
/// <see cref="MutavaultBecomesPTEffect"/> /
/// <see cref="CreepingTarPitBecomesPTEffect"/> /
/// <see cref="HiveOfTheEyeTyrantBecomesPTEffect"/>:
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a
/// plain <see cref="PermanentCharacteristics"/> with no P/T fields, so
/// the effect is registered for layer-system correctness and exposes
/// <see cref="NewPower"/> / <see cref="NewToughness"/> for inspection
/// until Compute upgrades the chars row when Layer 4 grants Creature type.
/// </summary>
public sealed class FaerieConclaveBecomesPTEffect : ContinuousEffect
{
    private readonly Permanent _target;

    /// <summary>The base power the target becomes (CR 613.7b).</summary>
    public int NewPower { get; }

    /// <summary>The base toughness the target becomes (CR 613.7b).</summary>
    public int NewToughness { get; }

    public FaerieConclaveBecomesPTEffect(Permanent target, int power, int toughness)
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
        // Layer 7b on a non-Creature row is observationally a no-op in the
        // current pipeline. See class xmldoc.
    }
}
