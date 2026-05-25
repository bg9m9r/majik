using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hissing Quagmire (Oath of the Gatewatch).
///
/// Land. Oracle text:
///   "Hissing Quagmire enters tapped.
///    {T}: Add {B} or {G}.
///    {1}{B}{G}: Until end of turn, Hissing Quagmire becomes a 2/2 black
///    and green Elemental creature with deathtouch. It's still a land."
///
/// See <see cref="ManlandCycleAnimateEffect"/> /
/// <see cref="ManlandCycleBecomesPTEffect"/> for the shared v1 layer-system
/// posture used across the Worldwake / BFZ / OGW manland cycle.
/// </summary>
[CardName("Hissing Quagmire")]
public static class HissingQuagmireFactory
{
    public const string CardName = "Hissing Quagmire";

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

        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("G")));

        var animateEffect = new Effect(
            $"{CardName}: becomes 2/2 black and green Elemental creature with deathtouch until EOT (still a land)",
            () =>
            {
                if (effects == null) return;
                effects.Register(new ManlandCycleAnimateEffect(
                    land, new[] { "Deathtouch" }));
                effects.Register(new ManlandCycleBecomesPTEffect(land, 2, 2));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{B}{G}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
