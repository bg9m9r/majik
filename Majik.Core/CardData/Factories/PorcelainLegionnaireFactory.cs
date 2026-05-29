using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Porcelain Legionnaire (New Phyrexia, {2}{W/P}).
///
/// Artifact Creature — Phyrexian Soldier 3/1. Oracle text (verified
/// against Scryfall):
///   "({W/P} can be paid with either {W} or 2 life.)
///    First strike"
///
/// ## Implemented (v1)
/// - 3/1 Artifact Creature — Phyrexian Soldier, mana cost {2}{W/P}. The
///   base shape (name, both Artifact + Creature card types, Phyrexian +
///   Soldier subtypes, cost, P/T) is materialised from the embedded JSON
///   definition (<c>porcelain-legionnaire.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. The JSON declares
///   <c>["Creature", "Artifact"]</c> so Build returns a
///   <see cref="Creature"/> shell with the Artifact card type stamped on
///   (CR 301.1 / 302.1 — same multi-type seam as Walking Ballista).
/// - <b>Phyrexian-mana pip (CR 107.4f / 118.8)</b>: the {W/P} pip in the
///   printed cost is parsed by <see cref="Majik.Core.ValueObjects.ManaCost"/>
///   into a phyrexian pip (payable with {W} or 2 life via
///   <see cref="Majik.Core.Costs.PhyrexianManaAlternativeCost"/>). The
///   parenthetical in the oracle text is reminder text only — it shapes no
///   additional ability.
/// - <b>First strike (CR 702.7)</b>: a <see cref="KeywordAbility"/> marker.
///   The combat first-strike damage step reads this via
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/> (same
///   wiring as <see cref="YouthfulKnightFactory"/> /
///   <see cref="PhyrexianCrusaderFactory"/>).
///
/// First strike is the only printed ability, so there are no deferred
/// gaps — the card is fully expressed by the existing engine.
/// </summary>
[CardName("Porcelain Legionnaire")]
public static class PorcelainLegionnaireFactory
{
    public const string CardName = "Porcelain Legionnaire";
    public const string Slug = "porcelain-legionnaire";

    /// <summary>
    /// Construct Porcelain Legionnaire — a {2}{W/P} 3/1 Artifact Creature —
    /// Phyrexian Soldier with the First strike keyword marker. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Artifact types, Phyrexian + Soldier subtypes, {2}{W/P}, 3/1).
        // The JSON carries no abilities — First strike is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.7 — First strike marker. The combat first-strike damage
        // step enforces first-strike damage assignment before regular
        // combat damage (read via CombatAbilities.HasFirstStrike).
        card.AddAbility(new KeywordAbility("First strike", card, owner));

        return card;
    }
}
