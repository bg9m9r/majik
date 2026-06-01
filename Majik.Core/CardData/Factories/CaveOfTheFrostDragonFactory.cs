using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cave of the Frost Dragon (Adventures in the
/// Forgotten Realms "cave" manland cycle). Land.
///
/// Oracle text (verified Scryfall 2026-05-29):
///   "If you control two or more other lands, this land enters tapped.
///    {T}: Add {W}.
///    {4}{W}: This land becomes a 3/4 white Dragon creature with flying
///    until end of turn. It's still a land."
///
/// Same posture as the sibling <see cref="HiveOfTheEyeTyrantFactory"/>
/// (Beholder manland) — the simpler white-Dragon member of the cycle: no
/// per-instance attack trigger, an evasion keyword (Flying) instead of
/// Menace, and a {4}{W} animate cost. The base shape (plain nonbasic Land,
/// {T}: Add {W}) is materialised from the embedded JSON definition
/// (<c>cave-of-the-frost-dragon.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the conditional ETB-tapped
/// rider and the animate ability are layered on here because the JSON
/// <c>AbilityDefinition</c> schema doesn't express either yet (same posture
/// as <see cref="StormscaleScionFactory"/> / <see cref="HiveOfTheEyeTyrantFactory"/>).
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes, no supertype) + the
///   <c>{T}: Add {W}</c> mana ability — both from the JSON definition
///   (CR 605.1, mana ability, no stack).
/// - <b>Conditional ETB-tapped (CR 614.1c)</b> — registered as a
///   <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Predicate: enters untapped iff the
///   controller controls one or fewer OTHER lands (i.e. enters tapped
///   when ≥ 2 other lands are present). Mirrors Hive of the Eye Tyrant's
///   "two or more other lands" predicate at threshold = 2.
/// - <b>{4}{W}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{4}{W}</c>. Resolution registers two end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/>:
///     - Layer 4 (<see cref="CaveOfTheFrostDragonAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/> to the permanent's effective
///       types and grants <see cref="CardSubtype.Dragon"/> as a subtype.
///       The printed Land type is left intact ("It's still a land",
///       CR 613.1c). Also grants the Flying <see cref="StaticAbility"/>
///       keyword marker (CR 702.9 — same posture as Hive's Menace / Inkmoth
///       Nexus' Flying marker).
///     - Layer 7b (<see cref="CaveOfTheFrostDragonBecomesPTEffect"/>) —
///       set-base P/T 3/4 (CR 613.7b). Same "Land runtime instance" shim
///       posture as <see cref="HiveOfTheEyeTyrantBecomesPTEffect"/> /
///       <see cref="MutavaultBecomesPTEffect"/>.
///   Both effects carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///   = true so the cleanup-step expiry (CR 514.2) lifts the animation.
///
/// ## Deferred (v1 gaps)
/// - <b>White colour identity of the animated form</b> — same gap as
///   Hive's "black Beholder": the engine's colour layer (Layer 5) has no
///   colour-setting effect primitive yet. The Dragon body should be white
///   while animated; v1 records the intent but doesn't apply it.
/// - <b>Combat math through Compute</b>: same gap as Hive / Mutavault /
///   Inkmoth — until
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> upgrades to
///   a <see cref="CreatureCharacteristics"/> row when Layer 4 grants
///   <see cref="CardType.Creature"/>, the 3/4 doesn't surface for combat
///   resolution.
/// - <b>Activation gate / sorcery-speed</b>: none — the animate ability is
///   instant-speed per oracle, no restriction needed.
/// </summary>
[CardName("Cave of the Frost Dragon")]
public static class CaveOfTheFrostDragonFactory
{
    public const string CardName = "Cave of the Frost Dragon";
    public const string Slug = "cave-of-the-frost-dragon";
    public const int Power = 3;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Cave of the Frost Dragon with no
    /// <see cref="ContinuousEffectsService"/> or <see cref="ReplacementBus"/>
    /// wired. The {T}: Add {W} mana ability (from JSON) + the animate
    /// ability are attached so the card surface is complete; the layer
    /// effects are not registered and the conditional ETB-tapped
    /// replacement is omitted (single-arg shape-only path). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct Cave of the Frost Dragon.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the conditional
    /// "enters tapped if you control two or more other lands" rider
    /// (CR 614.1c). May be null — the land enters untapped unconditionally
    /// in that posture (mirrors how the sibling factories defer this to the
    /// production binder).</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {W} mana ability). The conditional ETB-tapped rider +
        // the animate ability are layered on below — neither is expressible
        // in the current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Conditional ETB-tapped (CR 614.1c) — "If you control two or more
        // other lands, this land enters tapped."
        // Predicate: enters untapped iff controller controls ≤ 1 OTHER
        // land. Same shape as Hive of the Eye Tyrant.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    CountOtherLands(controller, self) <= 1));
        }

        // ----------------------------------------------------------------
        // {4}{W}: This land becomes a 3/4 white Dragon creature with flying
        // until end of turn. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {4}{W}, no tap rider. Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes 3/4 white Dragon creature with flying until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type, Dragon subtype, Flying.
                // Printed Land type stays ("it's still a land").
                effects.Register(new CaveOfTheFrostDragonAnimateEffect(land));

                // Layer 7b — set base P/T 3/4.
                effects.Register(new CaveOfTheFrostDragonBecomesPTEffect(land, Power, Toughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{4}{W}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }

    /// <summary>
    /// CR 614 helper — count lands the controller controls excluding the
    /// candidate <paramref name="self"/>. Used by the conditional ETB-
    /// tapped predicate ("two or more OTHER lands").
    /// </summary>
    private static int CountOtherLands(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasType(CardType.Land));
}

/// <summary>
/// Cave of the Frost Dragon — activated-ability resolution: Layer 4
/// type-adding effect. Adds <see cref="CardType.Creature"/> to the
/// permanent's effective types, plus the <see cref="CardSubtype.Dragon"/>
/// subtype and a Flying keyword marker. Expires at end of turn (CR 514.2).
///
/// CR 613.1c — types are added on top of printed values; the printed
/// <see cref="CardType.Land"/> remains intact, matching the oracle's
/// "It's still a land" rider.
///
/// Flying (CR 702.9) is registered as a keyword marker on the permanent's
/// keyword set — same posture as Hive's Menace / Inkmoth Nexus' Flying.
/// Combat block-legality of Flying is consulted by the combat-block
/// validator when Flying is present in the effective keyword set.
/// </summary>
public sealed class CaveOfTheFrostDragonAnimateEffect : ContinuousEffect
{
    private readonly Permanent _target;

    public CaveOfTheFrostDragonAnimateEffect(Permanent target)
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
        chars.Subtypes.Add(CardSubtype.Dragon);
        chars.Keywords.Add("Flying");
    }
}

/// <summary>
/// Cave of the Frost Dragon — activated-ability resolution: Layer 7b
/// set-base P/T effect recording the 3/4 the manland turns into. Same
/// "Land runtime instance" shim posture as
/// <see cref="HiveOfTheEyeTyrantBecomesPTEffect"/> and
/// <see cref="MutavaultBecomesPTEffect"/>:
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
/// <see cref="PermanentCharacteristics"/> with no P/T fields, so the effect
/// is registered for layer-system correctness and exposes
/// <see cref="NewPower"/> / <see cref="NewToughness"/> for inspection until
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> can upgrade the
/// chars row when Layer 4 grants Creature type.
/// </summary>
public sealed class CaveOfTheFrostDragonBecomesPTEffect : ContinuousEffect
{
    private readonly Permanent _target;

    /// <summary>The base power the target becomes (CR 613.7b).</summary>
    public int NewPower { get; }

    /// <summary>The base toughness the target becomes (CR 613.7b).</summary>
    public int NewToughness { get; }

    public CaveOfTheFrostDragonBecomesPTEffect(Permanent target, int power, int toughness)
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
