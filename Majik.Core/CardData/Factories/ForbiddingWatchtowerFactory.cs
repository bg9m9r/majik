using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Forbidding Watchtower (Urza's Saga / Tenth Edition
/// mono-white manland). Land.
///
/// Oracle text (verified Scryfall 2026-05-29):
///   "This land enters tapped.
///    {T}: Add {W}.
///    {1}{W}: This land becomes a 1/5 white Soldier creature until end of
///    turn. It's still a land."
///
/// Same posture as the white-Dragon cycle-mate
/// <see cref="CaveOfTheFrostDragonFactory"/> (white manland: {T}: Add {W}
/// from JSON + an activated animate-until-EOT that adds Creature type, a
/// subtype, and a base P/T via Layer 4 / Layer 7b continuous effects), with
/// three differences:
///   - it enters tapped <em>unconditionally</em> ("This land enters tapped.")
///     rather than under a "two or more other lands" predicate — the
///     unconditional ETB-tapped (CR 614.1c) is applied on the production load
///     path by <see cref="EntersTappedBinder"/> (its regex matches the oracle
///     sentence), so this factory wires no replacement at all — same posture
///     <see cref="BlossomingSandsFactory"/> and the Refuge / Temple taplands
///     take;
///   - the animated body is a 1/5 <see cref="CardSubtype.Soldier"/> with no
///     evasion keyword (no Flying);
///   - the animate cost is {1}{W}.
///
/// The base shape (plain nonbasic Land, {T}: Add {W}) is materialised from
/// the embedded JSON definition (<c>forbidding-watchtower.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the animate ability is layered
/// on here because the JSON <c>AbilityDefinition</c> schema doesn't express a
/// "becomes a creature until end of turn" effect yet (same posture as
/// <see cref="CaveOfTheFrostDragonFactory"/>).
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes, no supertype) + the
///   <c>{T}: Add {W}</c> mana ability — both from the JSON definition
///   (CR 605.1, mana ability, no stack).
/// - <b>{1}{W}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{1}{W}</c>. Resolution registers two end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/>:
///     - Layer 4 (<see cref="ForbiddingWatchtowerAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/> to the permanent's effective types
///       and grants <see cref="CardSubtype.Soldier"/> as a subtype. The
///       printed Land type is left intact ("It's still a land",
///       CR 613.1c). No keyword grant (the body has no evasion).
///     - Layer 7b (<see cref="ForbiddingWatchtowerBecomesPTEffect"/>) —
///       set-base P/T 1/5 (CR 613.7b). Same "Land runtime instance" shim
///       posture as <see cref="CaveOfTheFrostDragonBecomesPTEffect"/> /
///       <see cref="MutavaultBecomesPTEffect"/>.
///   Both effects carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///   = true so the cleanup-step expiry (CR 514.2) lifts the animation.
///
/// ## Deferred (v1 gaps)
/// - <b>White colour identity of the animated form</b> — same gap as Cave's
///   "white Dragon": the engine's colour layer (Layer 5) has no
///   colour-setting effect primitive yet. The Soldier body should be white
///   while animated; v1 records the intent but doesn't apply it.
/// - <b>Combat math through Compute</b>: same gap as Cave / Mutavault /
///   Inkmoth — until
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> upgrades to a
///   <see cref="CreatureCharacteristics"/> row when Layer 4 grants
///   <see cref="CardType.Creature"/>, the 1/5 doesn't surface for combat
///   resolution.
/// - <b>Activation gate / sorcery-speed</b>: none — the animate ability is
///   instant-speed per oracle, no restriction needed.
/// </summary>
[CardName("Forbidding Watchtower")]
public static class ForbiddingWatchtowerFactory
{
    public const string CardName = "Forbidding Watchtower";
    public const string Slug = "forbidding-watchtower";
    public const int Power = 1;
    public const int Toughness = 5;

    /// <summary>Animate cost — {1}{W}.</summary>
    public const string AnimateCost = "{1}{W}";

    /// <summary>
    /// Construct Forbidding Watchtower with no
    /// <see cref="ContinuousEffectsService"/> wired. The {T}: Add {W} mana
    /// ability (from JSON) + the animate ability are attached so the card
    /// surface is complete; the layer effects are not registered
    /// (single-arg shape-only path). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to. The unconditional
    /// ETB-tapped rider is applied on the production load path by
    /// <see cref="EntersTappedBinder"/>, not here.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Forbidding Watchtower.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effects are recorded.</param>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {W} mana ability). The animate ability is layered on
        // below — it isn't expressible in the current JSON AbilityDefinition
        // schema. Unconditional ETB-tapped is handled by EntersTappedBinder
        // on the production load path (same posture as the taplands).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {1}{W}: This land becomes a 1/5 white Soldier creature until end
        // of turn. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {1}{W}, no tap rider. Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes 1/5 white Soldier creature until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Soldier subtype. Printed
                // Land type stays ("it's still a land").
                effects.Register(new ForbiddingWatchtowerAnimateEffect(land));

                // Layer 7b — set base P/T 1/5.
                effects.Register(new ForbiddingWatchtowerBecomesPTEffect(land, Power, Toughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(AnimateCost) },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}

/// <summary>
/// Forbidding Watchtower — activated-ability resolution: Layer 4 type-adding
/// effect. Adds <see cref="CardType.Creature"/> to the permanent's effective
/// types plus the <see cref="CardSubtype.Soldier"/> subtype. Expires at end
/// of turn (CR 514.2).
///
/// CR 613.1c — types are added on top of printed values; the printed
/// <see cref="CardType.Land"/> remains intact, matching the oracle's "It's
/// still a land" rider. Unlike <see cref="CaveOfTheFrostDragonAnimateEffect"/>
/// there is no keyword grant — the 1/5 Soldier body has no evasion.
/// </summary>
public sealed class ForbiddingWatchtowerAnimateEffect : ContinuousEffect
{
    private readonly Permanent _target;

    public ForbiddingWatchtowerAnimateEffect(Permanent target)
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
        chars.Subtypes.Add(CardSubtype.Soldier);
    }
}

/// <summary>
/// Forbidding Watchtower — activated-ability resolution: Layer 7b set-base
/// P/T effect recording the 1/5 the manland turns into. Same "Land runtime
/// instance" shim posture as <see cref="CaveOfTheFrostDragonBecomesPTEffect"/>
/// and <see cref="MutavaultBecomesPTEffect"/>:
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
/// <see cref="PermanentCharacteristics"/> with no P/T fields, so the effect is
/// registered for layer-system correctness and exposes <see cref="NewPower"/>
/// / <see cref="NewToughness"/> for inspection until
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> can upgrade the
/// chars row when Layer 4 grants Creature type.
/// </summary>
public sealed class ForbiddingWatchtowerBecomesPTEffect : ContinuousEffect
{
    private readonly Permanent _target;

    /// <summary>The base power the target becomes (CR 613.7b).</summary>
    public int NewPower { get; }

    /// <summary>The base toughness the target becomes (CR 613.7b).</summary>
    public int NewToughness { get; }

    public ForbiddingWatchtowerBecomesPTEffect(Permanent target, int power, int toughness)
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
