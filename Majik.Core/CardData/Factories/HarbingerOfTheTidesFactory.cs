using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Harbinger of the Tides (Magic Origins,
/// Creature — Merfolk Wizard {U}{U} 2/2).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "You may cast this spell as though it had flash if you pay {2} more to
///    cast it. (You may cast it any time you could cast an instant.)
///    When this creature enters, you may return target tapped creature an
///    opponent controls to its owner's hand."
///
/// ## Shape source
/// Card identity (name, {U}{U}, 2/2, Merfolk Wizard) plus the ETB "you may
/// return target tapped creature an opponent controls to its owner's hand"
/// trigger are fully declarative JSON in
/// <c>Majik.Core/CardData/Cards/harbinger-of-the-tides.json</c> — an
/// <c>etb_self</c> trigger (CR 603.6a) carrying an OPTIONAL
/// (<c>"optional": true</c>) <c>return_to_hand</c> bounce (CR 701.20) over the
/// <c>tapped_creature_opponent_controls</c> target filter (CR 109.5).
///
/// ## Flash alt-cost permission (CR 601.2b / 702.8)
/// The "cast as though it had flash if you pay {2} more" line is the
/// alternative-cost permission paid down by this deferral. It is NOT a printed
/// Flash keyword (the card has no flash for its {U}{U} cost — that's still
/// sorcery speed for a creature, CR 302.1). Instead, the caller opts into a
/// flash casting window by casting through <see cref="BuildFlashAlternativeCost"/>:
/// a <see cref="FlashAlternativeCost"/> whose mana cost is the printed
/// {U}{U} plus the {2} surcharge ({2}{U}{U}). Because
/// <see cref="Majik.Core.Game.SpellCastFlow"/> skips the CR 117.1 sorcery-speed
/// gate whenever an alternative cost is supplied, casting for this alt cost is
/// legal at instant speed — exactly the printed "any time you could cast an
/// instant" window (CR 601.2b).
/// </summary>
[CardName("Harbinger of the Tides")]
public static class HarbingerOfTheTidesFactory
{
    public const string CardName = "Harbinger of the Tides";
    public const string PrintedManaCost = "{U}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>CR 601.2b — the flat generic surcharge ("{2} more") that
    /// purchases the flash casting window.</summary>
    public const int FlashSurcharge = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("harbinger-of-the-tides");

    /// <summary>
    /// Construct Harbinger of the Tides owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }

    /// <summary>
    /// CR 601.2b / 702.8 — build the flash alternative cost: the printed
    /// {U}{U} plus the {2} surcharge ({2}{U}{U}). Pass this to
    /// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/> as the
    /// <c>alternativeCost</c> to cast Harbinger at instant speed.
    /// </summary>
    public static FlashAlternativeCost BuildFlashAlternativeCost() =>
        new(ManaCost.Parse(PrintedManaCost), FlashSurcharge);
}
