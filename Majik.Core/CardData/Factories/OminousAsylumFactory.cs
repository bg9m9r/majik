using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ominous Asylum (Duskmourn: House of Horror — B/R
/// surveil land).
///
/// Land. Oracle text (verified against Scryfall 2026-06-24):
///   "This land enters tapped.
///    {T}: Add {B} or {R}.
///    {4}, {T}: Surveil 1. (Look at the top card of your library. You may put
///    it into your graveyard.)"
///
/// <para>
/// Unlike the Murders at Karlov Manor "surveil land" dual cycle
/// (<see cref="SurveilLandCycleFactory"/>, which surveils once on ETB), Ominous
/// Asylum's surveil is a repeatable <b>activated</b> ability gated behind a
/// {4} + {T} cost — the same shape as Sinister Starfish's surveil-on-tap
/// (<see cref="SinisterStarfishFactory"/>), here with an added {4} mana cost.
/// </para>
///
/// <para>
/// ## Card identity + abilities come from JSON
/// Name / Land type, the two single-colour <see cref="Majik.Core.Abilities.ManaAbility"/>s
/// ({B} and {R}, CR 605.1a — mana abilities don't use the stack), and the
/// "{4}, {T}: Surveil 1" activated ability are all declared in the embedded JSON
/// definition (<c>ominous-asylum.json</c>) and materialised via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. At resolution the shared surveil builder
/// consults the controller's agent (CR 701.42 — look at the top card, you may
/// put it into the graveyard), falling back to the all-to-graveyard default when
/// no agent is registered. Same JSON-identity posture as
/// <see cref="SinisterStarfishFactory"/> and <see cref="TempleOfDeceitFactory"/>.
/// </para>
///
/// <para>
/// ## Enters tapped (CR 614.1c)
/// "This land enters tapped." is unconditional and is applied on the production
/// load path by <see cref="EntersTappedBinder"/> (it matches the oracle line),
/// NOT by this named factory — identical split to the surveil-land cycle
/// (<see cref="SurveilLandCycleFactory"/>) and the Temple scry-land cycle. The
/// named factory exists for the test / <see cref="NamedCardFactory"/> dispatch
/// path so unit tests get the mana + surveil abilities without round-tripping
/// through the binder chain.
/// </para>
/// </summary>
[CardName("Ominous Asylum")]
public static class OminousAsylumFactory
{
    public const string CardName = "Ominous Asylum";
    public const string Slug = "ominous-asylum";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Ominous Asylum owned and controlled by <paramref name="owner"/>.
    /// Identity, the {B}/{R} mana abilities, and the "{4}, {T}: Surveil 1"
    /// activated ability all come from the embedded JSON definition. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
