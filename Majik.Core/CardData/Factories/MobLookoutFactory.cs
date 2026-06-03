using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mob Lookout (Marvel's Spider-Man,
/// Creature — Human Rogue Villain {1}{U/B} 0/3).
///
/// Oracle text (verified against Scryfall):
///   "When this creature enters, target creature you control connives. (Draw a
///    card, then discard a card. If you discarded a nonland card, put a +1/+1
///    counter on that creature.)"
///
/// Mob Lookout is the canonical fixed-X <c>connive_target</c> card: its ETB
/// connives ANOTHER creature you control (the SELF-form <c>connive_self</c> verb
/// could not express it — that applies to the source). It is a thin wrapper that
/// loads <c>Majik.Core/CardData/Cards/mob-lookout.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. The ETB ability is
/// fully declarative JSON: an <c>etb_self</c> trigger (CR 603.6a) carrying the
/// <c>connive_target</c> effect (CR 701.50) over the <c>creature_you_control</c>
/// target filter — the declarative pay-down of the
/// "connive-and-surveil-style library-manipulation verbs" deferral.
///
/// The shared <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline prompts
/// the controller's agent (CR 602.2b) for a creature they control, and the effect
/// connives the chosen creature once via the shared
/// <see cref="Majik.Core.Keywords.ConniveAction"/> primitive
/// (<see cref="Majik.Core.Primitives.Fx.Connive"/>): its controller draws a card,
/// then discards a card, putting a +1/+1 counter on it for each nonland card
/// discarded (CR 701.50a). CR 608.2b — an illegal target at resolution fizzles
/// cleanly: no connive.
/// </summary>
[CardName("Mob Lookout")]
public static class MobLookoutFactory
{
    public const string CardName = "Mob Lookout";
    public const string PrintedManaCost = "{1}{U/B}";
    public const int Power = 0;
    public const int Toughness = 3;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("mob-lookout");

    /// <summary>
    /// Construct Mob Lookout owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
