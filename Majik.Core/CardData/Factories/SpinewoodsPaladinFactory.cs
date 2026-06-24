using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spinewoods Paladin (Bloomburrow, {4}{G}).
///
/// Creature — Human Knight 5/4. Oracle text (verified against Scryfall
/// 2026-06-24):
///   "Trample
///    When this creature enters, you gain 3 life.
///    Plot {3}{G} (You may pay {3}{G} and exile this card from your hand.
///    Cast it as a sorcery on a later turn without paying its mana cost.
///    Plot only as a sorcery.)"
///
/// ## Implemented (v1) — pure-JSON body
/// Every printed ability EXCEPT Plot is expressible declaratively, so this
/// factory is a thin <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> wrapper over
/// <c>spinewoods-paladin.json</c>, on the same plan as
/// <see cref="LifecreedDuoFactory"/> / <see cref="DuskLegionZealotFactory"/>:
///
/// - <b>Trample</b> (CR 702.19) from the declarative <c>keywords</c> array,
///   materialised as a <see cref="KeywordAbility"/> consumed by the combat
///   damage-assignment path (same wiring shape as Charging Monstrosaur).
/// - <b>ETB gain 3 life</b> (CR 603.6e — "When this creature enters"):
///   an <c>etb_self</c> trigger → <c>gain_life_self</c> effect (amount 3),
///   routed through the shared <c>Fx.GainLife</c> primitive on the
///   controller. Untargeted (CR 608.2). Same declarative shape as
///   Dusk Legion Zealot's ETB body.
///
/// ## Deferred (v1 gap)
/// - <b>Plot {3}{G} (CR 718)</b>: the printed Plot rider is NOT wired — the
///   same deliberate deferral as <see cref="SlickshotShowOffFactory"/>. Plot
///   is the cast-from-exile-on-a-later-turn alt-cost cluster (pay {3}{G} from
///   hand to exile with a "plotted" marker per CR 718.2; on a later turn,
///   during a main phase with an empty stack, cast from exile for {0} at
///   sorcery speed per CR 718.2 / CR 117.1a, capped once-per-turn per plotted
///   card per CR 718.2c). No activated-from-hand-with-alt-cost +
///   sorcery-speed-later-turn-cast-from-exile primitive exists in the engine
///   yet; ship the printed body (Trample + ETB lifegain), defer Plot until its
///   primitive lands. Until then the bot treats Spinewoods Paladin as a
///   vanilla 5/4 Trample body with the ETB lifegain rider.
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Spinewoods Paladin")]
public static class SpinewoodsPaladinFactory
{
    public const string CardName = "Spinewoods Paladin";
    public const string PrintedManaCost = "{4}{G}";
    public const int Power = 5;
    public const int Toughness = 4;
    public const int LifeGainAmount = 3;

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "spinewoods-paladin";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Spinewoods Paladin with no live <see cref="TriggerManager"/>
    /// wiring. The ETB lifegain trigger is attached to the card shape; without
    /// a registered <see cref="TriggerManager"/> the bus won't pick it up.
    /// Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Spinewoods Paladin, registering the ETB lifegain trigger with
    /// <paramref name="triggers"/> when supplied.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        if (triggers != null)
        {
            foreach (var trigger in card.Abilities.OfType<TriggeredAbility>())
            {
                triggers.RegisterTriggeredAbility(trigger);
            }
        }

        return card;
    }
}
