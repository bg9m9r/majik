using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Celestial Colonnade (Worldwake).
///
/// Land. Oracle text:
///   "Celestial Colonnade enters tapped.
///    {T}: Add {W} or {U}.
///    {3}{W}{U}: Until end of turn, Celestial Colonnade becomes a 4/4
///    white and blue Elemental creature with flying and vigilance. It's
///    still a land."
///
/// See <see cref="ManlandCycleAnimateEffect"/> /
/// <see cref="ManlandCycleBecomesPTEffect"/> for the shared v1 layer-system
/// posture used across the Worldwake / BFZ / OGW manland cycle.
/// </summary>
[CardName("Celestial Colonnade")]
public static class CelestialColonnadeFactory
{
    public const string CardName = "Celestial Colonnade";

    /// <summary>
    /// Construct Celestial Colonnade with no
    /// <see cref="ContinuousEffectsService"/> or
    /// <see cref="ReplacementBus"/> wired (shape-only path).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct Celestial Colonnade.
    /// </summary>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ETB-tapped (CR 614.1c).
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // {T}: Add {W} / {T}: Add {U} — CR 605.1 mana abilities, no stack.
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("U")));

        // {3}{W}{U}: animate to 4/4 W/U Elemental with Flying + Vigilance EOT.
        var animateEffect = new Effect(
            $"{CardName}: becomes 4/4 white and blue Elemental creature with flying and vigilance until EOT (still a land)",
            () =>
            {
                if (effects == null) return;
                effects.Register(new ManlandCycleAnimateEffect(
                    land, new[] { "Flying", "Vigilance" }));
                effects.Register(new ManlandCycleBecomesPTEffect(land, 4, 4));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{3}{W}{U}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
