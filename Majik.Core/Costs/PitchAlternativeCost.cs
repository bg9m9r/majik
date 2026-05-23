using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.9 — "Pitch" alternative cost. The canonical Force-of-Will-cycle
/// pattern:
///
///   "If it's not your turn, you may exile a [color] card from your hand
///    rather than pay this spell's mana cost."
///
/// Force of Will additionally specifies "and pay 1 life" — this class
/// supports an optional life rider via <see cref="LifeCost"/>.
///
/// Differences vs. <see cref="ExileColoredCardAlternativeCost"/>:
///   * Adds a printed timing-restriction predicate (the "if it's not your
///     turn" clause) via <see cref="IsLegalInContext(Player)"/>. The
///     spell-cast flow checks this against the active player; the bot
///     probe filters candidates by it.
///   * Supports an optional life payment (Force of Will's +1 life rider).
///   * No mana is paid — <see cref="AlternativeManaCost"/> is
///     <see cref="ManaCost.Zero"/>; the exile (+ optional life loss) is
///     the entire cost.
///
/// Modern Horizons "incarnation" pitch (Solitude, Endurance, Fury, Grief,
/// Subtlety) is a related-but-distinct mechanic — those exile-a-card
/// payments are folded into Evoke (<see cref="EvokeAlternativeCost"/>)
/// rather than this Force-style pitch, because the incarnation cycle
/// triggers a sacrifice on ETB. This class is reserved for the
/// not-your-turn pitch cycle.
/// </summary>
public sealed class PitchAlternativeCost : IAlternativeCost
{
    /// <summary>The required color of the exiled hand card.</summary>
    public ManaColor RequiredColor { get; }

    /// <summary>The card the caster chose to pitch.</summary>
    public ICard ExiledCard { get; }

    /// <summary>Life payment rider. 0 for plain pitch (Force of Negation),
    /// 1 for Force of Will. Paid in <see cref="OnResolved"/>.</summary>
    public int LifeCost { get; }

    public string Description =>
        LifeCost > 0
            ? $"Pitch — Exile a {RequiredColor} card from your hand and pay {LifeCost} life"
            : $"Pitch — Exile a {RequiredColor} card from your hand";

    /// <summary>No mana is paid. CR 118.9.</summary>
    public ManaCost AlternativeManaCost => ManaCost.Zero;

    public PitchAlternativeCost(ManaColor requiredColor, ICard exiledCard, int lifeCost = 0)
    {
        if (lifeCost < 0) throw new ArgumentOutOfRangeException(nameof(lifeCost));
        RequiredColor = requiredColor;
        ExiledCard = exiledCard ?? throw new ArgumentNullException(nameof(exiledCard));
        LifeCost = lifeCost;
    }

    /// <summary>
    /// Validation that doesn't depend on whose turn it is. The caster must
    /// own the pitched card, it must be in hand, of the required color, and
    /// not be the spell being cast. <see cref="SpellCastFlow"/> additionally
    /// calls <see cref="IsLegalInContext(Player)"/> to gate on the active
    /// player.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (!ReferenceEquals(ExiledCard.Owner, caster)) return false;
        if (ExiledCard.Zone != ZoneType.Hand) return false;
        if (ReferenceEquals(ExiledCard, card)) return false;
        if (!CardColors.GetColors(ExiledCard).Contains(RequiredColor)) return false;
        if (LifeCost > 0 && caster.LifeTotal <= 0) return false;
        return true;
    }

    /// <summary>
    /// "If it's not your turn" timing gate. Returns true when
    /// <paramref name="activePlayer"/> is NOT the caster (i.e. the caster is
    /// on an opponent's turn). Called by <see cref="Majik.Core.Game.SpellCastFlow"/>
    /// after the standard <see cref="CanCastFor"/> check.
    /// </summary>
    public bool IsLegalInContext(Player activePlayer)
    {
        if (activePlayer == null) return false;
        return !ReferenceEquals(activePlayer, ExiledCard.Owner);
    }

    /// <summary>
    /// Apply the pitch payment after the spell resolves: exile the chosen
    /// hand card (CR 701.21) and pay the optional life rider (CR 118.8).
    /// Idempotent — safe if the pitched card has already moved elsewhere.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        if (ExiledCard.Zone == ZoneType.Hand)
        {
            caster.Zones.Hand.RemoveCard(ExiledCard);
            caster.Zones.Exile.AddCard(ExiledCard);
            ExiledCard.SetZone(ZoneType.Exile);
        }
        if (LifeCost > 0)
        {
            caster.LoseLife(LifeCost);
        }
    }
}
