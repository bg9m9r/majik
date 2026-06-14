using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Soulstone Sanctuary (Modern Horizons 3 manland).
///
/// Land.
/// Oracle text (verified Scryfall 2026-06-13):
///   "{T}: Add {C}.
///    {4}: This land becomes a 3/3 creature with vigilance and all creature
///    types. It's still a land."
///
/// The "all creature types" sibling of <see cref="MutavaultFactory"/> /
/// <see cref="FacelessHavenFactory"/> — but its animate has <b>no "until end
/// of turn" clause</b>, so the animation is <b>permanent</b> (CR 613.1c — it
/// is a continuous effect with no duration; it lasts as long as the land is on
/// the battlefield, not just until cleanup). The continuous effects are
/// therefore registered with <c>expiresAtEndOfTurn: false</c>.
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes) + <c>{T}: Add {C}</c> mana
///   ability (CR 605.1, no stack).
/// - <b>{4}: become a 3/3 every-creature-type creature with vigilance</b> —
///   wired as an <see cref="ActivatedAbility"/> with a
///   <see cref="ManaCostCost"/> of <c>{4}</c>. Resolution registers two
///   PERMANENT (non-EOT) continuous effects:
///     - Layer 4 (<see cref="ManlandCycleAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/>, every creature subtype the engine
///       models (<see cref="MutavaultAnimateEffect.EveryCreatureType"/>,
///       CR 205.3m "all creature types"), and a Vigilance keyword marker
///       (CR 702.20). The printed Land type stays ("It's still a land",
///       CR 613.1c).
///     - Layer 7b (<see cref="ManlandCycleBecomesPTEffect"/>) — set-base
///       P/T 3/3 (CR 613.7b).
///
/// ## "Every creature type" simplification (v1 gap)
/// Same v1 equivalent as Mutavault — grants every creature subtype currently
/// enumerated in <see cref="CardSubtype"/>; the set auto-grows with the enum.
///
/// <para>Lands are never routed through their <c>[CardName]</c> factory in
/// production (the factory instance-swap is gated on <c>!HasType(Land)</c>);
/// the live-match animate ability is bound by <see cref="ManlandBinder"/>
/// (which now recognises the "all creature types" + permanent-animate shape).
/// This factory provides the (test-only) dispatch + the <c>IsImplemented</c>
/// flip.</para>
/// </summary>
[CardName("Soulstone Sanctuary")]
public static class SoulstoneSanctuaryFactory
{
    public const string CardName = "Soulstone Sanctuary";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Soulstone Sanctuary with no
    /// <see cref="ContinuousEffectsService"/> wired (shape-only path the
    /// <see cref="NamedCardFactory"/> dispatcher uses).
    /// </summary>
    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct a fully-wired Soulstone Sanctuary.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 / 7b
    /// registration of the (permanent) animate ability. May be null — the
    /// ability still resolves but no continuous effect is recorded.</param>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // {T}: Add {C} — CR 605.1 mana ability (no stack).
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // {4}: This land becomes a 3/3 creature with vigilance and all creature
        // types. It's still a land. (No "until end of turn" → permanent.)
        var animateEffect = new Effect(
            $"{CardName}: becomes a 3/3 every-creature-type creature with vigilance (permanent; still a land)",
            () =>
            {
                if (effects == null) return; // shape-only path

                // Layer 4 — Creature type + every creature subtype + Vigilance,
                // permanent (no EOT expiry). Printed Land stays.
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: new[] { "Vigilance" },
                    subtypes: MutavaultAnimateEffect.EveryCreatureType,
                    extraTypes: null,
                    expiresAtEndOfTurn: false));

                // Layer 7b — set base P/T 3/3, permanent.
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, Power, Toughness, expiresAtEndOfTurn: false));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{4}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
