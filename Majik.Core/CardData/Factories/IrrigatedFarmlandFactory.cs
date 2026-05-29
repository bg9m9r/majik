using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Irrigated Farmland (Amonkhet) — the W/U member of
/// the "bicycle" (cycling dual) land cycle. Oracle text (verified against
/// Scryfall):
///
/// <code>
/// ({T}: Add {W} or {U}.)
/// This land enters tapped.
/// Cycling {2} ({2}, Discard this card: Draw a card.)
/// </code>
///
/// Type line: <c>Land — Plains Island</c>.
///
/// <para>
/// The Land shell — the Plains and Island land subtypes (CR 205.3i) plus the
/// two mana abilities {W}/{U} (CR 605.1 — mana abilities don't use the stack)
/// — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/irrigated-farmland.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="AlpineMeadowFactory"/> and the Guildgate factories. The printed
/// land subtypes would also feed the L4 mana-derivation pipeline (CR 305.6),
/// but the explicit JSON mana abilities make the shape observable without an
/// active <see cref="Majik.Core.Effects.ContinuousEffectsService"/>.
/// </para>
///
/// <para>
/// Cycling {2} (CR 702.32) is layered on top of the JSON shell via the shared
/// <see cref="CyclingFactory.Build"/> primitive — cost stack
/// <c>[ManaCostCost("2"), DiscardSelfCost]</c> + a draw-a-card resolve body,
/// plus the "Cycling" <see cref="Majik.Core.Abilities.KeywordAbility"/>
/// marker. The CardDefinitionFactory JSON cost/effect vocabulary can express
/// the activated ability shape, but routing through CyclingFactory is what
/// also attaches the keyword marker and publishes
/// <see cref="CardCycledEvent"/> (CR 702.32d) so cycling-trigger cards
/// (Lightning Rift, Astral Slide, Decree of Justice) can subscribe — same
/// posture as <see cref="OnslaughtCyclingLandFactory"/>.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as <see cref="AlpineMeadowFactory"/> and the
/// Guildgate factories).
/// </para>
/// </summary>
[CardName("Irrigated Farmland")]
public static class IrrigatedFarmlandFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("irrigated-farmland");

    /// <summary>Construct Irrigated Farmland owned and controlled by
    /// <paramref name="owner"/>. Shape-only path — cycling does not publish
    /// <see cref="CardCycledEvent"/>.</summary>
    public static Land Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Irrigated Farmland owned and controlled by
    /// <paramref name="owner"/>. When an <paramref name="eventBus"/> is
    /// supplied the cycling resolve publishes <see cref="CardCycledEvent"/>
    /// (CR 702.32d) so cycling-trigger cards fire.
    /// </summary>
    public static Land Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Cycling {2}. CR 702.32 — "{2}, Discard this card: Draw a card."
        // Cycle cost is the generic ManaCostCost("2"); CyclingFactory appends
        // the DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d).
        CyclingFactory.Build(land, new ManaCostCost("2"), eventBus);

        return land;
    }
}
