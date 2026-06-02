using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Savage Lands (Shards of Alara / reprints).
///
/// B/R/G "tapland". Oracle text:
///   "This land enters tapped.
///    {T}: Add {B}, {R}, or {G}."
///
/// ## Implemented (v1)
/// - <b>Land</b> with <b>no</b> basic-land subtypes — the printed type line
///   is simply "Land" (unlike the Triome cycle, e.g.
///   <see cref="ZiatorasProvingGroundFactory"/>, which carries
///   Swamp/Mountain/Forest). Declared data-driven from
///   <c>savage-lands.json</c>.
/// - <b>{T}: Add {B}/{R}/{G}</b> — three vanilla
///   <see cref="Majik.Core.Abilities.ManaAbility"/>s, one per produced
///   colour (CR 605.1a — mana abilities don't use the stack).
/// - No Cycling and no other activated abilities.
///
/// ## Production / test parity
/// The production server load path builds the card through the binder
/// chain: <see cref="Majik.Core.CardData.EntersTappedBinder"/> matches
/// "This land enters tapped." and registers the unconditional ETB-tapped
/// replacement (CR 614.1c). This named factory exists for the dispatcher /
/// test path (<see cref="NamedCardFactory"/>) and builds the shape without
/// the ETB-tapped replacement — same posture as
/// <see cref="ZiatorasProvingGroundFactory"/> and the other JSON-loaded
/// tapland wrappers.
/// </summary>
[CardName("Savage Lands")]
public static class SavageLandsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("savage-lands");

    /// <summary>Construct Savage Lands owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
