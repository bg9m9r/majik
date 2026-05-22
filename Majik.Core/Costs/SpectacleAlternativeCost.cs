using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.118 — Spectacle. "You may cast this spell for its spectacle cost
/// rather than its mana cost if an opponent lost life this turn."
///
/// Eligibility (CR 702.118a):
///   • Card is in the caster's hand (the default casting zone — spectacle
///     does not relax the zone restriction).
///   • At least one of the supplied <see cref="_opponents"/> has
///     <see cref="Player.LifeLostThisTurn"/> &gt; 0 at announce time
///     (CR 702.118a checks the condition only as part of casting; once
///     legal, the cost lock-in survives a later life-gain — see
///     <see cref="SpellCastFlow"/> which calls <see cref="CanCastFor"/>
///     exactly once during cost determination).
///
/// No post-resolution side-effect; the card goes to the graveyard normally
/// like any instant/sorcery.
/// </summary>
public sealed class SpectacleAlternativeCost : IAlternativeCost
{
    private readonly IReadOnlyList<Player> _opponents;

    public string Description => $"Spectacle {AlternativeManaCost}";

    /// <summary>The spectacle cost (e.g. {R} for Skewer the Critics).</summary>
    public ManaCost AlternativeManaCost { get; }

    /// <param name="spectacleCost">The alternative cost printed after the
    /// "Spectacle" keyword (e.g. <c>ManaCost.Parse("R")</c> for Skewer).</param>
    /// <param name="opponents">The caster's opponents — checked at cast
    /// time for any non-zero <see cref="Player.LifeLostThisTurn"/>. The
    /// caster is intentionally excluded; spectacle does not key on the
    /// caster's own life loss (CR 702.118a — "an opponent").</param>
    public SpectacleAlternativeCost(ManaCost spectacleCost, IReadOnlyList<Player> opponents)
    {
        AlternativeManaCost = spectacleCost ?? throw new ArgumentNullException(nameof(spectacleCost));
        _opponents = opponents ?? throw new ArgumentNullException(nameof(opponents));
    }

    /// <summary>
    /// Legal iff (a) the card is in the caster's hand (CR 601.2 zone rule),
    /// and (b) any opponent of the caster has lost life this turn
    /// (CR 702.118a). The <paramref name="caster"/> argument is unused for
    /// opponent enumeration because the constructor takes the resolved
    /// opponent list — callers (binder/dispatcher) know the game shape and
    /// pass only the relevant non-caster players.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (card.Zone != ZoneType.Hand) return false;
        if (!ReferenceEquals(card.Owner, caster)) return false;
        foreach (var opp in _opponents)
        {
            if (ReferenceEquals(opp, caster)) continue;
            if (opp.LifeLostThisTurn > 0) return true;
        }
        return false;
    }

    /// <summary>No-op: spectacle has no post-resolution side-effect; the
    /// card resolves like any other instant/sorcery (graveyard via the
    /// engine's normal disposition).</summary>
    public void OnResolved(ICard card, Player caster)
    {
        // intentionally empty (CR 702.118 imposes no resolution hook)
    }
}
