using System.Linq;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.9 — "You may pay {N} rather than pay this spell's mana cost if there
/// are thirteen or more creatures on the battlefield." The Blasphemous Edict
/// pattern: a fixed-mana alternative cost gated on a GLOBAL battlefield count
/// (CR 109.4 — "on the battlefield" with no controller qualifier counts every
/// creature, regardless of who controls it).
///
/// Differs from <see cref="SpectacleAlternativeCost"/> (also a fixed-mana
/// conditional alt cost) only in the legality predicate: spectacle keys on an
/// opponent's life loss this turn, whereas this keys on the total number of
/// creatures on every player's battlefield reaching
/// <see cref="RequiredCreatures"/>.
///
/// The creature count reads the live player set off
/// <see cref="GamePlayersRegistry.AllPlayers"/> at announce time (the
/// production casting path supplies no player list to <see cref="CanCastFor"/>),
/// with an optional constructor-injected player list used by unit tests. No new
/// engine mechanic — it composes the existing fixed-mana alt-cost plumbing
/// (<see cref="SpectacleAlternativeCost"/>) with a battlefield head-count.
/// </summary>
public sealed class PayManaIfThirteenCreaturesAlternativeCost : IAlternativeCost
{
    /// <summary>Blasphemous Edict's threshold — thirteen creatures (CR 118.9).</summary>
    public const int DefaultRequiredCreatures = 13;

    private readonly IReadOnlyList<Player>? _players;

    /// <summary>The minimum number of creatures on the battlefield that makes
    /// this alternative cost available (thirteen for Blasphemous Edict).</summary>
    public int RequiredCreatures { get; }

    /// <inheritdoc/>
    public ManaCost AlternativeManaCost { get; }

    /// <inheritdoc/>
    public string Description =>
        $"Pay {AlternativeManaCost} (if there are {RequiredCreatures}+ creatures on the battlefield)";

    /// <param name="alternativeManaCost">The fixed mana paid in lieu of the
    /// printed cost (<c>{B}</c> for Blasphemous Edict).</param>
    /// <param name="requiredCreatures">Threshold creature count
    /// (<see cref="DefaultRequiredCreatures"/> = 13).</param>
    /// <param name="players">Optional explicit player list whose battlefields
    /// are counted (unit tests). When null, <see cref="CanCastFor"/> reads the
    /// live player set off <see cref="GamePlayersRegistry.AllPlayers"/> — the
    /// production path.</param>
    public PayManaIfThirteenCreaturesAlternativeCost(
        ManaCost alternativeManaCost,
        int requiredCreatures = DefaultRequiredCreatures,
        IReadOnlyList<Player>? players = null)
    {
        AlternativeManaCost = alternativeManaCost
            ?? throw new ArgumentNullException(nameof(alternativeManaCost));
        if (requiredCreatures < 0)
            throw new ArgumentOutOfRangeException(nameof(requiredCreatures),
                "Required creature count must be non-negative.");
        RequiredCreatures = requiredCreatures;
        _players = players;
    }

    /// <summary>
    /// CR 118.9 — available iff (a) the card is in the caster's hand
    /// (CR 601.2 — an alternative cost does not relax the casting zone) and
    /// (b) there are <see cref="RequiredCreatures"/> or more creatures across
    /// every battlefield (CR 109.4 — "on the battlefield" counts all players'
    /// creatures, the caster's included).
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (caster == null) return false;
        if (card.Zone != ZoneType.Hand) return false;
        if (!ReferenceEquals(card.Owner, caster)) return false;

        var players = _players is { Count: > 0 }
            ? _players
            : GamePlayersRegistry.AllPlayers;
        if (players == null || players.Count == 0) return false;

        int creatures = 0;
        foreach (var pl in players)
        {
            if (pl == null) continue;
            foreach (var c in pl.Zones.Battlefield.GetCards())
            {
                if (c.HasType(CardType.Creature)) creatures++;
            }
        }
        return creatures >= RequiredCreatures;
    }

    /// <summary>No post-resolution side-effect: the fixed mana is paid as the
    /// spell's cost and the card resolves to the graveyard normally
    /// (CR 118.9 imposes no resolution hook). Mirrors
    /// <see cref="SpectacleAlternativeCost.OnResolved"/>.</summary>
    public void OnResolved(ICard card, Player caster)
    {
        // intentionally empty
    }
}
