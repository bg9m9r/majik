using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sudden Edict (Modern Horizons 3, {1}{B}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Split second (As long as this spell is on the stack, players can't cast
///    spells or activate abilities that aren't mana abilities.)
///    Target player sacrifices a creature of their choice."
///
/// Sudden Edict is the split-second-stapled cousin of Diabolic Edict: the same
/// "target player sacrifices a creature of their choice" body
/// (<see cref="DiabolicEdictFactory.BuildSpellDefinition"/>) plus the printed
/// Split second keyword — both primitives already ship, so no new engine
/// mechanic is introduced.
///
/// ## Implemented (v1)
/// - Instant {1}{B} card shape. Card shape comes from the embedded JSON
///   (<c>sudden-edict.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Split second</b> (CR 702.61) modelled as a <see cref="KeywordAbility"/>
///   marker ("Split second"), exactly as <see cref="ExtirpateFactory"/>. The
///   full restriction surface (preventing other spells / non-mana activated
///   abilities while the spell is on the stack) is enforced elsewhere once the
///   priority manager learns to consult the marker; this factory declares the
///   keyword on the card, matching the project-wide convention for keyword
///   markers.
/// - <see cref="BuildSpellDefinition"/> delegates to
///   <see cref="DiabolicEdictFactory.BuildSpellDefinition"/>: one 1..1 "target
///   player" request; on resolution the target player sacrifices a creature of
///   their choice (CR 701.16 — sacrifice bypasses Indestructible /
///   regeneration). Agent-driven pick (<see cref="BotIntent.Removal"/>) with a
///   deterministic fallback to the first creature in battlefield order; no
///   creatures on the target's battlefield → no-op (the spell still resolves).
///
/// ## Rules citations
/// - CR 702.61 — Split second.
/// - CR 701.16 — sacrifice (move to graveyard, bypasses Indestructible / regen).
/// - CR 608.2b — single-target spell with an illegal target fizzles.
///
/// ## Deferred (v1 gaps)
/// - <b>Split second restriction enforcement</b>: the marker is present, but
///   the priority manager does not yet consult it (same queue as
///   <see cref="ExtirpateFactory"/>).
/// - <b>Forced sacrifice prompt UI</b>: the target player's agent receives the
///   full creature list; surfacing the choice to the portal decision panel is
///   deferred (same queue as Diabolic Edict).
/// </summary>
[CardName("Sudden Edict")]
public static class SuddenEdictFactory
{
    public const string CardName = "Sudden Edict";
    public const string Slug = "sudden-edict";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>
    /// Build the Sudden Edict instant from the embedded JSON definition and
    /// stamp the Split second keyword marker (CR 702.61). Card shape only —
    /// the resolve-time target request + sacrifice body is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 702.61 — Split second declared as a keyword marker. The priority
        // manager will consult markers like this once split-second restriction
        // enforcement lands; for now the marker documents the card's printed
        // keyword and matches ExtirpateFactory's posture.
        card.AddAbility(new KeywordAbility("Split second", card, owner));
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Sudden Edict is cast.
    /// Identical to Diabolic Edict: a single 1..1 "target player" request; on
    /// resolution that player sacrifices a creature of their choice
    /// (CR 701.16). No-op when the target controls no creatures.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="agent">Optional agent for the <em>target player</em> (the
    /// one who must sacrifice). When null, the pick falls back deterministically
    /// to the first creature in battlefield order.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        IPlayerAgent? agent) =>
        // Split second is a static restriction on the stack, not part of the
        // resolve body — the sacrifice effect is exactly Diabolic Edict's.
        DiabolicEdictFactory.BuildSpellDefinition(resolver, agent);
}
