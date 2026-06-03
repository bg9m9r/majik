using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ethersworn Canonist
/// (Alara Reborn — Artifact Creature — Human Cleric {1}{W} 2/2).
///
/// Oracle text (verified against Scryfall):
///   "Each player who has cast a nonartifact spell this turn can't cast
///    additional nonartifact spells."
///
/// The base shape (name, Creature + Artifact types, Human + Cleric subtypes,
/// {1}{W}, 2/2) is materialised from the embedded JSON definition
/// (<c>ethersworn-canonist.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed static (the
/// nonartifact-spell-per-turn restriction) is layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express a casting-restriction rail,
/// so it lives in the factory (same posture as
/// <see cref="ArchonOfEmeriaFactory"/> / <see cref="SanctumPrelateFactory"/>).
///
/// ## Implemented
///
/// ### "Each player who has cast a nonartifact spell this turn can't cast
/// ### additional nonartifact spells." (CR 605/616 / 601.3)
/// Wired via <see cref="EtherswornCanonistNonartifactRestrictionEffect"/>:
/// while the Canonist is on the battlefield, a battlefield-gated symmetric
/// active flag is registered for every player in
/// <see cref="Majik.Core.Rules.CastingRestrictions"/>. The companion per-player
/// counter of nonartifact spells cast this turn is incremented unconditionally
/// by <see cref="Majik.Core.Game.SpellCastFlow"/> on every nonartifact cast.
/// <see cref="Majik.Core.Rules.ActionValidator"/> rejects a NONARTIFACT
/// <c>CastSpellAction</c> only when the active flag is set for the caster AND
/// the caster has already cast a nonartifact spell this turn. ARTIFACT spells
/// are never restricted (CR 605/616 — the caster's own artifact spells stay
/// castable and don't increment the nonartifact counter). Symmetric (CR 109.5 —
/// "Each player", the Canonist's controller included). The counter is re-seeded
/// each turn (CR 514.2); the restriction lifts when the Canonist leaves the
/// battlefield.
///
/// ## Deferred
/// - None for the printed text. (The restriction clause is the card's only
///   ability; the body is a vanilla 2/2 artifact creature otherwise.)
/// </summary>
[CardName("Ethersworn Canonist")]
public static class EtherswornCanonistFactory
{
    public const string CardName = "Ethersworn Canonist";
    public const string Slug = "ethersworn-canonist";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Ethersworn Canonist with no rail wired. Card-shape /
    /// dispatcher posture — the nonartifact restriction is not registered. This
    /// is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, allPlayersResolver: null);

    /// <summary>
    /// Construct a fully-wired Ethersworn Canonist with the nonartifact-spell
    /// restriction attached.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking and per-turn
    /// counter reset. May be null — the lifecycle still syncs once on Attach,
    /// but per-turn reset of the nonartifact counter relies on the bus.</param>
    /// <param name="allPlayersResolver">Returns every player in the game (the
    /// Canonist's controller included — the restriction is symmetric,
    /// CR 109.5). May be null — the restriction simply won't activate.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Artifact, Human + Cleric subtypes, {1}{W}, 2/2). The JSON carries no
        // abilities — the printed static is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 605/616 / 601.3 — "Each player who has cast a nonartifact spell
        // this turn can't cast additional nonartifact spells."
        if (allPlayersResolver != null)
        {
            var restriction = new EtherswornCanonistNonartifactRestrictionEffect(
                source: card,
                eventBus: eventBus,
                allPlayersResolver: allPlayersResolver);
            restriction.Attach();
        }

        return card;
    }
}
