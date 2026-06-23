using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tine Shrike (Phyrexia: All Will Be One, {3}{W}).
///
/// Creature — Phyrexian Bird 2/1. Oracle text (verified against Scryfall
/// 2026-06-23):
///   "Flying
///    Infect (This creature deals damage to creatures in the form of -1/-1
///    counters and to players in the form of poison counters.)"
///
/// ## Shape source
/// Tine Shrike is a near-vanilla body: two intrinsic evergreen keywords
/// (Flying CR 702.9, Infect CR 702.90) and no triggered / activated logic. The
/// entire card — name, Creature — Phyrexian Bird subtypes, {3}{W}, 2/1, and
/// BOTH keyword markers — is materialised from the embedded JSON definition
/// (<c>tine-shrike.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON <c>keywords</c> array
/// (<c>["Flying", "Infect"]</c>) is lowered to a
/// <see cref="Majik.Core.Abilities.KeywordAbility"/> marker per keyword by
/// <c>CardDefRuntime.Build</c> (CR 702 — printed keyword lines), so this
/// factory adds nothing in code. Same JSON-driven posture as
/// <see cref="WarScreecherFactory"/> (which also carries Flying via the JSON
/// keywords array).
///
/// ## Implemented (v1)
/// - 2/1 Creature — Phyrexian Bird at {3}{W} (white), owner / controller wired.
/// - <b>Flying (CR 702.9)</b> — KeywordAbility marker; the can-only-be-blocked-
///   by-flying/reach evasion is enforced engine-side by
///   <see cref="Majik.Core.Combat.CombatValidator"/> consulting the marker.
/// - <b>Infect (CR 702.90)</b> — KeywordAbility marker. The combat-damage
///   replacement (-1/-1 counters to creatures, poison counters to players,
///   CR 702.90b) is engine-side; this factory contributes the structurally
///   correct marker so the replacement picks Tine Shrike up without further
///   wiring — same posture as <see cref="BlightedAgentFactory"/> /
///   <see cref="IchorclawMyrFactory"/>.
/// </summary>
[CardName("Tine Shrike")]
public static class TineShrikeFactory
{
    public const string CardName = "Tine Shrike";
    public const string Slug = "tine-shrike";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Tine Shrike from the embedded JSON definition. Flying + Infect
    /// keyword markers are materialised by the definition factory off the JSON
    /// <c>keywords</c> array. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
