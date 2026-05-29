using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Desert of the Fervent (Hour of Devastation —
/// the monocolour "cycling desert" cycle, red member).
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "This land enters tapped.
///    {T}: Add {R}.
///    Cycling {1}{R} ({1}{R}, Discard this card: Draw a card.)"
///
/// Type line: <c>Land — Desert</c>.
///
/// Shares the cycling-land shape of <see cref="OnslaughtCyclingLandFactory"/>
/// (enters-tapped + {T}: Add {color} + Cycling), differing in two ways:
/// the printed land subtype is <c>Desert</c> (not a basic-land type) and
/// the cycling cost is <c>{1}{R}</c> rather than a single coloured pip.
///
/// The base shape (nonbasic Land + Desert subtype + {T}: Add {R} mana
/// ability, CR 605.1) is materialised from the embedded JSON definition
/// (<c>desert-of-the-fervent.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The enters-tapped rider and
/// the Cycling ability are layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses neither (same hybrid posture
/// as <see cref="RestlessSpireFactory"/>).
///
/// ## Implemented
/// - <b>Land — Desert</b> + <b>{T}: Add {R}</b> (from JSON; CR 605.1 — a
///   mana ability, no stack).
/// - <b>Enters-tapped (CR 614.1c)</b> — unconditional "This land enters
///   tapped." On the production load path the
///   <see cref="Majik.Core.CardData.EntersTappedBinder"/> registers the
///   <see cref="EntersTappedReplacement"/> from the oracle text; this
///   factory also registers it when a <see cref="ReplacementBus"/> is
///   supplied (and omits it on the shape-only path — matching the Refuge /
///   Onslaught cycle posture).
/// - <b>Cycling {1}{R}</b> (CR 702.32) — wired through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>(<c>{1}{R}</c>). When the bus is supplied,
///   cycling resolve publishes <see cref="CardCycledEvent"/> (CR 702.32d
///   "Whenever a player cycles a card") so cycling-payoff triggers fire.
/// </summary>
[CardName("Desert of the Fervent")]
public static class DesertOfTheFerventFactory
{
    public const string CardName = "Desert of the Fervent";
    public const string Slug = "desert-of-the-fervent";

    /// <summary>
    /// Construct Desert of the Fervent owned and controlled by
    /// <paramref name="owner"/>. Shape-only path — no bus wiring
    /// (enters-tapped is omitted and cycling does not publish
    /// <see cref="CardCycledEvent"/>). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null, replacements: null);

    /// <summary>
    /// Construct Desert of the Fervent with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Optional event bus the cycling resolve
    /// publishes <see cref="CardCycledEvent"/> against (CR 702.32d).</param>
    /// <param name="replacements">Optional replacement bus the
    /// enters-tapped restriction (CR 614.1c) is registered against.</param>
    public static Land Create(
        Player owner,
        IEventBus? eventBus,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition: nonbasic Land with
        // the Desert subtype + the {T}: Add {R} mana ability (CR 605.1).
        // The enters-tapped rider and Cycling ability are layered on below —
        // neither is expressible in the current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; the land
        // then enters untapped. Same posture as OnslaughtCyclingLandFactory.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // Cycling {1}{R}. CR 702.32 — "{1}{R}, Discard this card: Draw a
        // card." Cycle cost is ManaCostCost("{1}{R}"); the primitive appends
        // the DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d).
        // ----------------------------------------------------------------
        CyclingFactory.Build(land, new ManaCostCost("{1}{R}"), eventBus);

        return land;
    }
}
