using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Man-o'-War (Visions,
/// Creature — Jellyfish {2}{U} 2/2).
///
/// Oracle text:
///   "When this creature enters, return target creature to its owner's hand."
///
/// Man-o'-War is the simpler, unconditional sibling of
/// <see cref="AetherAdeptFactory"/> / <see cref="ReflectorMageFactory"/>:
/// any creature, no opponent restriction, no replay restriction. It is a thin
/// wrapper that loads <c>Majik.Core/CardData/Cards/man-o-war.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. The ETB ability
/// is fully declarative JSON: an <c>etb_self</c> trigger (CR 603.6a) carrying a
/// <c>return_to_hand</c> effect (CR 701.20) over the <c>creature</c> target
/// filter.
///
/// The shared <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline
/// prompts the controller's agent (CR 602.2b) for any creature on the
/// battlefield ("target creature" means ANY creature), and the effect returns
/// the chosen creature to its owner's hand via
/// <see cref="Majik.Core.Primitives.Fx.BounceToHand(Majik.Core.Cards.ICard, Majik.Core.Services.ZoneService?)"/>
/// (CR 608.2b — an illegal target at resolution fizzles cleanly).
/// </summary>
[CardName("Man-o'-War")]
public static class ManOWarFactory
{
    public const string CardName = "Man-o'-War";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("man-o-war");

    /// <summary>
    /// Construct Man-o'-War owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
