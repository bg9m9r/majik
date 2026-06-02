using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eiganjo Castle (Champions of Kamigawa).
///
/// Legendary Land. Oracle text (Scryfall-confirmed):
///   "{T}: Add {W}.
///    {W}, {T}: Prevent the next 2 damage that would be dealt to target
///     legendary creature this turn."
///
/// ## Shape source
/// The entire card — Legendary supertype + Land type, the <c>{T}: Add {W}</c>
/// mana ability (CR 605.1a), and the <c>{W}, {T}: prevent 2 damage to target
/// legendary creature</c> activated ability (CR 602) — is fully expressed in
/// <c>Majik.Core/CardData/Cards/eiganjo-castle.json</c> and built data-driven
/// through <see cref="CardDefinitionFactory"/>. The prevent ability is a
/// <c>prevent_damage_target</c> effect (<see cref="PreventDamageTargetEffectDef"/>)
/// with <c>targetFilter: "legendary_creature"</c> and <c>amount: 2</c>: it
/// declares a single target request and, at resolution, registers a CR 615
/// prevention shield (<see cref="Majik.Core.Effects.PreventNextNDamageToCreatureShield"/>)
/// bound to the chosen legendary creature on the controller-attached
/// <see cref="Majik.Core.Effects.ReplacementBus"/>. The shield soaks up to 2
/// points of damage aimed at that creature this turn and auto-expires at
/// cleanup (CR 514.2). CR 608.2b — an illegal/absent target at resolution
/// fizzles with no shield registered.
///
/// ## Production / test parity
/// This is a thin wrapper around the JSON definition — the same posture as the
/// other JSON-loaded land factories (e.g. <see cref="SavageLandsFactory"/>).
/// The dispatcher / test path (<see cref="NamedCardFactory.Create"/>) routes
/// here with no <see cref="Majik.Core.Effects.ReplacementBus"/> (shape-only):
/// the abilities, costs, and target requests are all present; only the live
/// shield registration at resolution requires a bus, which the live-prevention
/// tests supply by calling <see cref="CardDefinitionFactory.Build(CardDefinition, Player, Majik.Core.Effects.ReplacementBus?)"/>
/// directly. Adding this factory flips <c>IsImplemented</c> automatically via
/// the <see cref="ImplementedCardNames"/> registry.
/// </summary>
[CardName("Eiganjo Castle")]
public static class EiganjoCastleFactory
{
    public const string CardName = "Eiganjo Castle";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("eiganjo-castle");

    /// <summary>Construct Eiganjo Castle owned and controlled by
    /// <paramref name="owner"/> (shape-only — no
    /// <see cref="Majik.Core.Effects.ReplacementBus"/> wired).</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
