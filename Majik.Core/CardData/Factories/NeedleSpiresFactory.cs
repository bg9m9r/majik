using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Needle Spires (Oath of the Gatewatch).
///
/// Land. Oracle text:
///   "Needle Spires enters tapped.
///    {T}: Add {R} or {W}.
///    {1}{R}{W}: Until end of turn, Needle Spires becomes a 2/1 red and
///    white Elemental creature with double strike. It's still a land."
///
/// See <see cref="ManlandCycleAnimateEffect"/> /
/// <see cref="ManlandCycleBecomesPTEffect"/> for the shared v1 layer-system
/// posture used across the Worldwake / BFZ / OGW manland cycle.
/// </summary>
[CardName("Needle Spires")]
public static class NeedleSpiresFactory
{
    public const string CardName = "Needle Spires";

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

        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("R")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));

        var animateEffect = new Effect(
            $"{CardName}: becomes 2/1 red and white Elemental creature with double strike until EOT (still a land)",
            () =>
            {
                if (effects == null) return;
                effects.Register(new ManlandCycleAnimateEffect(
                    land, new[] { "Double Strike" }));
                effects.Register(new ManlandCycleBecomesPTEffect(land, 2, 1));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{R}{W}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
