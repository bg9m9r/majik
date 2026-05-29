using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Raffine's Tower (Streets of New Capenna / reprints).
///
/// W/U/B triome (a "Tower" in the New Capenna tapland cycle — mechanically a
/// Triome). Oracle text:
///   "({T}: Add {W}, {U}, or {B}.)
///    This land enters tapped.
///    Cycling {3} ({3}, Discard this card: Draw a card.)"
///
/// Direct analogue of <see cref="IndathaTriomeFactory"/> — only the produced
/// colours (W/U/B) and basic-land subtypes (Plains / Island / Swamp) differ.
///
/// ## Implemented (v1)
/// - <b>Land</b> carrying the three basic-land subtypes
///   (Plains / Island / Swamp) on the type line so subtype-keyed effects
///   (fetchlands — CR 701.19a, Yavimaya / Urborg) treat it as a real triple
///   basic. Subtypes + the three single-colour mana abilities are data-driven
///   from <c>raffines-tower.json</c>.
/// - <b>{T}: Add {W}/{U}/{B}</b> — three vanilla
///   <see cref="Majik.Core.Abilities.ManaAbility"/>s, one per produced colour
///   (CR 605.1a — mana abilities don't use the stack). The mana line is
///   reminder text on Triomes (it derives from the basic-land subtypes), but
///   the explicit abilities are declared so the shape is observable without an
///   active continuous-effects derivation pass — same posture as
///   <see cref="IndathaTriomeFactory"/>.
/// - <b>Cycling {3}</b> (CR 702.32) — wired through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>(<c>{3}</c>). The primitive appends the
///   <see cref="DiscardSelfCost"/> hand-zone gate (CR 702.32a), attaches the
///   <c>Cycling</c> <see cref="Majik.Core.Abilities.KeywordAbility"/> marker,
///   and — when a bus is supplied — publishes <see cref="CardCycledEvent"/> on
///   resolve (CR 702.32d) so "whenever a player cycles a card" triggers fire.
///
/// ## Production / test parity
/// The production server load path builds the card through the binder chain
/// (<see cref="Majik.Core.CardData.EntersTappedBinder"/> matches "This land
/// enters tapped." and <see cref="Majik.Core.CardData.OracleManaBinder"/>
/// derives the mana abilities); this named factory exists for the dispatcher /
/// test path (<see cref="NamedCardFactory"/>). Unconditional ETB-tapped
/// (CR 614.1c) is therefore omitted on the dispatcher overload — same posture
/// as <see cref="IndathaTriomeFactory"/> and the other JSON-loaded tapland
/// wrappers — and is supplied in production by <c>EntersTappedBinder</c>.
/// </summary>
[CardName("Raffine's Tower")]
public static class RaffinesTowerFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("raffines-tower");

    /// <summary>Construct Raffine's Tower owned and controlled by
    /// <paramref name="owner"/>. Shape-only path — no event bus, so cycling
    /// does not publish <see cref="CardCycledEvent"/>.</summary>
    public static Land Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>Construct Raffine's Tower with optional bus wiring for the
    /// cycling resolve publication (CR 702.32d).</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Optional event bus the cycling resolve publishes
    /// <see cref="CardCycledEvent"/> against.</param>
    public static Land Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Cycling {3}. CR 702.32 — "{3}, Discard this card: Draw a card."
        // The shared primitive appends DiscardSelfCost (CR 702.32a hand-zone
        // gate), the "Cycling" keyword marker, and the CardCycledEvent publish
        // (CR 702.32d) when a bus is supplied.
        CyclingFactory.Build(land, new ManaCostCost("{3}"), eventBus);

        return land;
    }
}
