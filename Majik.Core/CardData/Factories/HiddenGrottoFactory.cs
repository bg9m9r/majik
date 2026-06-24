using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hidden Grotto (Tarkir: Dragonstorm common land — the
/// surveil-on-ETB sibling of <see cref="CrystalGrottoFactory"/>, which scries
/// on ETB instead).
///
/// Land. Oracle text (verified against Scryfall):
///   "When this land enters, surveil 1. (Look at the top card of your library.
///    You may put it into your graveyard.)
///    {T}: Add {C}.
///    {1}, {T}: Add one mana of any color."
///
/// Scryfall type line is plain <c>Land</c> — no basic-land subtypes. Hidden
/// Grotto does NOT enter tapped. Loaded from the embedded JSON definition via
/// <see cref="CardDefinitionFactory"/>.
///
/// ## Implemented (v1) — all from the JSON definition
/// - <b>ETB surveil 1 (CR 603.6a + CR 701.43 — surveil keyword action)</b> —
///   a self-ETB <see cref="Majik.Core.Abilities.TriggeredAbility"/> whose
///   <c>surveil_self</c> effect peeks the top card and, via the controller's
///   registered agent, decides graveyard-vs-top. With no agent it defaults to
///   all-peeked-to-graveyard (same posture as the surveil-land cycle).
/// - <b>{T}: Add {C} (CR 605.1a)</b> — one cost-free colourless
///   <see cref="Majik.Core.Abilities.ManaAbility"/>.
/// - <b>{1}, {T}: Add one mana of any color (CR 605.1a)</b> — modelled as five
///   per-colour ManaAbility slots, each carrying the {1} additional mana cost,
///   the same WUBRG fan-out the engine uses for "any color" everywhere else
///   (Crystal Grotto, Springleaf Drum, Aether Hub). So six mana abilities
///   total.
/// </summary>
[CardName("Hidden Grotto")]
public static class HiddenGrottoFactory
{
    public const string CardName = "Hidden Grotto";
    public const string Slug = "hidden-grotto";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Build Hidden Grotto (plain Land) from its embedded JSON definition: the
    /// ETB surveil-1 trigger, {T}: Add {C}, and the {1},{T}: any-colour modes.
    /// Hidden Grotto does not enter tapped.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
