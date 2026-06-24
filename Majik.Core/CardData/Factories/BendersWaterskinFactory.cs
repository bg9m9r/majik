using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bender's Waterskin (Avatar: The Last Airbender, {3}).
///
/// Artifact. Oracle text (verified against Scryfall 2026):
///   "Untap this artifact during each other player's untap step.
///    {T}: Add one mana of any color."
///
/// ## Implemented
/// - Artifact body / identity / owner / controller built from
///   <c>Majik.Core/CardData/Cards/benders-waterskin.json</c> via
///   <see cref="CardDefinitionFactory"/>.
/// - <b>{T}: Add one mana of any color (CR 605.1, CR 106.6)</b> — modeled as
///   five <see cref="Majik.Core.Abilities.ManaAbility"/> instances (one per
///   WUBRG), carried in the JSON definition as five <c>{ "kind": "mana" }</c>
///   entries. The mana picker satisfies any single colour pip by selecting the
///   matching ability slot — the same any-colour shape
///   <see cref="ArcaneSignetFactory"/> uses. The implicit {T} self-tap is baked
///   into each mana ability's default cost path.
/// - <b>"Untap this artifact during each other player's untap step." (CR 502.1
///   + the printed static)</b>: lifecycle binder
///   <see cref="UntapsDuringOtherUntapStepsStaticEffect"/> registers an
///   extra-untap rider while the Waterskin is on the battlefield;
///   <see cref="Majik.Core.Game.TurnDriver"/>'s untap step then untaps it during
///   each non-controller's untap step too. Wired only when an event bus is
///   supplied (the shape-only constructor stays side-effect-free), exactly the
///   posture <see cref="EndbringerFactory"/> uses for the same printed static.
/// </summary>
[CardName("Bender's Waterskin")]
public static class BendersWaterskinFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("benders-waterskin");

    /// <summary>
    /// Construct Bender's Waterskin with no event-bus wiring (shape-only /
    /// unit-test path). The five any-colour mana abilities are attached; the
    /// "untap during each other player's untap step" static does NOT register
    /// (it needs an event bus to track the battlefield lifecycle). Use the
    /// <see cref="Create(Player, IEventBus)"/> overload for the full surface.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Bender's Waterskin owned and controlled by
    /// <paramref name="owner"/>. When <paramref name="eventBus"/> is supplied
    /// the <see cref="UntapsDuringOtherUntapStepsStaticEffect"/> lifecycle
    /// binder attaches so the printed "untap this artifact during each other
    /// player's untap step" static registers on ETB and lifts on LTB.
    /// </summary>
    public static Artifact Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        // "Untap this artifact during each other player's untap step."
        // CR 502.1 + the printed static. Only attaches when an event bus is
        // supplied so the shape-only constructor stays zero-side-effect for
        // structural tests that don't drive zone moves (same posture as
        // Endbringer / Mana Vault's untap static). On ETB the extra-untap
        // rider registers; on LTB it lifts.
        if (eventBus != null)
        {
            new UntapsDuringOtherUntapStepsStaticEffect(card, eventBus).Attach();
        }

        return card;
    }
}
