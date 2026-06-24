using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Titan's Grave (Outlaws of Thunder Junction "surveil
/// land" cycle — the B/G member).
///
/// Land (no basic land subtypes). Oracle text (verified against Scryfall
/// 2026-06-24):
///   "This land enters tapped.
///    {T}: Add {B} or {G}.
///    {2}{B}{G}, {T}: Surveil 1. (Look at the top card of your library. You may
///    put it into your graveyard.)"
///
/// This is distinct from the Duskmourn / Murders at Karlov Manor surveil lands
/// (e.g. <see cref="UndercitySewersFactory"/>): those surveil ON ENTER via a
/// self-ETB trigger and carry two basic land subtypes; Titan's Grave instead
/// gates its surveil behind an ACTIVATED ability with a {2}{B}{G} mana cost and
/// a {T} cost, and has no basic land subtypes on its type line.
///
/// A thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/titans-grave.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the whole shape
/// is fully declarative JSON:
///
/// - <b>Dual mana (CR 605.1a)</b> — two single-colour
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances producing {B} and
///   {G} ("{T}: Add {B} or {G}"). Mana abilities don't use the stack.
/// - <b>{2}{B}{G}, {T}: Surveil 1 (CR 601.2f cost + CR 701.42 surveil)</b> — an
///   <c>activated</c> ability whose costs are a <c>mana</c> ({2}{B}{G}) payment
///   plus a <c>tap_self</c> ({T}) cost, resolving a <c>surveil_self</c> effect.
///   At resolution the shared <see cref="CardDefRuntime"/> surveil builder
///   consults the controller's registered agent (CR 701.42 — look at the top
///   card, may put it into the graveyard), falling back to the all-to-graveyard
///   default when no agent is registered.
///
/// ## Note on enters-tapped (CR 614.1c)
/// "This land enters tapped." is applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the oracle text,
/// not by this named factory — same posture as the surveil-land cycle. The
/// shape-only factory path therefore enters untapped (no
/// <see cref="Majik.Core.Abilities.ReplacementBus"/> is available here to own
/// the replacement).
/// </summary>
[CardName("Titan's Grave")]
public static class TitansGraveFactory
{
    public const string CardName = "Titan's Grave";
    public const string Slug = "titans-grave";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Titan's Grave (Land) owned and controlled by
    /// <paramref name="owner"/>: dual {B}/{G} mana plus the {2}{B}{G}, {T}:
    /// Surveil 1 activated ability, materialised from the embedded JSON
    /// definition. Enters-tapped (CR 614.1c) is owned by the binder layer on
    /// the production load path, not wired here. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
