using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aether Adept (Magic 2011,
/// Creature — Human Wizard {1}{U}{U} 2/2).
///
/// Oracle text:
///   "When this creature enters, return target creature to its owner's hand."
///
/// Now a thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/aether-adept.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the same
/// posture as <see cref="BoseijuFactory"/> / Karakas. The ETB ability is
/// fully declarative JSON: an <c>etb_self</c> trigger (CR 603.6a) carrying a
/// <c>return_to_hand</c> effect (CR 701.20) over the <c>creature</c> target
/// filter.
///
/// The shared <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline
/// prompts the controller's agent (Rule 602.2b) for any creature on the
/// battlefield (no opponent restriction — "target creature" means ANY
/// creature), and the effect returns the chosen creature to its owner's hand
/// via <see cref="Majik.Core.Primitives.Fx.BounceToHand(Majik.Core.Cards.ICard, Majik.Core.Services.ZoneService?)"/>
/// (CR 608.2b — an illegal target at resolution fizzles cleanly).
///
/// This card previously hand-rolled the ETB triggered ability + a bespoke
/// raw-zone bounce because the declarative JSON schema lacked a "return
/// target … to its owner's hand" effect verb. That verb
/// (<see cref="ReturnToHandEffectDef"/>) now exists, so the factory collapses
/// to the standard JSON-loading shell.
/// </summary>
[CardName("Aether Adept")]
public static class AetherAdeptFactory
{
    public const string CardName = "Aether Adept";
    public const string PrintedManaCost = "{1}{U}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("aether-adept");

    /// <summary>
    /// Construct Aether Adept owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
