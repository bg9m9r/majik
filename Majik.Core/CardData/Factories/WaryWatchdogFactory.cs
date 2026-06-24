using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wary Watchdog (Bloomburrow, {1}{G}).
///
/// Creature — Dog 3/1. Oracle text (verified against Scryfall 2026-06-24):
///   "When this creature enters or dies, surveil 1. (Look at the top card of
///   your library. You may put it into your graveyard.)"
///
/// A thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/wary-watchdog.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the same posture
/// as <see cref="SinisterStarfishFactory"/>.
///
/// The "enters or dies" wording (CR 603.6e / CR 700.4) is modelled as TWO
/// declarative triggered abilities that share the same surveil-1 effect:
///
/// - <b>etb_self → surveil_self 1</b> — the
///   <see cref="EnterBattlefieldSelfTriggerDef"/> over the source's own
///   battlefield entry (CR 603.6 — "when this creature enters").
/// - <b>dies_self → surveil_self 1</b> — the <see cref="DiesSelfTriggerDef"/>
///   over the source's own Battlefield → Graveyard move (CR 700.4 — "dies"),
///   whose ActiveZones reach the Graveyard so the ability is still observed
///   after the death is stamped.
///
/// Both resolve via the shared <see cref="CardDefRuntime"/> surveil builder,
/// which consults the controller's agent (CR 701.42 — look at the top card, may
/// put it into the graveyard), falling back to the all-to-graveyard default when
/// no agent is registered.
/// </summary>
[CardName("Wary Watchdog")]
public static class WaryWatchdogFactory
{
    public const string CardName = "Wary Watchdog";
    public const string Slug = "wary-watchdog";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Wary Watchdog owned and controlled by <paramref name="owner"/>.
    /// The two surveil-1 triggered abilities are materialised from the embedded
    /// JSON definition. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
