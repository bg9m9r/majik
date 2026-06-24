using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wary Thespian (Bloomburrow, {1}{G}).
///
/// Creature — Cat Druid 3/1. Oracle text (verified against Scryfall 2026-06-24):
///   "When this creature enters or dies, surveil 1. (Look at the top card of
///    your library. You may put it into your graveyard.)"
///
/// A thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/wary-thespian.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the same posture
/// as <see cref="SinisterStarfishFactory"/> (surveil) and
/// <see cref="StitchersSupplierFactory"/> ("enters or dies" combined trigger).
/// Both abilities are fully declarative JSON:
///
/// - <b>ETB trigger (CR 603.6a)</b> — an <c>etb_self</c> trigger firing
///   <c>surveil_self 1</c>.
/// - <b>Dies trigger (CR 603.6c / 700.4)</b> — a <c>dies_self</c> trigger firing
///   <c>surveil_self 1</c>. The runtime supplies the Graveyard active zone for
///   <c>dies_self</c> so the trigger remains observable after ZoneService stamps
///   <c>card.Zone = Graveyard</c> before publishing the CardMovedEvent.
///
/// The engine has no OR'd-condition object (see
/// <see cref="StitchersSupplierFactory"/>), so "enters or dies" is modelled as
/// two distinct triggered abilities that share the same surveil effect shape.
///
/// At resolution the shared surveil builder consults the controller's agent
/// (CR 701.42 — look at the top card, may put it into the graveyard), falling
/// back to the all-to-graveyard default when no agent is registered.
/// </summary>
[CardName("Wary Thespian")]
public static class WaryThespianFactory
{
    public const string CardName = "Wary Thespian";
    public const string Slug = "wary-thespian";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Wary Thespian owned and controlled by <paramref name="owner"/>.
    /// Both the enters and dies surveil triggers are materialised from the
    /// embedded JSON definition. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
