using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Damnation (Planar Chaos, {2}{B}{B}).
///
/// Sorcery. Oracle text:
///   "Destroy all creatures. They can't be regenerated."
///
/// ## Implementation
///
/// Functional reprint of <see cref="WrathOfGodFactory"/> in black —
/// same resolve effect (sweep every creature on every player's
/// battlefield to its owner's graveyard) at a different mana cost. The
/// resolve effect is delegated to
/// <see cref="WrathOfGodFactory.BuildResolveEffect"/> verbatim so the
/// two cards stay observationally identical.
///
/// ## v1 simplifications
/// Inherited from <see cref="WrathOfGodFactory.BuildResolveEffect"/> —
/// "can't be regenerated" is a no-op rider (no CR 701.15 plumbing) and
/// <see cref="Majik.Core.Abilities.Keywords.KeywordType.Indestructible"/>
/// bypass is also lossy (no CR 702.12 surface on
/// <see cref="OracleSpellBinder.MoveToGraveyard"/>).
/// </summary>
public static class DamnationFactory
{
    public const string CardName = "Damnation";
    public const string PrintedManaCost = "{2}{B}{B}";

    /// <summary>
    /// Build a Damnation sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — wire the resolve
    /// effect via <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Damnation's resolve effect — identical to
    /// <see cref="WrathOfGodFactory.BuildResolveEffect"/>.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers) =>
        WrathOfGodFactory.BuildResolveEffect(allPlayers);
}
