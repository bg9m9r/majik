using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lair of the Hydra (Modern Horizons 2 "creature
/// land" cycle, green member — sibling of <see cref="LavaclawReachesFactory"/>
/// and the X/X-animate variant of the conditional-tapped manlands like
/// <see cref="CaveOfTheFrostDragonFactory"/> / <see cref="DenOfTheBugbearFactory"/>).
/// Land.
///
/// Oracle text (verified Scryfall 2026-05-29):
///   "If you control two or more other lands, this land enters tapped.
///    {T}: Add {G}.
///    {X}{G}: Until end of turn, this land becomes an X/X green Hydra
///    creature. It's still a land. X can't be 0."
///
/// Posture: the simpler green member of the X/X creature-land family. Base
/// shape (plain nonbasic Land, {T}: Add {G}) is materialised from the
/// embedded JSON definition (<c>lair-of-the-hydra.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="CaveOfTheFrostDragonFactory"/>. The conditional ETB-tapped
/// rider and the X/X animate ability are layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses neither yet.
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes, no supertype) + the
///   <c>{T}: Add {G}</c> mana ability — both from the JSON definition
///   (CR 605.1, mana ability, no stack).
/// - <b>Conditional ETB-tapped (CR 614.1c)</b> — registered as a
///   <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Predicate: enters untapped iff the
///   controller controls one or fewer OTHER lands (i.e. enters tapped
///   when >= 2 other lands are present). Same "two or more other lands"
///   threshold as <see cref="CaveOfTheFrostDragonFactory"/>.
/// - <b>{X}{G}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{X}{G}</c>. Resolution registers two end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/>:
///     - Layer 4 (<see cref="ManlandCycleAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/> and the
///       <see cref="CardSubtype.Hydra"/> subtype. The printed Land type is
///       left intact ("It's still a land", CR 613.1c). No printed keywords
///       on the animated body.
///     - Layer 7b (<see cref="ManlandCycleBecomesPTEffect"/>) — set-base
///       P/T <c>X/X</c>, with the X sampled at resolution from the
///       caller-supplied <paramref name="xValueProvider"/>. Same posture as
///       <see cref="LavaclawReachesFactory"/> — the engine has no live
///       per-activation X ledger, so X comes from a wired provider.
///   Both effects carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///   = true so the cleanup-step expiry (CR 514.2) lifts the animation.
/// - <b>"X can't be 0" rider (CR 107.1b)</b> — the sampled X is clamped to
///   a minimum of 1 before being recorded as the animated body, mirroring
///   the printed restriction that the activation is illegal at X = 0. The
///   engine has no activation-time X validator yet, so the clamp enforces
///   the only observable consequence (the body is never 0/0, so it never
///   dies to the SBA for 0 toughness as a degenerate free activation).
///
/// ## Deferred (v1 gaps)
/// - <b>Green colour identity of the animated form</b> — same gap as the
///   rest of the creature-land family (Lavaclaw Reaches / Creeping Tar Pit
///   / Hive of the Eye Tyrant): Layer 5 has no colour-setting effect
///   primitive yet. The Hydra body should be green while animated; v1
///   records the intent in the effect name but doesn't apply it.
/// - <b>Combat math through Compute</b>: same gap as every other manland —
///   until <see cref="ContinuousEffectsService.Compute(Permanent)"/>
///   upgrades to a <see cref="CreatureCharacteristics"/> row when Layer 4
///   grants <see cref="CardType.Creature"/>, the X/X doesn't surface for
///   combat resolution.
/// - <b>X-payment provenance</b>: the engine has no live X ledger; callers
///   wire <paramref name="xValueProvider"/> to whatever signal they have.
///   Single-arg dispatcher path returns 0, which the "X can't be 0" clamp
///   raises to a minimal 1/1 animated body.
/// - <b>Activation gate / sorcery-speed</b>: none — the animate ability is
///   instant-speed per oracle, no restriction needed.
/// </summary>
[CardName("Lair of the Hydra")]
public static class LairOfTheHydraFactory
{
    public const string CardName = "Lair of the Hydra";
    public const string Slug = "lair-of-the-hydra";

    /// <summary>
    /// Minimum animated body size — the printed "X can't be 0" rider
    /// (CR 107.1b) means a legal activation always yields at least a 1/1.
    /// </summary>
    public const int MinX = 1;

    /// <summary>
    /// Construct Lair of the Hydra with no <see cref="ContinuousEffectsService"/>
    /// or <see cref="ReplacementBus"/> wired, and an X provider defaulting
    /// to <c>() =&gt; 0</c> (clamped to the MinX 1/1 body at resolution).
    /// The {T}: Add {G} mana ability (from JSON) + the animate ability are
    /// attached so the card surface is complete; the layer effects are not
    /// registered and the conditional ETB-tapped replacement is omitted.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null, xValueProvider: null);

    /// <summary>
    /// Construct Lair of the Hydra.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the conditional
    /// "enters tapped if you control two or more other lands" rider
    /// (CR 614.1c). May be null — the land enters untapped unconditionally
    /// in that posture.</param>
    /// <param name="xValueProvider">Callback supplying X at resolution time.
    /// Mirrors <see cref="LavaclawReachesFactory"/> — the engine has no live
    /// X-payment ledger yet. Null defaults to <c>() =&gt; 0</c>; the sampled
    /// value is clamped to <see cref="MinX"/> ("X can't be 0").</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements,
        Func<int>? xValueProvider)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {G} mana ability). The conditional ETB-tapped rider +
        // the animate ability are layered on below — neither is expressible
        // in the current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Conditional ETB-tapped (CR 614.1c) — "If you control two or more
        // other lands, this land enters tapped."
        // Predicate: enters untapped iff controller controls <= 1 OTHER
        // land. Same shape as Cave of the Frost Dragon.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    CountOtherLands(controller, self) <= 1));
        }

        // ----------------------------------------------------------------
        // {X}{G}: Until end of turn, this land becomes an X/X green Hydra
        // creature. It's still a land. X can't be 0.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {X}{G}. Resolution registers Layer 4 + Layer 7b continuous
        // effects flagged ExpiresAtEndOfTurn. X is sampled from the wired
        // provider and clamped to MinX (CR 107.1b — "X can't be 0").
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes an X/X green Hydra creature until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                var x = Math.Max(MinX, xValueProvider?.Invoke() ?? 0);

                // Layer 4 — add Creature type + Hydra subtype. No printed
                // keywords on the animated body. Printed Land type stays
                // ("it's still a land", CR 613.1c).
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.Hydra },
                    extraTypes: null));

                // Layer 7b — set base P/T to X/X (CR 613.7b).
                effects.Register(new ManlandCycleBecomesPTEffect(land, x, x));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{X}{G}") },
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
