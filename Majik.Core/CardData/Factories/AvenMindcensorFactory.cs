using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aven Mindcensor (Future Sight, {2}{W}).
///
/// Creature — Bird Wizard 2/1. Oracle text:
///   "Flash
///    Flying
///    If an opponent would search a library, that player searches the top
///    four cards of that library instead."
///
/// ## Implemented (v1)
/// - 2/1 Creature — Bird Wizard at {2}{W}, owner / controller wired.
/// - <b>Flash</b> (CR 702.8) and <b>Flying</b> (CR 702.9) keyword markers
///   via <see cref="KeywordAbility"/>. Both are functional — Flash routes
///   through <see cref="Majik.Core.Rules.TimingRules.CanCastAtInstantSpeed"/>
///   and Flying through the combat-block validator
///   (<see cref="Majik.Core.Rules.CombatRules"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>"Top four cards instead" library-search replacement</b> (CR 701.19
///   / CR 614 — replacement effect on a search): the engine has no unified
///   "search library" surface yet — tutor and fetch-land search paths are
///   each implemented individually with no shared interception point. The
///   sibling card <see cref="LeoninArbiterFactory"/> hits the same wall;
///   when a SearchLibraryService (or similar) lands, both factories should
///   wire a real interceptor: Aven Mindcensor as a "constrain candidates
///   to top N" replacement, Leonin Arbiter as a "require {2}" optional
///   cost. Until then, the search restriction is a documented no-op.
/// </summary>
[CardName("Aven Mindcensor")]
public static class AvenMindcensorFactory
{
    public const string CardName = "Aven Mindcensor";
    public const string PrintedManaCost = "{2}{W}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Aven Mindcensor. Flash + Flying keyword markers are
    /// attached; the library-search replacement is deferred (see class
    /// xmldoc).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Bird, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));
        // CR 702.9 — Flying. Block restrictions enforced by CombatRules.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
