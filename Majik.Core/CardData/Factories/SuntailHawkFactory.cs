using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Suntail Hawk (Eighth Edition, {W}).
///
/// Creature — Bird 1/1. Oracle text:
///   "Flying"
///
/// A 1/1 evasive body for one white mana — the quintessential white
/// one-drop flier. Suntail Hawk is a vanilla flier: no triggers, no
/// activated abilities, just the printed Flying keyword (CR 702.9).
///
/// ## Implementation
///
/// - 1/1 <see cref="Creature"/> — Bird, mana cost {W}.
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path
///   (<see cref="Majik.Core.Rules.CombatRules"/>) reads the marker
///   directly — same shape as <see cref="WelkinTernFactory"/>'s Flying /
///   <see cref="WindDrakeFactory"/>'s Flying.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point (mirrors Welkin Tern / Wind Drake).
/// </summary>
[CardName("Suntail Hawk")]
public static class SuntailHawkFactory
{
    public const string CardName = "Suntail Hawk";
    public const string PrintedManaCost = "{W}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Suntail Hawk — a {W} 1/1 Creature — Bird with the
    /// Flying keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Bird });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
