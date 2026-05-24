using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grist, the Hunger Tide (Streets of New Capenna, {1}{B}{G}).
///
/// Legendary Planeswalker — Grist, loyalty 3.
/// Oracle text:
///   "As long as Grist, the Hunger Tide isn't on the battlefield, it's a
///    1/1 Insect creature in addition to its other types."
///   [+1], [-2], [-5] loyalty abilities wired by OracleLoyaltyAbilityBinder
///   during the deck-load pipeline — this factory sets up card structure only.
///
/// ## V1 simplification
/// The "not on the battlefield" conditional is deferred. Grist is constructed
/// as a Planeswalker with <see cref="CardType.Creature"/> unconditionally
/// added, plus the Insect subtype. This makes Grist tutorable by Green Sun's
/// Zenith and similar creature-search effects (HasType(Creature) == true)
/// without wiring the full conditional layer-4 infrastructure.
///
/// Migrated to the fluent <see cref="CardDef"/> DSL — the Planeswalker shape
/// + Creature secondary type + Legendary supertype + Insect subtype all
/// flow through one chain.
/// </summary>
[CardName("Grist, the Hunger Tide")]
public static class GristFactory
{
    public static CardDef Define() => CardDef
        .Planeswalker("Grist, the Hunger Tide", "{1}{B}{G}", loyalty: 3)
        .WithSupertype(CardSupertype.Legendary)
        .WithSubtypes(CardSubtype.Grist, CardSubtype.Insect)
        // V1: add Creature type unconditionally so tutors like Green Sun's
        // Zenith can target Grist in all zones (CR 115.4 / 106.5a). The
        // oracle-text restriction is a deferred conditional layer-4 effect.
        .WithType(CardType.Creature);

    public static Planeswalker Create(Player owner) =>
        (Planeswalker)CardDefRuntime.Build(Define(), owner);
}
