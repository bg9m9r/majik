using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Creeping Tar Pit (Worldwake).
///
/// Land. Oracle text:
///   "Creeping Tar Pit enters tapped.
///    {T}: Add {U} or {B}.
///    {1}{U}{B}: Until end of turn, Creeping Tar Pit becomes a 3/2 blue and
///    black Elemental creature that's still a land. It gains shroud until
///    end of turn. (It can't be the target of spells or abilities.)"
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes, no supertype).
/// - <b>ETB-tapped (CR 614.1c)</b> — registered as an
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> when one is provided. Without the bus
///   (single-arg dispatcher path), the land enters untapped — deferred to
///   the production binder layer (mirrors every other always-tapped factory
///   path in this codebase).
/// - <b>{T}: Add {U}</b> and <b>{T}: Add {B}</b> — two
///   <see cref="ManaAbility"/> instances (CR 605.1, no stack).
/// - <b>{1}{U}{B}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{1}{U}{B}</c>. Resolution registers three end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/>:
///     - Layer 4 (<see cref="CreepingTarPitAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/> and
///       <see cref="CardSubtype.Elemental"/> to the land's effective
///       characteristics. The printed Land type is left intact (CR 613.1c
///       — "that's still a land").
///     - Layer 7b (<see cref="CreepingTarPitBecomesPTEffect"/>) — sets
///       base P/T to 3/2 (CR 613.7b). Same "Mutavault on a Land runtime
///       instance" gap: <see cref="ContinuousEffectsService.Compute(Permanent)"/>
///       seeds a <see cref="PermanentCharacteristics"/> row for a Land;
///       the effect records the intended P/T for future Compute upgrades.
///     - Layer 6 (<see cref="CreepingTarPitShroudEffect"/>) — grants
///       "Shroud" to the land's effective keyword set until EOT. Shroud is
///       read back by <see cref="Majik.Core.Targeting.TargetLegality"/>
///       which consults <see cref="ContinuousEffectsService.Compute"/>
///       when a <see cref="ContinuousEffectsService"/> is wired (CR 702.18).
///   All three carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> =
///   true so <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR
///   514.2 cleanup step) lifts the animation.
///
/// ## Deferred (v1 gaps)
/// - Same "combat math through Compute" gap as Mutavault: until
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> upgrades to
///   a <see cref="CreatureCharacteristics"/> row when Layer 4 grants
///   <see cref="CardType.Creature"/>, the 3/2 doesn't surface for combat
///   resolution.
/// - Summoning sickness (CR 302.1) — Creeping Tar Pit has been on the
///   battlefield since the land-play step; activating on a later turn should
///   produce a creature with no summoning sickness. No intricate
///   "had-Creature-continuously-since-untap" bookkeeping yet.
/// - Blue and black colour identity of the animated form — the engine's
///   colour layer (Layer 5) has no colour-setting effect primitive yet.
///   Shroud still functions correctly because it is a Layer 6 keyword grant,
///   independent of colour.
/// </summary>
[CardName("Creeping Tar Pit")]
public static class CreepingTarPitFactory
{
    public const string CardName = "Creeping Tar Pit";

    /// <summary>
    /// Construct Creeping Tar Pit with no <see cref="ContinuousEffectsService"/>
    /// or <see cref="ReplacementBus"/> wired. The mana abilities are wired;
    /// the animate ability's cost still pays but the layer effects are not
    /// registered, and the ETB-tapped replacement is omitted.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct Creeping Tar Pit.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4, Layer 6,
    /// and Layer 7b registration of the animate ability. May be null — the
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
        // ETB-tapped restriction (CR 614.1c) — "Creeping Tar Pit enters
        // tapped." Unconditional; no gate (contrast Valakut's 5-mountain
        // check or shock-land's life payment option).
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {U}  /  {T}: Add {B}
        // CR 605.1 — mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("U")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));

        // ----------------------------------------------------------------
        // {1}{U}{B}: Until end of turn, Creeping Tar Pit becomes a 3/2
        // blue and black Elemental creature that's still a land, and it
        // gains shroud until end of turn.
        //
        // CR 602 — ordinary activated ability (uses the stack).
        // Cost: {1}{U}{B}, no tap.
        // Resolution registers Layer 4 + Layer 6 + Layer 7b continuous
        // effects flagged ExpiresAtEndOfTurn.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes 3/2 Elemental creature until EOT (still a land), gains Shroud until EOT",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type and Elemental subtype (CR 613.1c).
                // Printed Land type stays ("it's still a land").
                effects.Register(new CreepingTarPitAnimateEffect(land));

                // Layer 7b — set base P/T 3/2 (CR 613.7b).
                effects.Register(new CreepingTarPitBecomesPTEffect(land, 3, 2));

                // Layer 6 — grant Shroud keyword until EOT (CR 702.18 / CR 613.1f).
                effects.Register(new CreepingTarPitShroudEffect(land));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{U}{B}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}

/// <summary>
/// Creeping Tar Pit — activated-ability resolution: Layer 4 type-adding
/// effect. Adds <see cref="CardType.Creature"/> and
/// <see cref="CardSubtype.Elemental"/> to the land's effective
/// characteristics. Expires at end of turn (CR 514.2).
///
/// CR 613.1c — types are added on top of printed values; the printed
/// <see cref="CardType.Land"/> remains intact, matching the oracle's
/// "that's still a land" rider.
/// </summary>
public sealed class CreepingTarPitAnimateEffect : ContinuousEffect
{
    private readonly Permanent _target;

    public CreepingTarPitAnimateEffect(Permanent target)
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
        chars.Subtypes.Add(CardSubtype.Elemental);
    }
}

/// <summary>
/// Creeping Tar Pit — activated-ability resolution: Layer 7b set-base P/T
/// effect recording the 3/2 the manland turns into. Mirrors the
/// <see cref="MutavaultBecomesPTEffect"/> pattern: Creeping Tar Pit is a
/// <see cref="Land"/> runtime instance, so
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a
/// plain <see cref="PermanentCharacteristics"/> with no P/T fields. The
/// effect is still registered for layer-system correctness and exposes
/// <see cref="NewPower"/> / <see cref="NewToughness"/> for inspection
/// until <see cref="ContinuousEffectsService.Compute(Permanent)"/> can
/// upgrade the chars row when Layer 4 grants Creature type.
/// </summary>
public sealed class CreepingTarPitBecomesPTEffect : ContinuousEffect
{
    private readonly Permanent _target;

    /// <summary>The base power the target becomes (CR 613.7b).</summary>
    public int NewPower { get; }

    /// <summary>The base toughness the target becomes (CR 613.7b).</summary>
    public int NewToughness { get; }

    public CreepingTarPitBecomesPTEffect(Permanent target, int power, int toughness)
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

    // No Apply(PermanentCharacteristics) override: the base default dispatches
    // to Apply(CreatureCharacteristics) when the working set is a creature row.
    // Now that ContinuousEffectsService.Compute upgrades an animated Land to a
    // creature row (CR 613.1c), this set-base lands correctly and the 3/2
    // surfaces through combat math. (A previous no-op override here swallowed
    // the P/T against the upgraded row — removed.)
}

/// <summary>
/// Creeping Tar Pit — activated-ability resolution: Layer 6 keyword grant.
/// Grants "Shroud" to the land until end of turn (CR 702.18 — it can't be
/// the target of spells or abilities). Expires at end of turn (CR 514.2).
///
/// <see cref="Majik.Core.Targeting.TargetLegality"/> reads Shroud from
/// <see cref="ContinuousEffectsService.Compute"/> when a service is wired,
/// so the grant feeds the targeting legality check automatically.
///
/// Note: the effect targets a <see cref="Permanent"/> (a
/// <see cref="Land"/> instance), not a <see cref="Creature"/>. The
/// <see cref="Apply(PermanentCharacteristics)"/> override adds "Shroud" to
/// the permanent's keyword set; <see cref="Apply(CreatureCharacteristics)"/>
/// delegates to the same body for completeness (Compute may call either
/// overload depending on the chars type it seeds).
/// </summary>
public sealed class CreepingTarPitShroudEffect : ContinuousEffect
{
    private readonly Permanent _target;

    public CreepingTarPitShroudEffect(Permanent target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>The permanent receiving Shroud.</summary>
    public Permanent Target => _target;

    public override Layer Layer => Layer.Abilities;

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
        chars.Keywords.Add("Shroud");
    }
}
