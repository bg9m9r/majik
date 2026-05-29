using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Jungle Hollow (Khans of Tarkir).
///
/// B/G "life gain land" (the Tarkir "refuge"/gainland cycle).
/// Oracle text:
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {B} or {G}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/jungle-hollow.json</c> and materializes it
/// through <see cref="CardDefinitionFactory"/>. Same oracle shape as the
/// rest of the refuge cycle (<see cref="BloodfellCavesFactory"/>) — ETB-tapped
/// + an ETB self-trigger + two single-colour mana abilities — except this
/// member produces {B}/{G}.
///
/// - Two mana abilities {B} / {G} (CR 605.1a — mana abilities, never use
///   the stack).
/// - ETB triggered ability (CR 603.6a) firing on <c>etb_self</c>, resolving
///   to a <c>gain_life_self</c> effect that calls
///   <see cref="Player.GainLife"/> for the land's controller (CR 119.3).
/// - Unconditional enters-tapped (CR 614.1c) is applied on the production
///   load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not
///   by this named-card factory — same posture as the cycle.
/// </summary>
[CardName("Jungle Hollow")]
public static class JungleHollowFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("jungle-hollow");

    /// <summary>Construct Jungle Hollow owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
