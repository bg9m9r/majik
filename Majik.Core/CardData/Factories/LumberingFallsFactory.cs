using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lumbering Falls (Battle for Zendikar).
///
/// Land. Oracle text:
///   "Lumbering Falls enters tapped.
///    {T}: Add {G} or {U}.
///    {1}{G}{U}: Until end of turn, Lumbering Falls becomes a 3/3 green
///    and blue Elemental creature with hexproof. It's still a land."
///
/// See <see cref="ManlandCycleAnimateEffect"/> /
/// <see cref="ManlandCycleBecomesPTEffect"/> for the shared v1 layer-system
/// posture used across the Worldwake / BFZ / OGW manland cycle. Hexproof
/// is granted as a keyword string ("Hexproof"); the engine's
/// <see cref="Majik.Core.Targeting.TargetLegality"/> v1 reads bare
/// "Hexproof" from <see cref="ContinuousEffectsService.Compute"/> when a
/// service is wired (CR 702.11).
/// </summary>
[CardName("Lumbering Falls")]
public static class LumberingFallsFactory
{
    public const string CardName = "Lumbering Falls";

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
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("U")));

        var animateEffect = new Effect(
            $"{CardName}: becomes 3/3 green and blue Elemental creature with hexproof until EOT (still a land)",
            () =>
            {
                if (effects == null) return;
                effects.Register(new ManlandCycleAnimateEffect(
                    land, new[] { "Hexproof" }));
                effects.Register(new ManlandCycleBecomesPTEffect(land, 3, 3));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{G}{U}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
