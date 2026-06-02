using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dread Statuary (Magic 2010 / reprints).
///
/// Land.
/// Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add {C}.
///    {4}: This land becomes a 4/2 Golem artifact creature until end of
///    turn. It's still a land."
///
/// Type line: Land. Printed P/T: none (it's a land until animated).
///
/// Shares the colorless artifact-creature manland animate shape of
/// <see cref="MishrasFactoryFactory"/> (the closest precedent — also a
/// colourless land that animates to an *artifact* creature): the base
/// shape (plain nonbasic Land + the <c>{T}: Add {C}</c> mana ability) is
/// materialised from the embedded JSON definition
/// (<c>dread-statuary.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>, and the animate ability is
/// layered on here because the JSON <c>AbilityDefinition</c> schema does
/// not yet express animate effects (same posture as Ghitu Encampment /
/// Faceless Haven / Mishra's Factory).
///
/// ## Implemented (v1)
/// - Plain nonbasic Land identity (no printed subtypes, no supertype) +
///   <c>{T}: Add {C}</c> mana ability — both from the JSON definition
///   (CR 605.1, mana ability, no stack). Dread Statuary enters untapped
///   unconditionally (no ETB-tapped clause), so no <c>ReplacementBus</c>
///   rider is needed.
/// - <b>{4}: become a 4/2 Golem artifact creature until EOT; still a
///   land</b> — wired as an <see cref="ActivatedAbility"/> with a
///   <see cref="ManaCostCost"/> of <c>{4}</c>. Resolution registers a
///   <see cref="ManlandCycleAnimateEffect"/> (Layer 4 — adds Creature +
///   Artifact types and the <see cref="CardSubtype.Golem"/> subtype, no
///   keyword grants) and a <see cref="ManlandCycleBecomesPTEffect"/>
///   (Layer 7b — base 4/2). Both flagged
///   <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so cleanup
///   (CR 514.2) lifts the animation. The printed Land type stays through
///   Layer 4 (CR 613.1c — "It's still a land").
///
/// ## Deferred (v1 gaps — shared with the manland cycle)
/// - <b>Land-becomes-creature P/T pipeline</b>: see
///   <see cref="MutavaultFactory"/> / <see cref="MishrasFactoryFactory"/>
///   notes — <see cref="ContinuousEffectsService.Compute(Permanent)"/> on
///   a Land runtime instance records the 4/2 base for inspection; the
///   Layer-4 Creature grant + Layer-7b set-base surface through Compute
///   when the chars row upgrades to a creature row.
/// </summary>
[CardName("Dread Statuary")]
public static class DreadStatuaryFactory
{
    public const string CardName = "Dread Statuary";
    public const string Slug = "dread-statuary";
    public const int AnimatedPower = 4;
    public const int AnimatedToughness = 2;

    /// <summary>
    /// Construct Dread Statuary with no <see cref="ContinuousEffectsService"/>
    /// wired. The {T}: Add {C} mana ability (from JSON) + the animate
    /// ability are attached so the card surface is complete; the layer
    /// effects are not registered (shape-only path). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct a fully-wired Dread Statuary.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effects are recorded.</param>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {C} mana ability). The animate ability is layered on
        // below — it isn't expressible in the current JSON
        // AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {4}: This land becomes a 4/2 Golem artifact creature until end of
        // turn. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost = {4}
        // (four generic mana), no tap rider. Resolution registers Layer 4
        // (add Creature + Artifact types and the Golem subtype, CR 613.1c)
        // + Layer 7b (set base P/T 4/2, CR 613.7b) continuous effects
        // flagged ExpiresAtEndOfTurn (CR 514.2 cleanup step).
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes {AnimatedPower}/{AnimatedToughness} Golem artifact creature until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature + Artifact types and the Golem
                // subtype. Printed Land type stays ("it's still a land").
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.Golem },
                    extraTypes: new[] { CardType.Artifact }));

                // Layer 7b — set base P/T 4/2.
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{4}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
