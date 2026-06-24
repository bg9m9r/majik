using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Savage Mansion (Murders at Karlov Manor — R/G
/// "mansion" surveil land).
///
/// Land. Oracle text (Scryfall-confirmed):
///   "This land enters tapped.
///    {T}: Add {R} or {G}.
///    {4}, {T}: Surveil 1. (Look at the top card of your library. You may put
///    it into your graveyard.)"
///
/// <para>
/// ## Card identity + abilities come from JSON
/// Name / Land type, the two single-colour
/// <see cref="Majik.Core.Abilities.ManaAbility"/>s ({R} and {G}), and the
/// <c>{4}, {T}: Surveil 1</c> activated ability are all declared in the embedded
/// JSON definition (<c>savage-mansion.json</c>) and materialised via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. Same JSON-identity posture as
/// <see cref="SinisterStarfishFactory"/>.
/// </para>
///
/// <para>
/// ## Activated surveil (CR 701.42)
/// Unlike the ETB surveil-land cycle (<see cref="SurveilLandCycleFactory"/>),
/// Savage Mansion's surveil is a repeatable <c>activated</c> ability with a
/// <c>mana</c> ({4}) + <c>tap_self</c> cost and a <c>surveil_self</c> effect —
/// the same declarative shape as Sinister Starfish's <c>{T}: Surveil 1</c>, only
/// with the extra {4} mana cost. At resolution the shared surveil builder
/// consults the controller's agent (CR 701.42 — look at the top card, may put it
/// into the graveyard), falling back to the all-to-graveyard default when no
/// agent is registered.
/// </para>
///
/// <para>
/// ## Enters tapped (CR 614.1c)
/// "This land enters tapped." is unconditional and is applied on the production
/// load path by <see cref="EntersTappedBinder"/> (it matches the oracle line),
/// NOT by this named factory — identical split to the surveil-land cycle and the
/// scry-land temples (<see cref="TempleOfDeceitFactory"/>). The named factory
/// exists for the test / <see cref="NamedCardFactory"/> dispatch path so unit
/// tests get the mana + activated-surveil abilities without round-tripping
/// through the binder chain.
/// </para>
/// </summary>
[CardName("Savage Mansion")]
public static class SavageMansionFactory
{
    public const string CardName = "Savage Mansion";
    public const string Slug = "savage-mansion";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Savage Mansion owned and controlled by <paramref name="owner"/>.
    /// The {R}/{G} mana abilities and the {4},{T}: Surveil 1 activated ability are
    /// materialised from the embedded JSON definition. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
