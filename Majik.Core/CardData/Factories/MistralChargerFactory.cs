using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mistral Charger (Magic 2014 / various, {1}{W}).
///
/// Creature — Pegasus 2/1. Oracle text (verified against Scryfall):
///   "Flying"
///
/// A 2/1 evasive white flier for two mana — Mistral Charger is the
/// archetypal aggressive white beater, slotting into white-weenie and tempo
/// shells where its Flying body pressures life totals through the air. It is
/// purely a vanilla flier: no triggers, no activated abilities, just the
/// printed Flying keyword (CR 702.9) — the same posture as
/// <see cref="WindDrakeFactory"/>.
///
/// ## Shape source
/// Card identity (name, {1}{W}, 2/1, Creature — Pegasus) is loaded from
/// <c>Majik.Core/CardData/Cards/mistral-charger.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="FaerieSeerFactory"/> / <see cref="DanithaCapashenParagonFactory"/>.
/// The Flying keyword marker is attached in code below: the JSON ability
/// schema does not yet express evergreen keyword markers.
///
/// ## Implemented (v1)
/// - 2/1 Pegasus (CR 205.3m) at {1}{W}. Colour identity white (derived from
///   the {W} pip per CR 202.2c). Mana value 2 (CR 202.3).
/// - <b>Flying</b> (CR 702.9): <see cref="KeywordAbility"/> marker read by
///   <c>CombatAbilities.HasFlying</c> for evasion in the combat validator —
///   same wire-up shape as <see cref="WindDrakeFactory"/>'s Flying.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Mistral Charger")]
public static class MistralChargerFactory
{
    public const string CardName = "Mistral Charger";
    public const string Slug = "mistral-charger";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Constructs Mistral Charger — a {1}{W} 2/1 Creature — Pegasus with the
    /// Flying keyword marker. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Pegasus, {1}{W}, 2/1). No abilities in the JSON — the Flying
        // keyword marker is layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules / CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
