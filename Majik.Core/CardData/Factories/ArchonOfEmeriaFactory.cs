using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Archon of Emeria
/// (Zendikar Rising — Creature — Archon {2}{W} 2/3).
///
/// Oracle text (verified against Scryfall):
///   "Flying
///    Each player can't cast more than one spell each turn.
///    Nonbasic lands your opponents control enter tapped."
///
/// The base shape (name, Archon subtype, {2}{W}, 2/3) is materialised from the
/// embedded JSON definition (<c>archon-of-emeria.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed behaviours
/// (Flying keyword, the one-spell-per-turn cast cap, the opponents'-nonbasic-
/// lands enters-tapped static) are layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers, casting-
/// restriction rails, or replacement-effect statics, so they live in the
/// factory (same posture as <see cref="ThaliaHereticCatharFactory"/>).
///
/// ## Implemented
///
/// ### Flying (CR 702.9)
/// Wired as a <see cref="KeywordAbility"/> marker; the combat system reads it
/// for evasion / block-legality.
///
/// ### "Each player can't cast more than one spell each turn." (CR 601.3)
/// Wired via <see cref="ArchonOfEmeriaOneSpellPerTurnEffect"/>: while Archon is
/// on the battlefield, every player's turn-scoped additional-spell cap (the
/// same <see cref="Majik.Core.Rules.CastingRestrictions"/> rail Irencrag Feat
/// uses) is seeded to 1 and re-seeded at each turn start.
/// <see cref="Majik.Core.Game.SpellCastFlow"/> decrements the counter per cast
/// and <see cref="Majik.Core.Rules.ActionValidator"/> rejects the second cast.
/// Symmetric (CR 109.5 — "Each player", Archon's controller included).
///
/// ### "Nonbasic lands your opponents control enter tapped." (CR 614.1c)
/// Wired via <see cref="ArchonOfEmeriaLandsEnterTappedEffect"/> (a nonbasic-
/// lands-only subset of Thalia, Heretic Cathar's enters-tapped replacement):
/// an <see cref="IReplacementEffect{ZoneMoveIntent}"/> registered on the
/// supplied <see cref="ReplacementBus"/> sets
/// <see cref="ZoneMoveIntent.EntersTapped"/> = true for any nonbasic land
/// (CR 305.6) entering under an opponent's control (CR 109.5). Lifts when
/// Archon leaves the battlefield.
///
/// ## Deferred
/// - <b>Ordering with other ETB-tapped replacements</b>: when multiple
///   replacements apply to the same entry the affected player should choose the
///   order (CR 616.1); the bus applies in registration order. The observable
///   result (the land enters tapped) is unchanged.
/// </summary>
[CardName("Archon of Emeria")]
public static class ArchonOfEmeriaFactory
{
    public const string CardName = "Archon of Emeria";
    public const string Slug = "archon-of-emeria";
    public const string PrintedManaCost = "{2}{W}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Archon of Emeria with no rails wired. Card-shape / dispatcher
    /// posture — Flying is present, but the one-spell cap and the enters-tapped
    /// static are not registered. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, replacementBus: null, eventBus: null, allPlayersResolver: null);

    /// <summary>
    /// Construct a fully-wired Archon of Emeria with both static lifecycles
    /// attached.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacementBus">The <see cref="ReplacementBus"/> the
    /// enters-tapped replacement registers on. May be null — that static simply
    /// won't activate.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking and per-turn cap
    /// reset. May be null — lifecycles still sync once on Attach, but per-turn
    /// reset of the cast cap relies on the bus.</param>
    /// <param name="allPlayersResolver">Returns every player in the game
    /// (Archon's controller included — the cast cap is symmetric, CR 109.5).
    /// May be null — the cast cap simply won't activate.</param>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacementBus,
        IEventBus? eventBus,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Archon subtype,
        // {2}{W}, 2/3). The JSON carries no abilities — the keyword + statics
        // are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying. KeywordAbility marker; CombatAbilities reads it.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 601.3 — "Each player can't cast more than one spell each turn."
        if (allPlayersResolver != null)
        {
            var castCap = new ArchonOfEmeriaOneSpellPerTurnEffect(
                source: card,
                eventBus: eventBus,
                allPlayersResolver: allPlayersResolver);
            castCap.Attach();
        }

        // CR 614.1c — "Nonbasic lands your opponents control enter tapped."
        if (replacementBus != null)
        {
            var entersTapped = new ArchonOfEmeriaLandsEnterTappedEffect(
                source: card,
                replacementBus: replacementBus,
                eventBus: eventBus);
            entersTapped.Attach();
        }

        return card;
    }
}
