using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Forum of Amity (Edge of Eternities — W/B "surveil
/// utility land").
///
/// Land. Oracle text (Scryfall-confirmed):
///   "This land enters tapped.
///    {T}: Add {W} or {B}.
///    {2}{W}{B}, {T}: Surveil 1. (Look at the top card of your library. You
///    may put it into your graveyard.)"
///
/// <para>
/// ## Card identity + abilities come from JSON
/// Name / Land type, the two single-colour <see cref="Majik.Core.Abilities.ManaAbility"/>s
/// ({W} and {B}), and the activated surveil ability are declared in the
/// embedded JSON definition (<c>forum-of-amity.json</c>) and materialised via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The activated ability pairs a
/// <c>{2}{W}{B}</c> mana cost with <c>{T}</c> (CR 602.1 — activated-ability
/// costs) to surveil 1 (CR 701.20 — surveil; the controller may put the peeked
/// card into their graveyard). Same JSON-driven activated-ability shape as
/// <c>Sunhome, Fortress of the Legion</c> (mana + tap cost) and the same
/// <c>surveil_self</c> effect used by <c>Sinister Starfish</c>.
/// </para>
///
/// <para>
/// ## Enters tapped (CR 614.1c)
/// "This land enters tapped." is unconditional and is applied on the
/// production load path by <see cref="EntersTappedBinder"/> (it matches the
/// oracle line), NOT by this named factory — identical split to the surveil-
/// land cycle (<see cref="SurveilLandCycleFactory"/>) and the Temple scry-land
/// cycle (<see cref="TempleOfDeceitFactory"/>). The named factory exists for
/// the test / <see cref="NamedCardFactory"/> dispatch path so unit tests get
/// the mana + activated surveil abilities without round-tripping through the
/// binder chain.
/// </para>
/// </summary>
[CardName("Forum of Amity")]
public static class ForumOfAmityFactory
{
    public const string CardName = "Forum of Amity";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("forum-of-amity");

    /// <summary>
    /// Construct Forum of Amity owned and controlled by <paramref name="owner"/>.
    /// Identity, the {W}/{B} mana abilities, and the {2}{W}{B}, {T}: surveil 1
    /// activated ability all come from the embedded JSON definition.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
