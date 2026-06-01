using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Treetop Village (Urza's Legacy / many reprints).
///
/// Land. Oracle text:
///   "Treetop Village enters tapped.
///    {T}: Add {G}.
///    {1}{G}: Treetop Village becomes a 3/3 green Ape creature with
///    trample until end of turn. It's still a land."
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes, no supertype).
/// - <b>Unconditional ETB-tapped (CR 614.1c)</b> — registered as an
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Single-arg dispatcher path omits the
///   replacement (mirrors every other always-tapped factory).
/// - <b>{T}: Add {G}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{1}{G}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{1}{G}</c>. Resolution registers two end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/>:
///     - Layer 4 (<see cref="TreetopVillageAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/> + <see cref="CardSubtype.Ape"/>
///       subtype and a Trample keyword marker. Printed Land type stays
///       (CR 613.1c — "It's still a land").
///     - Layer 7b (<see cref="TreetopVillageBecomesPTEffect"/>) — set-base
///       P/T 3/3 (CR 613.7b). Same "Land runtime instance" shim posture
///       as every other manland; Compute(Permanent) seeds a plain
///       PermanentCharacteristics, so the P/T is recorded for inspection
///       but doesn't surface through Compute yet.
///
/// ## Deferred (v1 gaps)
/// - Green colour identity of the animated form (no Layer 5 colour-set
///   primitive yet).
/// - Combat math through Compute — same gap as every other manland.
///   Trample still functions as a keyword set entry consumed by the
///   combat-damage assignment logic when present.
/// - Summoning sickness (CR 302.1) — fresh-Creature bookkeeping not yet
///   modelled.
/// </summary>
[CardName("Treetop Village")]
public static class TreetopVillageFactory
{
    public const string CardName = "Treetop Village";

    /// <summary>
    /// Construct Treetop Village with no <see cref="ContinuousEffectsService"/>
    /// or <see cref="ReplacementBus"/> wired. The mana ability + animate
    /// ActivatedAbility are attached; the animate effect-registration step
    /// is gated on a non-null effects service, and the ETB-tapped
    /// replacement is omitted (single-arg shape-only path).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct Treetop Village.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the always-enters-tapped
    /// restriction (CR 614.1c). May be null.</param>
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
        // ETB-tapped restriction (CR 614.1c) — "Treetop Village enters
        // tapped." Unconditional; no gate.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {G}
        // CR 605.1 — mana ability, no stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("G")));

        // ----------------------------------------------------------------
        // {1}{G}: Until end of turn, Treetop Village becomes a 3/3 green
        // Ape creature with trample. It's still a land.
        //
        // CR 602 — ordinary activated ability. Cost = {1}{G}, no tap rider.
        // Resolution registers Layer 4 + Layer 7b continuous effects
        // flagged ExpiresAtEndOfTurn.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes 3/3 green Ape creature with trample until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature + Ape subtype + Trample keyword.
                effects.Register(new TreetopVillageAnimateEffect(land));

                // Layer 7b — set base P/T 3/3.
                effects.Register(new TreetopVillageBecomesPTEffect(land, 3, 3));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{G}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}

/// <summary>
/// Treetop Village — activated-ability resolution: Layer 4 type-adding
/// effect. Adds <see cref="CardType.Creature"/> + <see cref="CardSubtype.Ape"/>
/// and a Trample keyword marker. Expires at end of turn (CR 514.2).
///
/// CR 613.1c — types and subtypes are added on top of printed values;
/// the printed <see cref="CardType.Land"/> remains intact, matching the
/// oracle's "It's still a land" rider.
/// </summary>
public sealed class TreetopVillageAnimateEffect : ContinuousEffect
{
    private readonly Permanent _target;

    public TreetopVillageAnimateEffect(Permanent target)
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
        chars.Subtypes.Add(CardSubtype.Ape);
        chars.Keywords.Add("Trample");
    }
}

/// <summary>
/// Treetop Village — activated-ability resolution: Layer 7b set-base P/T
/// effect recording the 3/3 the manland turns into. Same "Land runtime
/// instance" shim posture as the rest of the manland cycle.
/// </summary>
public sealed class TreetopVillageBecomesPTEffect : ContinuousEffect
{
    private readonly Permanent _target;

    /// <summary>The base power the target becomes (CR 613.7b).</summary>
    public int NewPower { get; }

    /// <summary>The base toughness the target becomes (CR 613.7b).</summary>
    public int NewToughness { get; }

    public TreetopVillageBecomesPTEffect(Permanent target, int power, int toughness)
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

    // No Apply(PermanentCharacteristics) override: the base default
    // dispatches to Apply(CreatureCharacteristics) when the working set is a
    // creature row. ContinuousEffectsService.Compute upgrades an animated
    // Land to a creature row (CR 613.1c) on a Layer-4 Creature grant, so this
    // set-base lands and the animated body surfaces through combat math.
}
