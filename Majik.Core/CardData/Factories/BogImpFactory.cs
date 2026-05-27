using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bog Imp (The Dark, {1}{B}).
///
/// Creature — Imp 1/1. Oracle text:
///   "Flying"
///
/// ## Implemented (v1)
/// - 1/1 Creature — Imp at {1}{B}, owner/controller wired.
/// - <b>Flying</b> (CR 702.9) — wired as a <see cref="KeywordAbility"/>
///   marker; read by CombatAbilities.HasFlying and the evasion
///   enforcement path.
///
/// No other abilities — Bog Imp is a vanilla flyer.
/// </summary>
[CardName("Bog Imp")]
public static class BogImpFactory
{
    public const string CardName = "Bog Imp";
    public const string PrintedManaCost = "{1}{B}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Bog Imp. Suitable for shape / dispatcher tests and
    /// runtime use alike — Flying is wired unconditionally, no additional
    /// services required.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Imp });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying keyword marker.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
