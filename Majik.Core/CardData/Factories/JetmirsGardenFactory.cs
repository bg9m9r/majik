using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Jetmir's Garden — the Streets of New Capenna
/// "Triome" tri-land (Mountain Forest Plains).
///
/// Oracle text (verified against the embedded seed):
/// <code>
/// ({T}: Add {R}, {G}, or {W}.)
/// This land enters tapped.
/// Cycling {3} ({3}, Discard this card: Draw a card.)
/// </code>
///
/// Shape mirrors <see cref="OnslaughtCyclingLandFactory"/> — a tapped land
/// that taps for mana and has Cycling — generalised from a monocolour cycle
/// member to a three-colour Triome:
/// <list type="bullet">
/// <item><b>Land</b> with the three printed land subtypes (Mountain /
///   Forest / Plains). NOT Basic — these are nonbasic land-types-as-subtype
///   lands (CR 305.6). The printed mana abilities are declared inline here
///   (matching <see cref="OnslaughtCyclingLandFactory"/>'s posture) so
///   dispatcher / shape tests see <c>{T}: Add {R}/{G}/{W}</c> without an
///   active <see cref="Majik.Core.Effects.ContinuousEffectsService"/>.</item>
/// <item><b>Enters-tapped (CR 614.1c)</b> — "This land enters tapped." This
///   factory does NOT register the replacement itself; on the production
///   load path <see cref="Majik.Core.CardData.EntersTappedBinder"/> detects
///   the oracle sentence and registers an
///   <see cref="Majik.Core.Effects.EntersTappedReplacement"/>. Same posture
///   as <see cref="HedgeMazeFactory"/> (which also relies on the binder)
///   and <see cref="OnslaughtCyclingLandFactory"/>'s shape-only path.</item>
/// <item><b>{T}: Add {R}, {G}, or {W}</b> — three vanilla
///   <see cref="ManaAbility"/> instances, one per colour (CR 605.1 — mana
///   abilities don't use the stack).</item>
/// <item><b>Cycling {3}</b> (CR 702.32) — wired through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>(<c>"3"</c>), i.e. 3 generic mana. The
///   primitive appends the <see cref="DiscardSelfCost"/> hand-zone gate
///   (CR 702.32a) and, when a bus is supplied, publishes
///   <see cref="CardCycledEvent"/> on resolve (CR 702.32d).</item>
/// </list>
/// </summary>
[CardName("Jetmir's Garden")]
public static class JetmirsGardenFactory
{
    private const string CardName = "Jetmir's Garden";

    /// <summary>
    /// Construct Jetmir's Garden owned and controlled by
    /// <paramref name="owner"/>. Single-arg path — no bus wiring (shape
    /// observability only; cycling does not publish
    /// <see cref="CardCycledEvent"/>). Enters-tapped is applied by the
    /// production binder, not here.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null, replacements: null);

    /// <summary>
    /// Construct Jetmir's Garden with optional bus wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Optional event bus the cycling resolve
    /// publishes <see cref="CardCycledEvent"/> against (CR 702.32d).</param>
    /// <param name="replacements">Reserved for parity with the analogue
    /// land factories. Enters-tapped (CR 614.1c) is registered by
    /// <see cref="Majik.Core.CardData.EntersTappedBinder"/> on the
    /// production load path off the oracle text, so this factory does not
    /// register it even when a bus is supplied — passing it keeps the
    /// signature aligned with sibling cycling-land factories.</param>
    public static Land Create(Player owner, IEventBus? eventBus, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ = replacements; // enters-tapped owned by EntersTappedBinder (CR 614.1c)

        var land = new Land(
            CardName,
            supertypes: null,
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Forest, CardSubtype.Plains });
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {R}, {G}, or {W}. CR 605.1 — mana abilities (no stack).
        // One ManaAbility per producible colour; the printed land subtypes
        // would also feed the L4 mana-derivation pipeline, but the
        // shape-only test surface reads these explicit abilities.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("R")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("G")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));

        // ----------------------------------------------------------------
        // Cycling {3}. CR 702.32 — "{3}, Discard this card: Draw a card."
        // Cycle cost is 3 generic mana; the primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d) when a bus is supplied.
        // ----------------------------------------------------------------
        CyclingFactory.Build(land, new ManaCostCost("3"), eventBus);

        return land;
    }
}
