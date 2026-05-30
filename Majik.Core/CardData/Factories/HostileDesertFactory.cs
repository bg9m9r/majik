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
/// Named-card factory for Hostile Desert (Hour of Devastation). Land — Desert.
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "{T}: Add {C}.
///    {2}, Exile a land card from your graveyard: This land becomes a 3/4
///    Elemental creature until end of turn. It's still a land."
///
/// A colourless member of the animate-until-EOT "manland" family used by
/// <see cref="RagingRavineFactory"/> / <see cref="RestlessBivouacFactory"/>,
/// but stripped down: <b>no</b> ETB-tapped rider, <b>no</b> attack trigger, a
/// single <c>{T}: Add {C}</c> mana ability, and a hybrid activation cost —
/// the generic {2} (a <see cref="ManaCostCost"/>) plus the non-mana
/// "Exile a land card from your graveyard" (an
/// <see cref="ExileLandCardFromGraveyardCost"/>, CR 602.1 / 118.4).
///
/// On resolution (CR 602 — ordinary activated ability, uses the stack) the
/// animate effect registers a <see cref="ManlandCycleAnimateEffect"/> (Layer 4
/// — add Creature type + Elemental subtype; printed Land type stays, CR 613.1c
/// "It's still a land") and a <see cref="ManlandCycleBecomesPTEffect"/>
/// (Layer 7b — set base P/T 3/4), both flagged
/// <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so
/// <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2 cleanup
/// step) lifts the animation.
///
/// The base shape (Land + Desert subtype + the {C} mana ability) is
/// materialised from the embedded JSON definition (<c>hostile-desert.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the animate ability is layered on
/// here because the JSON <c>AbilityDefinition</c> schema expresses neither the
/// hybrid activation cost nor the layer-effect resolution (same posture as
/// <see cref="RestlessBivouacFactory"/>).
///
/// ## v1 posture (shared with the manland cycle)
/// - <b>Combat math through Compute</b> — same gap as every other manland:
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
///   <see cref="PermanentCharacteristics"/> row for a Land runtime instance,
///   so the 3/4 is recorded for inspection but doesn't surface for combat
///   resolution yet. The Elemental subtype + Creature type DO apply via
///   Compute.
/// - <b>Deterministic exile pick</b> — <see cref="ExileLandCardFromGraveyardCost"/>
///   exiles the first land card in the graveyard (no agent prompt yet, same
///   posture as <see cref="ExileCardsFromGraveyardAdditionalCost"/>).
/// </summary>
[CardName("Hostile Desert")]
public static class HostileDesertFactory
{
    public const string CardName = "Hostile Desert";
    public const string Slug = "hostile-desert";
    public const int AnimatedPower = 3;
    public const int AnimatedToughness = 4;

    /// <summary>
    /// Construct Hostile Desert with no <see cref="ContinuousEffectsService"/>
    /// wired. The {C} mana ability (from JSON) + the animate ability are
    /// attached so the card surface is complete; the layer effects are not
    /// registered (the animate effect runs as a no-op shape-only path). This
    /// is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Hostile Desert.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 + Layer 7b
    /// registration of the animate ability. May be null — the ability still
    /// resolves but no continuous effects are recorded.</param>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // Desert subtype, {T}: Add {C} mana ability). The animate ability is
        // layered on below — its hybrid cost + layer resolution are not
        // expressible in the current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {2}, Exile a land card from your graveyard: This land becomes a
        // 3/4 Elemental creature until end of turn. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost is the
        // generic {2} (ManaCostCost) plus the non-mana "Exile a land card
        // from your graveyard" (ExileLandCardFromGraveyardCost, CR 602.1).
        // Resolution registers Layer 4 + Layer 7b continuous effects flagged
        // ExpiresAtEndOfTurn (CR 514.2).
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes {AnimatedPower}/{AnimatedToughness} Elemental creature until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Elemental subtype. Printed
                // Land type stays ("it's still a land", CR 613.1c).
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.Elemental },
                    extraTypes: null));

                // Layer 7b — set base P/T 3/4.
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                new ExileLandCardFromGraveyardCost(),
            },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
