using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Welkin Tern (Magic 2014, {U}).
///
/// Creature — Bird 2/1. Oracle text:
///   "Flying"
///
/// A 2/1 evasive body for one blue mana — the classic mono-blue tempo
/// one-drop alongside Mausoleum Wanderer / Spectral Sailor. Welkin Tern
/// is purely a vanilla flier: no triggers, no activated abilities, just
/// the printed Flying keyword (CR 702.9).
///
/// ## Implementation
///
/// - 2/1 <see cref="Creature"/> — Bird, mana cost {U}.
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker. The combat block-restriction path
///   (<see cref="Majik.Core.Rules.CombatRules"/>) reads the marker
///   directly — same shape as <see cref="AvenMindcensorFactory"/>'s
///   Flying / <see cref="OrnithopterFactory"/>'s Flying.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point (mirrors Aven Mindcensor / Ornithopter).
/// </summary>
[CardName("Welkin Tern")]
public static class WelkinTernFactory
{
    public const string CardName = "Welkin Tern";
    public const string PrintedManaCost = "{U}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Welkin Tern — a {U} 2/1 Creature — Bird with the
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
