using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blinkmoth Nexus (Darksteel / Modern Masters).
///
/// Land. Oracle text (Scryfall, verified):
///   "{T}: Add {C}.
///    {1}: This land becomes a 1/1 Blinkmoth artifact creature with flying
///         until end of turn. It's still a land.
///    {1}, {T}: Target Blinkmoth creature gets +1/+1 until end of turn."
///
/// Near-twin of <see cref="InkmothNexusFactory"/> — same colorless mana +
/// {1} animate-to-a-1/1-flying-artifact-creature shape — but animates to a
/// <see cref="CardSubtype.Blinkmoth"/> body (no infect) and carries a third
/// targeted pump ability.
///
/// ## Implemented (v1)
/// - Land identity (no printed subtypes / supertypes).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> generating one
///   colorless. {C} is bucketed as +1 generic in <see cref="ValueObjects.ManaCost"/>
///   today (see <see cref="ValueObjects.ManaCost.Parse"/>), matching Inkmoth.
/// - <b>{1}: Until EOT becomes a 1/1 Blinkmoth artifact creature with
///   flying; still a land</b> — an <see cref="ActivatedAbility"/> whose
///   resolution registers a <see cref="BlinkmothAnimateLandEffect"/> on the
///   supplied <see cref="ContinuousEffectsService"/> (Layer 4 type/subtype/
///   keyword add per CR 613.1d/1c; the printed Land type stays — "It's still
///   a land"). Expires at cleanup (CR 514.2). Same shim posture as
///   <see cref="InkmothAnimateLandEffect"/>: the 1/1 P/T is recorded for
///   inspection but doesn't surface through <see cref="ContinuousEffectsService.Compute(Permanent)"/>
///   while the runtime instance is a non-<see cref="Creature"/> <see cref="Land"/>.
/// - <b>{1}, {T}: Target Blinkmoth creature gets +1/+1 until end of turn</b>
///   — an <see cref="ActivatedAbility"/> with cost
///   <see cref="ManaCostCost"/>("{1}") + <see cref="AdditionalCost.Tap"/>
///   and a single 1..1 "target Blinkmoth creature" request (same
///   target-creature activated-ability shape as
///   <see cref="ShadowspearFactory"/>). On resolution it registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+1/+1) on the chosen creature
///   (CR 613.7c, expires CR 514.2) — the same +P/+T primitive
///   <see cref="BerserkFactory"/> uses. Without a continuous-effects service
///   on the target (or no chosen target) the resolution is a documented
///   no-op (CR 608.2b — illegal/absent target → nothing happens).
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality filter "Blinkmoth creature"</b>: the
///   <see cref="TargetRequest"/> records the description, but candidate
///   filtering by subtype is enforced at target-selection time by the agent
///   layer (same posture Shadowspear documents for "target creature"). The
///   pump still defends in depth at resolution by gating on
///   <see cref="ZoneType.Battlefield"/> + a live effects service.
/// - <b>Land-becomes-creature P/T pipeline</b>: same v1 limitation Inkmoth /
///   the manland cycle carry — <see cref="ContinuousEffectsService.Compute(Permanent)"/>
///   builds a <see cref="PermanentCharacteristics"/> with no P/T fields for
///   a non-Creature runtime instance, so the animated 1/1 is inspectable on
///   the effect but not applied through Compute on the Land itself.
/// - <b>"Becomes" trigger semantics</b>: nothing fires "whenever a permanent
///   becomes a creature" yet (same gap noted on Inkmoth / Mutavault).
/// </summary>
[CardName("Blinkmoth Nexus")]
public static class BlinkmothNexusFactory
{
    public const string CardName = "Blinkmoth Nexus";

    /// <summary>Animate cost — {1}.</summary>
    public const string AnimateCost = "{1}";

    /// <summary>Pump cost — {1} (plus a {T} additional cost).</summary>
    public const string PumpCost = "{1}";

    /// <summary>
    /// Construct Blinkmoth Nexus with no live continuous-effects wiring (the
    /// shape / dispatcher path). All three abilities are attached so the card
    /// shape is complete; the animate + pump resolution closures no-op
    /// because no <see cref="ContinuousEffectsService"/> is available to
    /// register against (legal — the deferred-wiring contract mirrors
    /// <see cref="InkmothNexusFactory.Create(Player)"/>).
    /// </summary>
    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct a fully-wired Blinkmoth Nexus.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service used by the animate
    /// ability. May be null — the animate ability still resolves and pays
    /// {1}, but no <see cref="BlinkmothAnimateLandEffect"/> is registered.
    /// The pump ability registers against the chosen target creature's own
    /// <see cref="Creature.ActiveEffects"/>, independent of this service.</param>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities don't use the stack. {C} is bucketed
        // as +1 generic in ManaCost.Parse today (see ValueObjects.ManaCost).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("{C}")));

        // ----------------------------------------------------------------
        // {1}: Until EOT, becomes a 1/1 Blinkmoth artifact creature with
        // flying. It's still a land. (CR 613.1d Layer 4 type-add; CR 514.2
        // cleanup expiry.)
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: become a 1/1 Blinkmoth artifact creature with flying until end of turn",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path
                effects.Register(new BlinkmothAnimateLandEffect(land));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(AnimateCost) },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // {1}, {T}: Target Blinkmoth creature gets +1/+1 until end of turn.
        // CR 602 activated ability; CR 613.7c Layer-7c pump; CR 514.2 expiry.
        // Same target-creature activated-ability shape as Shadowspear; reuses
        // the PumpUntilEndOfTurnEffect primitive (Berserk).
        // ----------------------------------------------------------------
        ActivatedAbility? pumpAbility = null;
        var pumpEffect = new Effect(
            $"{CardName}: target Blinkmoth creature gets +1/+1 until end of turn",
            () =>
            {
                if (pumpAbility == null) return;
                if (pumpAbility.ChosenTargets.Count == 0) return;
                if (pumpAbility.ChosenTargets[0].Count == 0) return;
                if (pumpAbility.ChosenTargets[0][0] is not Creature creature) return;

                // CR 608.2b — illegal target on resolution (left the
                // battlefield) → no-op. Defence-in-depth zone check.
                if (creature.Zone != Majik.Core.Zones.ZoneType.Battlefield) return;

                // Register against the target creature's own effects service.
                // Without one (shape-only target) the pump is a documented
                // no-op — the +1/+1 simply isn't tracked.
                if (creature.ActiveEffects == null) return;
                creature.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(creature, p: 1, t: 1));
            });

        pumpAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(PumpCost),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { pumpEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target Blinkmoth creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(pumpAbility);

        return land;
    }

    /// <summary>The {1} animate ability (the one with no target requests).</summary>
    public static ActivatedAbility GetAnimateAbility(Land land)
    {
        ArgumentNullException.ThrowIfNull(land);
        return land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);
    }

    /// <summary>The {1}, {T} targeted +1/+1 pump ability.</summary>
    public static ActivatedAbility GetPumpAbility(Land land)
    {
        ArgumentNullException.ThrowIfNull(land);
        return land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);
    }
}

/// <summary>
/// Blinkmoth Nexus animate effect — until EOT the land also counts as a
/// 1/1 Blinkmoth artifact creature with flying.
///
/// Layer 4 (CR 613.1d) — adds <see cref="CardType.Artifact"/> +
/// <see cref="CardType.Creature"/> to the permanent's effective types
/// (printed Land stays — "It's still a land", CR 613.1c), plus the
/// <see cref="CardSubtype.Blinkmoth"/> subtype and a Flying keyword marker.
///
/// Layer 7b P/T (1/1) is recorded on <see cref="NewPower"/> /
/// <see cref="NewToughness"/> for inspection but does NOT surface through
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> while the land
/// remains a non-<see cref="Creature"/> runtime instance — same v1
/// limitation <see cref="InkmothAnimateLandEffect"/> carries.
///
/// "Until end of turn" (CR 514.2) — <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
/// is true; <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> drops the
/// effect during cleanup, reverting the land.
/// </summary>
public sealed class BlinkmothAnimateLandEffect : ContinuousEffect
{
    private readonly Land _target;

    /// <summary>P/T the land's body reads as while animated (1/1).</summary>
    public int NewPower => 1;

    /// <summary>P/T the land's body reads as while animated (1/1).</summary>
    public int NewToughness => 1;

    public BlinkmothAnimateLandEffect(Land target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>The animated land.</summary>
    public Land Target => _target;

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _target;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool ExpiresAtEndOfTurn => true;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars)
    {
        Apply((PermanentCharacteristics)chars);
        // If Compute(Permanent) later upgrades to a CreatureCharacteristics
        // for type-changed-to-Creature permanents, this sets the layer-7b
        // base P/T to 1/1 in the same pass (mirrors Inkmoth).
        chars.Power = NewPower;
        chars.Toughness = NewToughness;
    }

    public override void Apply(PermanentCharacteristics chars)
    {
        // Layer 4 — additive type-add. Printed Land stays in chars.Types.
        chars.Types.Add(CardType.Artifact);
        chars.Types.Add(CardType.Creature);

        // Layer 4 — subtype addition (Blinkmoth).
        chars.Subtypes.Add(CardSubtype.Blinkmoth);

        // Flying keyword grant (gates blocking legality). No Infect — unlike
        // Inkmoth Nexus, Blinkmoth Nexus's animated body has only flying.
        chars.Keywords.Add("Flying");
    }
}
