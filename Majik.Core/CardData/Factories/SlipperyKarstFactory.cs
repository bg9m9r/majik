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
/// Named-card factory for Slippery Karst (Urza's Saga). Land.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "This land enters tapped.
///    {T}: Add {G}.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// NOTE: Slippery Karst looks like a member of the Onslaught monocolour
/// cycling-land cycle (Tranquil Thicket / Lonely Sandbar / Barren Moor /
/// Forgotten Cave / Secluded Steppe) but it is NOT. Two printed
/// differences keep it off <see cref="OnslaughtCyclingLandFactory"/>:
/// <list type="bullet">
///   <item>Cycling cost is <b>{2} generic</b> (CR 702.32), not
///   {color}. The Onslaught cycle's cost is always one mana of the
///   produced colour.</item>
///   <item>No printed land subtype — Slippery Karst's type_line is just
///   "Land" (same posture as <see cref="BojukaBogFactory"/>), whereas the
///   parametric Onslaught factory requires a land subtype arg.</item>
/// </list>
/// So it routes through a JSON base shape + thin factory instead.
///
/// ## Implemented (v1)
/// - <b>Land</b> with no printed subtype — built from the embedded JSON
///   definition (<c>slippery-karst.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. The JSON supplies the
///   <c>{T}: Add {G}</c> mana ability (CR 605.1 — mana abilities don't use
///   the stack); the enters-tapped rider and Cycling {2} are layered on
///   here because the JSON <c>AbilityDefinition</c> schema expresses
///   neither yet (same posture as <see cref="RestlessSpireFactory"/>).
/// - <b>Enters-tapped replacement (CR 614.1c)</b> — unconditional
///   "This land enters tapped." Registered via
///   <see cref="EntersTappedReplacement"/> on a supplied
///   <see cref="ReplacementBus"/>. Shape-only path (no bus) skips
///   registration and the land enters untapped — same posture every
///   always-tapped factory (Bojuka Bog / Sunscorched Desert) takes.
/// - <b>Cycling {2}</b> (CR 702.32) — wired through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>(<c>"2"</c>). The primitive appends the
///   <see cref="DiscardSelfCost"/> hand-zone gate (CR 702.32a) and, when a
///   bus is supplied, publishes <see cref="CardCycledEvent"/> on resolve
///   (CR 702.32d) so Lightning Rift / Astral Slide triggers fire.
/// </summary>
[CardName("Slippery Karst")]
public static class SlipperyKarstFactory
{
    public const string CardName = "Slippery Karst";
    public const string Slug = "slippery-karst";

    /// <summary>
    /// Construct Slippery Karst with no live wiring. The Cycling ability is
    /// attached for shape inspection (no <see cref="CardCycledEvent"/>
    /// publish without a bus); the enters-tapped replacement is omitted, so
    /// the land enters untapped on this path (matches every other
    /// always-tapped factory's shape-only posture). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null, replacements: null);

    /// <summary>
    /// Construct Slippery Karst with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Optional event bus the cycling resolve
    /// publishes <see cref="CardCycledEvent"/> against (CR 702.32d). When
    /// null no event fires (shape-only path).</param>
    /// <param name="replacements">Optional replacement bus the
    /// enters-tapped restriction (CR 614.1c) is registered against. When
    /// null the land enters untapped (shape-only path).</param>
    public static Land Create(
        Player owner,
        IEventBus? eventBus,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {G} mana ability). The ETB-tapped rider and Cycling {2}
        // are layered on below — neither is expressible in the current JSON
        // AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Enters-tapped restriction (CR 614.1c) — "This land enters tapped."
        // Unconditional; no gate. Shape-only path (no ReplacementBus) skips
        // registration and the land enters untapped.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // Cycling {2}. CR 702.32 — "{2}, Discard this card: Draw a card."
        // Cycle cost is ManaCostCost("2") (generic); the primitive appends
        // the DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d) when a bus is supplied.
        // ----------------------------------------------------------------
        CyclingFactory.Build(land, new ManaCostCost("2"), eventBus);

        return land;
    }
}
