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

    /// <summary>
    /// CR 118.8 — optional life rider paid in addition to the mana cost.
    /// 0 for plain graveyard casts (Lurrus / Yawgmoth's Will, where the
    /// alt cost is just "play it from the graveyard for its mana cost").
    /// &gt; 0 for grants that tack on a life payment — Festival of Embers'
    /// "by paying 1 life in addition to their other costs". Paid in
    /// <see cref="OnResolved"/>, mirroring <see cref="PitchAlternativeCost.LifeCost"/>.
    /// </summary>
    public int LifeCost { get; }

    public GraveyardCastAlternativeCost(
        string description,
        ManaCost cost,
        IGraveyardCastGate? gate = null,
        int lifeCost = 0)
    {
        if (lifeCost < 0) throw new ArgumentOutOfRangeException(nameof(lifeCost));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        AlternativeManaCost = cost ?? throw new ArgumentNullException(nameof(cost));
        _gate = gate;
        LifeCost = lifeCost;
    }

    public bool CanCastFor(ICard card, Player caster)
    {
        if (card.Zone != ZoneType.Graveyard) return false;
        if (!ReferenceEquals(card.Owner, caster)) return false;
        // CR 118.4 — a player can't announce a cost they can't pay. The mana
        // cost here is the card's printed cost (Festival adds the life rider
        // "in addition to their other costs"), so the life rider gate is the
        // only extra affordability check this alt cost owns.
        if (LifeCost > 0 && caster.LifeTotal < LifeCost) return false;
        if (_gate != null && !_gate.CanCast(card, caster)) return false;
        return true;
    }

    public void OnResolved(ICard card, Player caster)
    {
        // Default destination — the card moves to the battlefield (for
        // permanents) via the normal post-resolution path.
        // CR 118.8 — pay the optional life rider (Festival of Embers' +1 life).
        if (LifeCost > 0)
        {
            caster.LoseLife(LifeCost);
        }
        // Notify the optional gate that a cast was performed, so once-per-turn
        // predicates can tick.
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
