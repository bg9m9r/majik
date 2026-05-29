using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ghitu Encampment (Urza's Saga creature-land).
/// Land.
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "This land enters tapped.
///    {T}: Add {R}.
///    {1}{R}: This land becomes a 2/1 red Warrior creature with first
///    strike until end of turn. It's still a land."
///
/// Shares the manland animate shape used by <see cref="NeedleSpiresFactory"/> /
/// <see cref="RestlessSpireFactory"/>: unconditional ETB-tapped (CR 614.1c),
/// a single {T}: Add {R} mana ability (CR 605.1), and a {1}{R} animate-until-EOT
/// <see cref="ActivatedAbility"/> whose resolution registers a
/// <see cref="ManlandCycleAnimateEffect"/> (Layer 4 — add Creature type +
/// Warrior subtype + First Strike keyword) and a
/// <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b — set base P/T 2/1),
/// both flagged <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so
/// <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2 cleanup
/// step) lifts the animation.
///
/// The base shape (plain nonbasic Land + the {T}: Add {R} mana ability) is
/// materialised from the embedded JSON definition (<c>ghitu-encampment.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the ETB-tapped rider and the
/// animate ability are layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses neither yet (same posture as
/// <see cref="RestlessSpireFactory"/>).
///
/// ## What is unique to Ghitu Encampment vs the rest of the cycle
/// - Mono-red: a single {T}: Add {R} mana ability instead of the two
///   allied-colour abilities of the dual-land manlands.
/// - Animated body is a 2/1 red <see cref="CardSubtype.Warrior"/> (not the
///   cycle-default Elemental), so this factory passes an explicit
///   <c>subtypes</c> grant to <see cref="ManlandCycleAnimateEffect"/>.
/// - No printed attack trigger (unlike Raging Ravine / Restless Spire).
///
/// ## v1 posture (shared with the manland cycle)
/// - <b>Red colour identity of the animated form</b> — the engine has no
///   Layer-5 colour-set primitive yet (same gap as Needle Spires / Restless
///   Spire). Recorded only in the effect-name string. The Warrior subtype,
///   2/1 body, and First Strike keyword DO apply via Compute.
/// - <b>Combat math through Compute</b> — same gap as every other manland:
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
///   <see cref="PermanentCharacteristics"/> row for a Land runtime instance,
///   so the 2/1 + First Strike are recorded for inspection but don't surface
///   for combat resolution yet.
/// </summary>
[CardName("Ghitu Encampment")]
public static class GhituEncampmentFactory
{
    public const string CardName = "Ghitu Encampment";
    public const string Slug = "ghitu-encampment";
    public const int AnimatedPower = 2;
    public const int AnimatedToughness = 1;

    /// <summary>
    /// Construct Ghitu Encampment with no <see cref="ContinuousEffectsService"/>
    /// or <see cref="ReplacementBus"/> wired. The {T}: Add {R} mana ability
    /// (from JSON) + the animate ability are attached so the card surface is
    /// complete; the layer effects are not registered and the ETB-tapped
    /// replacement is omitted. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct Ghitu Encampment with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 + Layer
    /// 7b registration of the animate ability. May be null — the ability
    /// still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the unconditional
    /// enters-tapped rider (CR 614.1c). May be null.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {R} mana ability). The ETB-tapped rider and the animate
        // ability are layered on below — neither is expressible in the
        // current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB-tapped restriction (CR 614.1c) — "This land enters tapped."
        // Unconditional; no gate.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {1}{R}: This land becomes a 2/1 red Warrior creature with first
        // strike until end of turn. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Resolution
        // registers Layer 4 + Layer 7b continuous effects flagged
        // ExpiresAtEndOfTurn (CR 514.2).
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes {AnimatedPower}/{AnimatedToughness} red Warrior creature with first strike until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Warrior subtype + First
                // Strike keyword. Printed Land type stays ("it's still a
                // land", CR 613.1c).
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: new[] { "First Strike" },
                    subtypes: new[] { CardSubtype.Warrior },
                    extraTypes: null));

                // Layer 7b — set base P/T 2/1.
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{R}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
