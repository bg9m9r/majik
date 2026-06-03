using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Evolution Sage (War of the Spark, {2}{G}).
///
/// Creature — Elf Druid 3/2. Oracle text:
///   "Landfall — Whenever a land you control enters, proliferate. (Choose any
///    number of permanents and/or players, then give each another counter of
///    each kind already there.)"
///
/// ## Shape source
/// Card identity (name, {2}{G}, 3/2, Elf Druid) is loaded from
/// <c>Majik.Core/CardData/Cards/evolution-sage.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The landfall trigger is attached in
/// code below.
///
/// ## Implemented (v1)
/// - 3/2 Creature — Elf Druid, mana cost {2}{G}, owner / controller wired.
/// - <b>Landfall triggered ability</b> (CR 603.1 / 603.6a / CR 702.142) —
///   fires on a <see cref="Majik.Core.Events.CardMovedEvent"/> filtered to
///   "a land entering the battlefield under the controller's control" via the
///   shared <see cref="Triggers.OnLandEntersUnderControl"/> predicate (same
///   predicate as <see cref="SteppeLynxFactory"/> / <see cref="HedronCrabFactory"/>).
///   No <see cref="TargetRequest"/>: proliferate self-selects its
///   permanent/player set (CR 701.27), so there is nothing to target — mirrors
///   <see cref="KarnsBastionFactory"/>'s proliferate ability.
/// - <b>Resolve — proliferate</b> (CR 701.27): delegates to the shared
///   proliferate primitive <see cref="SwordOfTruthAndJusticeFactory.Proliferate"/>,
///   the same primitive Karn's Bastion / Sword of Truth and Justice /
///   Tezzeret's Gambit use — it walks every known player's battlefield and adds
///   one more counter of an existing kind to each permanent that already has at
///   least one counter.
///
/// ## v1 simplifications (inherited from the shared proliferate primitive)
/// - <b>"Any number" → "all of them"</b>: agent-driven subset selection is
///   deferred; v1 deterministically proliferates every eligible permanent.
/// - <b>Player counters</b> (poison, energy, experience) are not yet first-class
///   in the shared primitive — same gap as Karn's Bastion.
///
/// ## Deferred (v1 gaps)
/// - <b>Trigger registration</b>: the shape-only <see cref="Create(Player)"/>
///   path attaches the trigger to the card for inspection but does not register
///   it with a bus. Use the <see cref="Create(Player, TriggerManager)"/>
///   overload for live firing.
/// </summary>
[CardName("Evolution Sage")]
public static class EvolutionSageFactory
{
    public const string CardName = "Evolution Sage";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("evolution-sage");

    /// <summary>
    /// Construct Evolution Sage with no live <see cref="TriggerManager"/>
    /// wiring. The landfall trigger is attached for shape inspection but not
    /// registered with a bus. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Evolution Sage. When <paramref name="triggers"/> is supplied
    /// the landfall trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering under
    /// the controller's control automatically queues the ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Landfall — CR 603.1 / 603.6a / CR 702.142.
        //   "Whenever a land you control enters, proliferate."
        // Predicate is shared with Steppe Lynx / Hedron Crab / Lotus Cobra.
        // No target: proliferate self-selects its permanent/player set
        // (CR 701.27). On resolve, delegate to the shared proliferate
        // primitive (the same one Karn's Bastion / Sword of Truth and
        // Justice / Tezzeret's Gambit use).
        // ----------------------------------------------------------------
        var proliferateEffect = new Effect(
            $"{CardName}: landfall — proliferate (CR 701.27)",
            () => SwordOfTruthAndJusticeFactory.Proliferate(card.Controller ?? owner));

        var landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { proliferateEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);

        return card;
    }
}
