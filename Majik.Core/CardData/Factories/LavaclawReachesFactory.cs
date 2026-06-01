using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lavaclaw Reaches (Worldwake / Modern Horizons 2
/// reprint). Land — Mountain Swamp. Oracle text:
///   "Lavaclaw Reaches enters tapped.
///    {T}: Add {B} or {R}.
///    {X}{B}{R}: Lavaclaw Reaches becomes an X/X black and red Elemental
///    creature with \"This creature gets +1/+0 until end of turn for each
///    {1} spent to activate this ability.\" It's still a land."
///
/// ## Implemented (v1)
/// - Land with printed Mountain + Swamp subtypes (CR 305.6 — a dual-typed
///   nonbasic land taps for its land subtypes via the matching intrinsic
///   mana abilities, but those intrinsic abilities are NOT auto-attached
///   in the engine; the printed oracle abilities below are the source of
///   truth).
/// - <b>Unconditional ETB-tapped (CR 614.1c)</b> via
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Single-arg dispatcher path omits the
///   replacement (mirrors every other always-tapped factory).
/// - <b>{T}: Add {B}</b> and <b>{T}: Add {R}</b> — two
///   <see cref="ManaAbility"/> instances (CR 605.1). Note: oracle phrases
///   this as one ability ("Add {B} or {R}") but the engine models it as
///   two distinct mana abilities — same pattern as Creeping Tar Pit.
/// - <b>{X}{B}{R}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{X}{B}{R}</c>. Resolution registers two end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/>:
///     - Layer 4 (<see cref="LavaclawReachesAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/> + <see cref="CardSubtype.Elemental"/>
///       subtype. Printed Land + Mountain + Swamp stays (CR 613.1c).
///     - Layer 7b (<see cref="LavaclawReachesBecomesPTEffect"/>) — set-base
///       P/T <c>X/X</c>, with the X sampled at resolution from the
///       caller-supplied <paramref name="xValueProvider"/>. Same v1
///       approximation Engineered Explosives / Pernicious Deed use: the
///       engine has no live per-activation X ledger, so X comes from a
///       wired provider (returns 0 on the single-arg shape-only path).
///       The "gets +1/+0 for each {1} spent" rider is folded into the
///       same X: an X/X body that gets +X/+0 is observationally an
///       (X + X)/X — see Deferred below for the spec note.
///
/// ## Deferred (v1 gaps)
/// - <b>"+1/+0 per {1} spent" rider</b>: oracle defines the body as X/X
///   and then PLUS X/+0, yielding a (2X)/X body in practice (CR 107.3 —
///   X is the same X across both clauses of the ability). v1 ships the
///   correct (2X)/X via <see cref="LavaclawReachesBecomesPTEffect"/>
///   building power = 2 * x and toughness = x at resolution. A future
///   pass that splits the Layer-7b set-base from a Layer-7c addition
///   ("+X/+0 EOT") will swap the math into two effects without changing
///   the visible result.
/// - <b>Black and red colour identity of the animated form</b> — same
///   gap as Creeping Tar Pit / Hive of the Eye Tyrant. No Layer 5
///   colour-set primitive yet.
/// - <b>Combat math through Compute</b>: same gap as every other manland.
/// - <b>X-payment provenance</b>: the engine has no live X ledger;
///   callers wire <paramref name="xValueProvider"/> to whatever signal
///   they have. Single-arg dispatcher path returns 0 (animated body is
///   0/0 — dies to SBAs immediately, matching the legal-but-pointless
///   X=0 activation).
/// </summary>
[CardName("Lavaclaw Reaches")]
public static class LavaclawReachesFactory
{
    public const string CardName = "Lavaclaw Reaches";

    /// <summary>
    /// Construct Lavaclaw Reaches with no live wiring. The mana abilities
    /// + animate ActivatedAbility are attached; the animate effect is
    /// registered against a null effects service (no-op), and the
    /// xValueProvider defaults to <c>() => 0</c> (X = 0 at resolution).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null, xValueProvider: null);

    /// <summary>
    /// Construct Lavaclaw Reaches.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null.</param>
    /// <param name="replacements">Replacement bus for the always-enters-
    /// tapped restriction (CR 614.1c). May be null.</param>
    /// <param name="xValueProvider">Callback supplying X at resolution
    /// time. Mirrors Pernicious Deed / Engineered Explosives — the engine
    /// has no live X-payment ledger yet. Null defaults to <c>() => 0</c>.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements,
        Func<int>? xValueProvider)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Printed subtypes: Mountain + Swamp (CR 305.6 — dual-typed
        // nonbasic land).
        var land = new Land(
            CardName,
            supertypes: null,
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Swamp });
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB-tapped restriction (CR 614.1c) — "Lavaclaw Reaches enters
        // tapped." Unconditional; no gate.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {B}  /  {T}: Add {R}
        // CR 605.1 — mana abilities do not use the stack. Modelled as two
        // distinct mana abilities (same approach as Creeping Tar Pit).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("R")));

        // ----------------------------------------------------------------
        // {X}{B}{R}: Until end of turn, Lavaclaw Reaches becomes an X/X
        // black and red Elemental creature with "This creature gets
        // +1/+0 until end of turn for each {1} spent to activate this
        // ability." It's still a land.
        //
        // CR 107.3 — X is the same value across both clauses, so the
        // animated body is (X + X) / X = 2X/X. v1 collapses both clauses
        // into a single set-base PT effect at resolution.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes X/X black and red Elemental creature with +X/+0 until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                var x = xValueProvider?.Invoke() ?? 0;

                // Layer 4 — add Creature + Elemental subtype. Printed
                // Land + Mountain + Swamp stay.
                effects.Register(new LavaclawReachesAnimateEffect(land));

                // Layer 7b — set base P/T to 2X/X (X/X body + X/+0
                // rider, see CR 107.3 note above).
                effects.Register(new LavaclawReachesBecomesPTEffect(land, x));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{X}{B}{R}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}

/// <summary>
/// Lavaclaw Reaches — activated-ability resolution: Layer 4 type-adding
/// effect. Adds <see cref="CardType.Creature"/> + <see cref="CardSubtype.Elemental"/>.
/// Expires at end of turn (CR 514.2).
///
/// CR 613.1c — types and subtypes are added on top of printed values;
/// the printed Land + Mountain + Swamp subtypes remain intact, matching
/// the oracle's "It's still a land" rider.
/// </summary>
public sealed class LavaclawReachesAnimateEffect : ContinuousEffect
{
    private readonly Permanent _target;

    public LavaclawReachesAnimateEffect(Permanent target)
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
/// Lavaclaw Reaches — activated-ability resolution: Layer 7b set-base
/// P/T effect. P/T = <c>2X/X</c> at resolution: the X/X body plus the
/// "+1/+0 per {1} spent" rider collapse into a single 2X/X set-base per
/// CR 107.3 (X is the same value across both clauses). See the
/// <see cref="LavaclawReachesFactory"/> xmldoc for the v1 collapse
/// rationale; a future pass can split into a Layer-7b set-base X/X +
/// Layer-7c +X/+0 without changing the visible result.
///
/// Same "Land runtime instance" shim posture as the rest of the manland
/// cycle: <see cref="ContinuousEffectsService.Compute(Permanent)"/>
/// seeds a plain <see cref="PermanentCharacteristics"/> with no P/T
/// fields, so the P/T is recorded for inspection but doesn't surface
/// through Compute yet.
/// </summary>
public sealed class LavaclawReachesBecomesPTEffect : ContinuousEffect
{
    private readonly Permanent _target;

    /// <summary>X at the time the ability was activated.</summary>
    public int X { get; }

    /// <summary>The base power the target becomes (2X — body + rider).</summary>
    public int NewPower => 2 * X;

    /// <summary>The base toughness the target becomes (X).</summary>
    public int NewToughness => X;

    public LavaclawReachesBecomesPTEffect(Permanent target, int x)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        X = x;
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
