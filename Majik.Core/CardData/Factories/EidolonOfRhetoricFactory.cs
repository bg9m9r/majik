using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eidolon of Rhetoric
/// (Journey into Nyx — Enchantment Creature — Spirit {2}{W} 1/4).
///
/// Oracle text (verified against Scryfall):
///   "Each player can't cast more than one spell each turn."
///
/// The base shape (name, Creature + Enchantment types, Spirit subtype, {2}{W},
/// 1/4) is materialised from the embedded JSON definition
/// (<c>eidolon-of-rhetoric.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed static (the one-
/// spell-per-turn cast cap) is layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express a casting-restriction rail,
/// so it lives in the factory (same posture as
/// <see cref="ArchonOfEmeriaFactory"/>, whose printed text is word-for-word
/// identical for this clause).
///
/// ## Implemented
///
/// ### "Each player can't cast more than one spell each turn." (CR 601.3)
/// Wired via the shared <see cref="ArchonOfEmeriaOneSpellPerTurnEffect"/>
/// lifecycle binder: while Eidolon is on the battlefield, every player's turn-
/// scoped additional-spell cap (the same
/// <see cref="Majik.Core.Rules.CastingRestrictions"/> rail Irencrag Feat /
/// Archon of Emeria use) is seeded to 1 and re-seeded at each turn start
/// (CR 514.2). <see cref="Majik.Core.Game.SpellCastFlow"/> decrements the
/// counter per cast and <see cref="Majik.Core.Rules.ActionValidator"/> rejects
/// the second cast. Symmetric (CR 109.5 — "Each player", Eidolon's controller
/// included). The cap lifts when Eidolon leaves the battlefield.
///
/// ## Deferred
/// - None for the printed text. (The "more than one spell" clause is the card's
///   only ability; the body is a vanilla 1/4 enchantment creature otherwise.)
/// </summary>
[CardName("Eidolon of Rhetoric")]
public static class EidolonOfRhetoricFactory
{
    public const string CardName = "Eidolon of Rhetoric";
    public const string Slug = "eidolon-of-rhetoric";
    public const string PrintedManaCost = "{2}{W}";
    public const int Power = 1;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Eidolon of Rhetoric with no rail wired. Card-shape /
    /// dispatcher posture — the one-spell cap is not registered. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, allPlayersResolver: null);

    /// <summary>
    /// Construct a fully-wired Eidolon of Rhetoric with the one-spell-per-turn
    /// cast cap attached.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking and per-turn cap
    /// reset. May be null — the lifecycle still syncs once on Attach, but
    /// per-turn reset of the cast cap relies on the bus.</param>
    /// <param name="allPlayersResolver">Returns every player in the game
    /// (Eidolon's controller included — the cast cap is symmetric, CR 109.5).
    /// May be null — the cast cap simply won't activate.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Enchantment, Spirit subtype, {2}{W}, 1/4). The JSON carries no
        // abilities — the printed static is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 601.3 — "Each player can't cast more than one spell each turn."
        if (allPlayersResolver != null)
        {
            var castCap = new ArchonOfEmeriaOneSpellPerTurnEffect(
                source: card,
                eventBus: eventBus,
                allPlayersResolver: allPlayersResolver);
            castCap.Attach();
        }

        return card;
    }
}
