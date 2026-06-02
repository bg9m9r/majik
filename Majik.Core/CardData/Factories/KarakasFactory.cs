using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Karakas (Legends / reprints).
///
/// Legendary Land.
/// Oracle text:
///   "{T}: Add {W}.
///    {T}: Return target legendary creature to its owner's hand."
///
/// Now a thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/karakas.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the same
/// posture as <see cref="BoseijuFactory"/> / Minamo. Both abilities are
/// fully declarative JSON:
///
/// - <b>{T}: Add {W}</b> — vanilla mana ability (CR 605.1, no stack).
/// - <b>{T}: Return target legendary creature to its owner's hand</b> — a
///   <c>return_to_hand</c> effect (CR 701.20) over the
///   <c>legendary_creature</c> target filter. The shared
///   <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline prompts the
///   activating player's agent (Rule 602.2b) for a legendary-creature pick,
///   and the effect returns the chosen creature to its owner's hand via
///   <see cref="Majik.Core.Primitives.Fx.BounceToHand(Majik.Core.Cards.ICard, Majik.Core.Services.ZoneService?)"/>
///   (CR 608.2b — an illegal target at resolution fizzles cleanly).
///
/// This card previously hand-rolled the bounce ability + a bespoke
/// <c>BounceToOwnersHand</c> zone walk because the declarative JSON schema
/// lacked a "return target … to its owner's hand" effect verb. That verb
/// (<see cref="ReturnToHandEffectDef"/>) now exists, so the factory collapses
/// to the standard JSON-loading shell.
/// </summary>
[CardName("Karakas")]
public static class KarakasFactory
{
    public const string CardName = "Karakas";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("karakas");

    /// <summary>
    /// Construct Karakas owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
