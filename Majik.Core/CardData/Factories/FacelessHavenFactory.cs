using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Faceless Haven (Kaldheim snow manland).
///
/// Oracle text (verified Scryfall 2026-05-29):
///   "{T}: Add {C}.
///    {S}{S}{S}: This land becomes a 4/3 creature with vigilance and all
///    creature types until end of turn. It's still a land. ({S} can be paid
///    with one mana from a snow source.)"
///
/// Type line: Snow Land. Printed P/T: none (it's a land until animated).
///
/// Combines the two existing manland analogue postures:
/// - <see cref="CaveOfTheFrostDragonFactory"/> — snow creature-land that
///   animates via an activated ability whose cost is paid with snow mana;
///   the base shape (plain Snow Land + <c>{T}: Add {C}</c>) is materialised
///   from the embedded JSON definition (<c>faceless-haven.json</c>) and the
///   animate ability is layered on here (the JSON <c>AbilityDefinition</c>
///   schema doesn't express animate effects).
/// - <see cref="MutavaultFactory"/> — "all creature types" + set-base P/T,
///   reusing <see cref="MutavaultAnimateEffect.EveryCreatureType"/> as the
///   single source of truth for the engine's known creature subtypes.
///
/// ## Implemented (v1)
/// - Plain Snow Land identity (no printed subtypes) + <c>{T}: Add {C}</c>
///   mana ability — both from the JSON definition (CR 605.1, mana ability,
///   no stack). Faceless Haven enters untapped unconditionally (no
///   ETB-tapped clause), so no <c>ReplacementBus</c> rider is needed —
///   simpler than the "cave" cycle.
/// - <b>{S}{S}{S}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{S}{S}{S}</c>. Resolution registers two end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/>:
///     - Layer 4 (<see cref="FacelessHavenAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/> to the permanent's effective
///       types, grants every creature subtype the engine models
///       (<see cref="MutavaultAnimateEffect.EveryCreatureType"/>, CR 205.3m
///       "all creature types"), and grants the Vigilance
///       <see cref="StaticAbility"/> keyword marker (CR 702.20). The
///       printed Land type is left intact ("It's still a land", CR 613.1c).
///     - Layer 7b (<see cref="FacelessHavenBecomesPTEffect"/>) — set-base
///       P/T 4/3 (CR 613.7b). Same "Land runtime instance" shim posture as
///       <see cref="MutavaultBecomesPTEffect"/> /
///       <see cref="CaveOfTheFrostDragonBecomesPTEffect"/>.
///   Both effects carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///   = true so the cleanup-step expiry (CR 514.2) lifts the animation.
///
/// ## Snow-mana payment ({S}) — v1 simplification
/// <see cref="ValueObjects.ManaCost.Parse"/> treats <c>{S}</c> as +1 generic
/// (snow-source-restricted payment gating is an engine-wide deferral shared
/// by Skred / Marit Lage's Slumber). The animate cost is therefore three
/// generic mana in v1 — the correct converted cost, with the snow-source
/// restriction not yet enforced. Same posture as every other <c>{S}</c>
/// consumer in the pool.
///
/// ## Deferred (v1 gaps — shared with the existing manlands)
/// - <b>Combat math through Compute</b>: until
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> upgrades to a
///   <see cref="CreatureCharacteristics"/> row when Layer 4 grants
///   <see cref="CardType.Creature"/>, the 4/3 doesn't surface for combat
///   resolution. <see cref="FacelessHavenBecomesPTEffect"/> records the
///   intended P/T for inspection meanwhile.
/// - <b>"All creature types" enum coverage</b>: same v1 equivalent as
///   Mutavault — grants every creature subtype currently enumerated in
///   <see cref="CardSubtype"/>; the set auto-grows with the enum.
/// </summary>
[CardName("Faceless Haven")]
public static class FacelessHavenFactory
{
    public const string CardName = "Faceless Haven";
    public const string Slug = "faceless-haven";
    public const int Power = 4;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Faceless Haven with no <see cref="ContinuousEffectsService"/>
    /// wired. The {T}: Add {C} mana ability (from JSON) + the animate ability
    /// are attached so the card surface is complete; the layer effects are
    /// not registered (shape-only path). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct a fully-wired Faceless Haven.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effects are recorded.</param>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Snow supertype,
        // Land type, {T}: Add {C} mana ability). The animate ability is
        // layered on below — it isn't expressible in the current JSON
        // AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {S}{S}{S}: This land becomes a 4/3 creature with vigilance and all
        // creature types until end of turn. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {S}{S}{S} (three snow mana; parsed as three generic in v1 — see
        // class xmldoc). No tap rider. Resolution registers Layer 4 +
        // Layer 7b continuous effects flagged ExpiresAtEndOfTurn.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes a 4/3 every-creature-type creature with vigilance until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type, every creature subtype,
                // Vigilance. Printed Land type stays ("it's still a land").
                effects.Register(new FacelessHavenAnimateEffect(land));

                // Layer 7b — set base P/T 4/3.
                effects.Register(new FacelessHavenBecomesPTEffect(land, Power, Toughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{S}{S}{S}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}

/// <summary>
/// Faceless Haven — activated-ability resolution: Layer 4 type-adding
/// effect. Adds <see cref="CardType.Creature"/> to the permanent's effective
/// types, every creature subtype the engine currently models (CR 205.3m
/// "all creature types"), and a Vigilance keyword marker (CR 702.20).
/// Expires at end of turn (CR 514.2).
///
/// CR 613.1c — types and subtypes are added on top of printed values; the
/// printed <see cref="CardType.Land"/> remains intact, matching the oracle's
/// "It's still a land" rider.
///
/// Vigilance (CR 702.20) is registered as a keyword marker on the
/// permanent's keyword set — same posture as Mutavault's subtype grant /
/// Cave of the Frost Dragon's Flying. CombatAbilities.HasVigilance /
/// CombatValidator consult the effective keyword set.
/// </summary>
public sealed class FacelessHavenAnimateEffect : ContinuousEffect
{
    private readonly Permanent _target;

    public FacelessHavenAnimateEffect(Permanent target)
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

        // "all creature types" (CR 205.3m). Reuse Mutavault's enumerated set
        // as the single source of truth for the engine's known creature
        // subtypes — see MutavaultFactory class xmldoc for the v1
        // simplification.
        foreach (var st in MutavaultAnimateEffect.EveryCreatureType)
        {
            chars.Subtypes.Add(st);
        }

        chars.Keywords.Add("Vigilance");
    }
}

/// <summary>
/// Faceless Haven — activated-ability resolution: Layer 7b set-base P/T
/// effect recording the 4/3 the manland turns into. Same "Land runtime
/// instance" shim posture as <see cref="MutavaultBecomesPTEffect"/> and
/// <see cref="CaveOfTheFrostDragonBecomesPTEffect"/>:
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
/// <see cref="PermanentCharacteristics"/> with no P/T fields, so the effect
/// is registered for layer-system correctness and exposes
/// <see cref="NewPower"/> / <see cref="NewToughness"/> for inspection until
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> can upgrade the
/// chars row when Layer 4 grants Creature type.
/// </summary>
public sealed class FacelessHavenBecomesPTEffect : ContinuousEffect
{
    private readonly Permanent _target;

    /// <summary>The base power the target becomes (CR 613.7b).</summary>
    public int NewPower { get; }

    /// <summary>The base toughness the target becomes (CR 613.7b).</summary>
    public int NewToughness { get; }

    public FacelessHavenBecomesPTEffect(Permanent target, int power, int toughness)
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
