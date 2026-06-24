using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Suburban Sanctuary (Tarkir: Dragonstorm common land).
///
/// Type line is <c>Land</c> (no basic-land subtypes). Oracle text (verified
/// against Scryfall 2026-06-24):
///   "This land enters tapped.
///    {T}: Add {G} or {W}.
///    {4}, {T}: Surveil 1. (Look at the top card of your library. You may put
///    it into your graveyard.)"
///
/// A thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/suburban-sanctuary.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the same posture
/// as <see cref="UndercitySewersFactory"/> / <see cref="SinisterStarfishFactory"/>.
/// The whole card body is fully declarative JSON:
///
/// - <b>{T}: Add {G} or {W} (CR 605.1a)</b> — two single-colour
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances producing {G} and
///   {W}. Mana abilities don't use the stack.
/// - <b>{4}, {T}: Surveil 1 (CR 701.42 — surveil keyword action)</b> — an
///   <c>activated</c> ability whose costs are a generic <c>{4}</c> mana payment
///   plus a <c>tap_self</c>, and whose <c>surveil_self</c> effect peeks the top
///   card and, via the controller's registered agent, decides graveyard-vs-top.
///   With no agent it defaults to all-peeked-to-graveyard — the same posture as
///   <see cref="SinisterStarfishFactory"/>'s {T}: Surveil 1.
///
/// ## Note on enters-tapped (CR 614.1c)
/// "This land enters tapped." is applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the oracle text, not
/// by this named factory — same posture as the surveil-land cycle. The
/// shape-only factory path therefore enters untapped (no
/// <see cref="Majik.Core.Abilities.ReplacementBus"/> available here to own the
/// replacement).
/// </summary>
[CardName("Suburban Sanctuary")]
public static class SuburbanSanctuaryFactory
{
    public const string CardName = "Suburban Sanctuary";
    public const string Slug = "suburban-sanctuary";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Suburban Sanctuary (Land) owned and controlled by
    /// <paramref name="owner"/>, materialised from the embedded JSON definition:
    /// dual {G}/{W} mana plus the {4}, {T}: Surveil 1 activated ability. This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
