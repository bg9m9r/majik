using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shambling Vent (Battle for Zendikar).
///
/// Land. Oracle text:
///   "Shambling Vent enters tapped.
///    {T}: Add {W} or {B}.
///    {1}{W}{B}: Until end of turn, Shambling Vent becomes a 2/3 white and
///    black Elemental creature with lifelink. It's still a land."
///
/// See <see cref="ManlandCycleAnimateEffect"/> /
/// <see cref="ManlandCycleBecomesPTEffect"/> for the shared v1 layer-system
/// posture used across the Worldwake / BFZ / OGW manland cycle.
/// </summary>
[CardName("Shambling Vent")]
public static class ShamblingVentFactory
{
    public const string CardName = "Shambling Vent";

    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));

        var animateEffect = new Effect(
            $"{CardName}: becomes 2/3 white and black Elemental creature with lifelink until EOT (still a land)",
            () =>
            {
                if (effects == null) return;
                effects.Register(new ManlandCycleAnimateEffect(
                    land, new[] { "Lifelink" }));
                effects.Register(new ManlandCycleBecomesPTEffect(land, 2, 3));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{W}{B}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
