using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.9 — generic "you may cast this card from your graveyard"
/// alternative cost. Used by Lurrus of the Dream-Den ("During each of
/// your turns, you may cast one permanent spell with mana value 2 or
/// less from your graveyard"). Unlike <see cref="FlashbackAlternativeCost"/>,
/// the card returns to its default destination on resolution — for
/// permanents that means the battlefield, no post-resolution exile.
///
/// <para>
/// Once-per-turn / "permanent spell only" / mana-value gate are NOT
/// enforced here — those belong to the granting effect (e.g.
/// <see cref="Majik.Core.CardData.Factories.LurrusOfTheDreamDenFactory"/>'s
/// <c>LurrusGraveyardCastGate</c>). The alt-cost itself only checks the
/// zone + ownership invariants common to every "cast from graveyard"
/// alt cost, and delegates the rest to an optional
/// <see cref="IGraveyardCastGate"/> consulted at cast time and again at
/// resolution-bookkeeping time.
/// </para>
/// </summary>
public sealed class GraveyardCastAlternativeCost : IAlternativeCost
{
    public string Description { get; }
    public ManaCost AlternativeManaCost { get; }
    private readonly IGraveyardCastGate? _gate;

    public GraveyardCastAlternativeCost(
        string description,
        ManaCost cost,
        IGraveyardCastGate? gate = null)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        AlternativeManaCost = cost ?? throw new ArgumentNullException(nameof(cost));
        _gate = gate;
    }

    public bool CanCastFor(ICard card, Player caster)
    {
        if (card.Zone != ZoneType.Graveyard) return false;
        if (!ReferenceEquals(card.Owner, caster)) return false;
        if (_gate != null && !_gate.CanCast(card, caster)) return false;
        return true;
    }

    public void OnResolved(ICard card, Player caster)
    {
        // Default destination — the card moves to the battlefield (for
        // permanents) via the normal post-resolution path. The only
        // bookkeeping is to notify the optional gate that a cast was
        // performed, so once-per-turn predicates can tick.
        _gate?.NotePerformed(card, caster);
    }
}

/// <summary>
/// Hook interface consulted by <see cref="GraveyardCastAlternativeCost"/>.
/// Implementers encode the granting effect's specific predicate ("permanent
/// spell with mv 2 or less, once per your turn", etc.) and any post-cast
/// bookkeeping (turn counter increments, etc.).
/// </summary>
public interface IGraveyardCastGate
{
    /// <summary>Return true if the alt cost is currently legal for this
    /// caster + card. False short-circuits <see cref="GraveyardCastAlternativeCost.CanCastFor"/>.</summary>
    bool CanCast(ICard card, Player caster);

    /// <summary>Called from <see cref="GraveyardCastAlternativeCost.OnResolved"/>
    /// after the spell resolves. Used to consume a "once per turn" slot.</summary>
    void NotePerformed(ICard card, Player caster);
}
