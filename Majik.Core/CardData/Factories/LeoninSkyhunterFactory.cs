using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leonin Skyhunter (various sets, {W}{W}).
///
/// Creature — Cat Knight 2/2. Oracle text (verified against Scryfall):
///   "Flying"
///
/// An efficient two-mana evasive white beater: a 2/2 flier for {W}{W} that
/// pressures the air in aggressive white decks. Leonin Skyhunter is purely a
/// vanilla flier — no triggers, no activated abilities, just the printed
/// Flying keyword (CR 702.9).
///
/// ## Shape source
/// Card identity (name, {W}{W}, 2/2, Creature — Cat Knight) is loaded from
/// <c>Majik.Core/CardData/Cards/leonin-skyhunter.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same JSON-backed posture as
/// <see cref="FaerieSeerFactory"/>. The Flying keyword is attached in code
/// below: the JSON ability schema does not yet express keyword markers.
///
/// ## Implemented (v1)
/// - 2/2 Cat Knight (CR 205.3m) at {W}{W}. Color identity white (derived from
///   the {W} pips per CR 202.2c). Mana value 2 (CR 202.3).
/// - <b>Flying</b> (CR 702.9): <see cref="KeywordAbility"/> marker read by the
///   combat block-restriction path (CombatAbilities.HasFlying) for evasion —
///   same wire-up shape as <see cref="FaerieSeerFactory"/>'s Flying marker.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Leonin Skyhunter")]
public static class LeoninSkyhunterFactory
{
    public const string CardName = "Leonin Skyhunter";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("leonin-skyhunter");

    /// <summary>
    /// Constructs Leonin Skyhunter — a {W}{W} 2/2 Creature — Cat Knight with
    /// the Flying keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules / CombatAbilities. Same shape as Faerie Seer's Flying.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
