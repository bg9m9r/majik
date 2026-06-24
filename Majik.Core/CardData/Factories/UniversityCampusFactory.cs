using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for University Campus (Magic: The Gathering Foundations
/// "campus" surveil land — the W/U member).
///
/// Land (no basic land subtypes). Oracle text (verified against Scryfall
/// 2026-06-24):
///   "This land enters tapped.
///    {T}: Add {W} or {U}.
///    {4}, {T}: Surveil 1. (Look at the top card of your library. You may put it
///    into your graveyard.)"
///
/// Same shape as <see cref="TitansGraveFactory"/>: the surveil is an ACTIVATED
/// ability gated behind a mana cost ({4}) plus {T}, NOT an ETB trigger like the
/// Duskmourn / Karlov Manor surveil-on-enter lands — and there are no basic land
/// subtypes on the type line. Only the produced colours ({W}/{U}) and the
/// generic-only activation cost ({4}) differ from Titan's Grave.
///
/// A thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/university-campus.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the whole shape
/// is fully declarative JSON:
///
/// - <b>Dual mana (CR 605.1a)</b> — two single-colour
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances producing {W} and
///   {U} ("{T}: Add {W} or {U}"). Mana abilities don't use the stack.
/// - <b>{4}, {T}: Surveil 1 (CR 601.2f cost + CR 701.42 surveil)</b> — an
///   <c>activated</c> ability whose costs are a <c>mana</c> ({4}) payment plus a
///   <c>tap_self</c> ({T}) cost, resolving a <c>surveil_self</c> effect. At
///   resolution the shared <see cref="CardDefRuntime"/> surveil builder consults
///   the controller's registered agent (CR 701.42 — look at the top card, may
///   put it into the graveyard), falling back to the all-to-graveyard default
///   when no agent is registered.
///
/// ## Note on enters-tapped (CR 614.1c)
/// "This land enters tapped." is applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the oracle text,
/// not by this named factory — same posture as Titan's Grave and the surveil-
/// land cycle. The shape-only factory path therefore enters untapped (no
/// <see cref="Majik.Core.Abilities.ReplacementBus"/> is available here to own
/// the replacement).
/// </summary>
[CardName("University Campus")]
public static class UniversityCampusFactory
{
    public const string CardName = "University Campus";
    public const string Slug = "university-campus";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct University Campus (Land) owned and controlled by
    /// <paramref name="owner"/>: dual {W}/{U} mana plus the {4}, {T}: Surveil 1
    /// activated ability, materialised from the embedded JSON definition.
    /// Enters-tapped (CR 614.1c) is owned by the binder layer on the production
    /// load path, not wired here. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
