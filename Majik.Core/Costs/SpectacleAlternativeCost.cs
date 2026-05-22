using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.139 — Spectacle. If an opponent lost life this turn, the caster
/// may pay the spectacle cost INSTEAD of the printed mana cost. Like
/// flashback, this is a Rule 118.9 alternative cost; legality depends on
/// game state at cast time.
///
/// The "opponent lost life this turn" check is delegated to a callback so
/// this class doesn't need to know about per-turn life-loss tracking
/// infrastructure (which doesn't live on <see cref="Player"/> yet). Callers
/// supply a predicate that returns true iff at least one opponent of the
/// caster has lost life during the current turn.
/// </summary>
public sealed class SpectacleAlternativeCost : IAlternativeCost
{
    private readonly Func<Player, bool> _opponentLostLifeThisTurn;

    public string Description => $"Spectacle {AlternativeManaCost}";
    public ManaCost AlternativeManaCost { get; }

    public SpectacleAlternativeCost(ManaCost spectacleCost, Func<Player, bool> opponentLostLifeThisTurn)
    {
        AlternativeManaCost = spectacleCost ?? throw new ArgumentNullException(nameof(spectacleCost));
        _opponentLostLifeThisTurn = opponentLostLifeThisTurn
            ?? throw new ArgumentNullException(nameof(opponentLostLifeThisTurn));
    }

    /// <summary>
    /// Spectacle is castable from hand, by the card's owner, only when an
    /// opponent lost life this turn (CR 702.139).
    /// </summary>
    public bool CanCastFor(ICard card, Player caster) =>
        card.Zone == ZoneType.Hand
        && ReferenceEquals(card.Owner, caster)
        && _opponentLostLifeThisTurn(caster);

    public void OnResolved(ICard card, Player caster)
    {
        // No special destination — instant/sorcery resolves to graveyard
        // via StackResolver's default. Spectacle is a cost gate only.
    }
}
