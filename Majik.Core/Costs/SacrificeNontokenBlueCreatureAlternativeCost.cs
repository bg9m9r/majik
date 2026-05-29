using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.9 — "Flare" alternative cost.
///
///   "You may sacrifice a nontoken blue creature rather than pay this
///    spell's mana cost."
///
/// Oracle pattern used by Flare of Denial (MH3). The caster chooses one
/// nontoken blue creature they control on the battlefield and sacrifices it
/// instead of paying the spell's printed mana cost. Unlike the Force-of-Will
/// pitch there is no timing restriction ("not your turn") — this alternative
/// cost is available whenever the spell is otherwise castable.
///
/// Post-resolution (<see cref="OnResolved"/>): the chosen creature moves
/// Battlefield → Graveyard (sacrifice, CR 701.18).
/// </summary>
public sealed class SacrificeNontokenBlueCreatureAlternativeCost : IAlternativeCost
{
    /// <summary>The nontoken blue creature the caster chose to sacrifice.</summary>
    public Permanent SacrificedCreature { get; }

    /// <inheritdoc/>
    public string Description =>
        $"Sacrifice nontoken blue creature ({SacrificedCreature.Name}) instead of paying mana cost";

    /// <inheritdoc/>
    /// <remarks>No mana is paid — CR 118.9. The sacrifice is the entire cost.</remarks>
    public ManaCost AlternativeManaCost => ManaCost.Zero;

    public SacrificeNontokenBlueCreatureAlternativeCost(Permanent sacrificedCreature)
    {
        SacrificedCreature = sacrificedCreature
            ?? throw new ArgumentNullException(nameof(sacrificedCreature));
    }

    /// <summary>
    /// Validation: the chosen creature must be on the battlefield, be a
    /// nontoken blue creature, be controlled by the caster (CR 701.18a),
    /// and must not be the spell card being cast (which is already leaving
    /// the hand).
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (caster == null) return false;
        if (!ReferenceEquals(SacrificedCreature.Controller, caster)) return false;
        if (SacrificedCreature.Zone != ZoneType.Battlefield) return false;
        if (!SacrificedCreature.HasType(CardType.Creature)) return false;
        if (SacrificedCreature.IsToken) return false;
        if (!CardColors.GetColors(SacrificedCreature).Contains(ManaColor.Blue)) return false;
        // The spell card being cast is not on the battlefield, so we don't
        // need to guard against self-sacrifice (different from additional costs).
        return true;
    }

    /// <summary>
    /// Apply the sacrifice after the spell resolves: move the chosen creature
    /// from the battlefield to the graveyard (CR 701.18).
    /// Idempotent — safe if the creature has already left the battlefield.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        if (SacrificedCreature.Zone != ZoneType.Battlefield) return;

        caster.Zones.Battlefield.RemoveCard(SacrificedCreature);
        caster.Zones.Graveyard.AddCard(SacrificedCreature);
        SacrificedCreature.SetZone(ZoneType.Graveyard);
    }
}
