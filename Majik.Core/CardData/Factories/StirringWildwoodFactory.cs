using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stirring Wildwood (Worldwake).
///
/// Land. Oracle text:
///   "Stirring Wildwood enters tapped.
///    {T}: Add {G} or {W}.
///    {1}{G}{W}: Until end of turn, Stirring Wildwood becomes a 3/4 green
///    and white Elemental creature with reach. It's still a land."
///
/// See <see cref="ManlandCycleAnimateEffect"/> /
/// <see cref="ManlandCycleBecomesPTEffect"/> for the shared v1 layer-system
/// posture used across the Worldwake / BFZ / OGW manland cycle.
/// </summary>
[CardName("Stirring Wildwood")]
public static class StirringWildwoodFactory
{
    public const string CardName = "Stirring Wildwood";

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

        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("G")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));

        var animateEffect = new Effect(
            $"{CardName}: becomes 3/4 green and white Elemental creature with reach until EOT (still a land)",
            () =>
            {
                if (effects == null) return;
                effects.Register(new ManlandCycleAnimateEffect(
                    land, new[] { "Reach" }));
                effects.Register(new ManlandCycleBecomesPTEffect(land, 3, 4));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{G}{W}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
