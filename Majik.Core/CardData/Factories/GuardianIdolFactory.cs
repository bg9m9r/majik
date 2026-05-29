using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Guardian Idol (Tenth Edition / reprints, {2}).
///
/// Artifact. Oracle text (verified against Scryfall 2026-05-29):
///   "This artifact enters tapped.
///    {T}: Add {C}.
///    {2}: This artifact becomes a 2/2 Golem artifact creature until end of
///    turn."
///
/// Combines the enters-tapped mana-rock body of <see cref="MindStoneFactory"/>
/// / Coldsteel Heart with the animate-self shape of the Worldwake / Ixalan
/// manland cycle (<see cref="MishrasFactoryFactory"/> /
/// <see cref="RestlessSpireFactory"/>) — only here the permanent is already an
/// Artifact, so the animation adds just the Creature type + Golem subtype
/// (CR 613.1c — printed Artifact stays; "becomes a … artifact creature").
///
/// ## Implemented (v1)
/// - Base shape from the embedded JSON definition (<c>guardian-idol.json</c>)
///   via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>: <b>Artifact, {2}</b> plus the
///   <b>{T}: Add {C}</b> mana ability (CR 605.1; {C} buckets as +1 generic in
///   <see cref="ValueObjects.ManaCost.Parse"/>, same convention as Mind Stone /
///   Palladium Myr).
/// - <b>This artifact enters tapped (CR 614.1c)</b> — unconditional ETB-tapped
///   restriction registered as an <see cref="EntersTappedReplacement"/> on the
///   supplied <see cref="ReplacementBus"/> (same posture as
///   <see cref="RestlessSpireFactory"/>). In production the
///   <see cref="EntersTappedBinder"/> already binds this from the card's
///   oracle text on the embedded seed; the explicit registration here keeps
///   the named-factory path self-contained when a bus is wired directly.
/// - <b>{2}: becomes a 2/2 Golem artifact creature until end of turn</b> —
///   an instant-speed <see cref="ActivatedAbility"/> with a single
///   <see cref="ManaCostCost"/>("{2}"). Resolution registers a
///   <see cref="ManlandCycleAnimateEffect"/> (Layer 4 — add
///   <see cref="CardType.Creature"/> + <see cref="CardSubtype.Golem"/>; no
///   extra type because the permanent is already an Artifact) and a
///   <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b — base 2/2). Both
///   flagged <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so
///   <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2 cleanup
///   step) reverts the artifact at end of turn.
///
/// ## Deferred (v1 gaps — shared with the manland cycle)
/// - <b>Artifact-becomes-creature P/T pipeline</b>: a Guardian Idol runtime
///   instance is a non-<see cref="Creature"/> <see cref="Artifact"/>, so
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
///   <see cref="PermanentCharacteristics"/> row with no P/T fields. The Layer
///   4 type/subtype add surfaces through Compute; the 2/2 base P/T is recorded
///   on <see cref="ManlandCycleBecomesPTEffect.NewPower"/> /
///   <see cref="ManlandCycleBecomesPTEffect.NewToughness"/> for inspection but
///   doesn't surface through Compute yet (identical shim to Mishra's Factory /
///   Mutavault).
/// - <b>"Becomes a creature" trigger semantics</b>: nothing fires "whenever a
///   permanent becomes a creature" yet (same gap noted across the manland
///   cycle).
/// </summary>
[CardName("Guardian Idol")]
public static class GuardianIdolFactory
{
    public const string CardName = "Guardian Idol";
    public const string Slug = "guardian-idol";

    /// <summary>The {2} animate cost.</summary>
    public const string AnimateCost = "{2}";

    /// <summary>P/T the artifact becomes while animated.</summary>
    public const int AnimatedPower = 2;
    public const int AnimatedToughness = 2;

    /// <summary>
    /// Construct Guardian Idol with no <see cref="ContinuousEffectsService"/>
    /// or <see cref="ReplacementBus"/> wired. The {T}: Add {C} mana ability
    /// (from JSON) + the {2} animate ability shape are attached so the card
    /// surface is complete; the layer effects are not registered and the
    /// ETB-tapped replacement is omitted. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to (production wires the
    /// ETB-tapped restriction via the <see cref="EntersTappedBinder"/> from the
    /// seed oracle text).
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct Guardian Idol with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 + Layer 7b
    /// registration of the animate ability. May be null — the ability still
    /// resolves and pays {2}, but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the unconditional
    /// enters-tapped rider (CR 614.1c). May be null.</param>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact type,
        // {2} cost, {T}: Add {C} mana ability). The ETB-tapped rider and the
        // animate ability are layered on below — neither is expressible in the
        // current JSON AbilityDefinition schema (same posture as Restless Spire).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var idol = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "This artifact enters tapped." — unconditional ETB-tapped
        // restriction (CR 614.1c). No gate.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(idol));
        }

        // ----------------------------------------------------------------
        // {2}: This artifact becomes a 2/2 Golem artifact creature until end
        // of turn.
        //
        // CR 602 — ordinary (instant-speed) activated ability. Resolution
        // registers Layer 4 (add Creature type + Golem subtype; the printed
        // Artifact stays — "becomes a … artifact creature", CR 613.1c) and
        // Layer 7b (base 2/2) continuous effects flagged ExpiresAtEndOfTurn
        // (CR 514.2 cleanup).
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes a {AnimatedPower}/{AnimatedToughness} Golem artifact creature until end of turn",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Golem subtype. No extra type:
                // the permanent is already an Artifact.
                effects.Register(new ManlandCycleAnimateEffect(
                    idol,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.Golem },
                    extraTypes: null));

                // Layer 7b — set base P/T 2/2.
                effects.Register(new ManlandCycleBecomesPTEffect(
                    idol, AnimatedPower, AnimatedToughness));
            });

        idol.AddAbility(new ActivatedAbility(
            source: idol,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(AnimateCost) },
            effects: new IEffect[] { animateEffect }));

        return idol;
    }
}
