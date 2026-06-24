using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hulking Raptor (The Lost Caverns of Ixalan,
/// {2}{G}{G}).
///
/// Creature — Dinosaur 5/3. Oracle text (verified against Scryfall):
///   "Ward {2}
///    At the beginning of your first main phase, add {G}{G}."
///
/// The base shape (name, Creature, Dinosaur subtype, {2}{G}{G}, 5/3) is
/// materialised from the embedded JSON definition (<c>hulking-raptor.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Ward {2} and the
/// first-main-phase mana trigger are layered on here (same posture as
/// <see cref="KlothysGodOfDestinyFactory"/> for the first-main-phase trigger
/// and <see cref="KappaCannoneerFactory"/> for Ward).
///
/// ## Implemented
///
/// - <b>Ward {2} (CR 702.21)</b>: a <see cref="KeywordAbility"/> marker PLUS
///   the real battlefield-attached Ward triggered ability wired off the
///   shared <see cref="WardTriggerWiring.Attach"/> helper: "Whenever this
///   creature becomes the target of a spell or ability an opponent controls,
///   counter it unless its controller pays {2}." Fires on the live
///   <c>TargetsChosenEvent</c> and counters via the live ResolutionContext
///   stack (CR 608 / 701.5b). Same shape as Kappa Cannoneer's Ward {4}.
///
/// - <b>First-main-phase mana trigger (CR 500.2 / 603.6a / 106.4)</b>: "At the
///   beginning of your first main phase, add {G}{G}." A
///   <see cref="TriggeredAbility"/> whose condition is
///   <see cref="Triggers.OnStepBegin"/> on
///   <see cref="StepStateType.PreCombatMain"/> (the precombat / "first" main
///   phase — CR 505.1a), restricted to this creature's controller's own
///   turns. Resolution adds {G}{G} to the controller's mana pool via
///   <see cref="Fx.AddMana"/> (CR 106.4). No target — same trigger seam as
///   Klothys God of Destiny's first-main-phase ability, minus the target /
///   branch logic. The mana is added on every precombat main phase the
///   creature is on the battlefield under the controller's turn (the printed
///   "your first main phase" denotes the precombat main, distinct from a
///   postcombat second main — CR 505.1a).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape + Ward marker + the trigger attached
///   to the card for observability; the trigger is NOT registered with a
///   <see cref="TriggerManager"/> (no bus-driven firing). This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?)"/> — fully wired: the
///   first-main-phase trigger AND the Ward trigger are registered with the
///   supplied <see cref="TriggerManager"/> for live firing.
/// </summary>
[CardName("Hulking Raptor")]
public static class HulkingRaptorFactory
{
    public const string CardName = "Hulking Raptor";
    public const string Slug = "hulking-raptor";

    /// <summary>CR 702.21 — printed Ward cost: {2}.</summary>
    public const string WardCost = "{2}";

    /// <summary>CR 106.4 — mana added at the controller's first (precombat)
    /// main phase.</summary>
    public const string ManaProduced = "{G}{G}";

    /// <summary>
    /// Construct Hulking Raptor with no live trigger-manager wiring. The Ward
    /// marker keyword and the first-main-phase trigger are attached to the
    /// card shape; neither trigger is registered for bus-driven firing.
    /// Suitable for dispatcher / structural tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Hulking Raptor with optional trigger-manager wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, both the first-main-phase mana
    /// trigger and the Ward {2} trigger are registered for live firing. May be
    /// null — both triggers are still attached to the card shape.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Dinosaur, {2}{G}{G}, 5/3). The JSON carries no abilities — printed
        // behaviours are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Ward {2} (CR 702.21) — marker keyword PLUS the real battlefield-
        // attached Ward triggered ability. Same one-liner Kappa Cannoneer
        // uses: the WardTriggerWiring helper builds + (optionally) registers
        // the "counter unless its controller pays {2}" trigger.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Ward", card, owner));
        WardTriggerWiring.Attach(
            new WardEffect(card, ManaCost.Parse(WardCost)),
            owner,
            triggers: triggers);

        // ----------------------------------------------------------------
        // First-main-phase mana trigger — CR 500.2 / 603.6a / 106.4.
        //   "At the beginning of your first main phase, add {G}{G}."
        // Fires on the precombat ("first") main phase (CR 505.1a), restricted
        // to the controller's own turns via Triggers.OnStepBegin. Resolution
        // adds {G}{G} to the controller's mana pool (CR 106.4). No target.
        // ----------------------------------------------------------------
        var triggerEffect = new Effect(
            $"{CardName}: add {ManaProduced} at first main phase",
            _ =>
            {
                var controller = card.Controller ?? owner;
                Fx.AddMana(controller, ManaProduced);
                return ValueTask.CompletedTask;
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.PreCombatMain),
            effects: new IEffect[] { triggerEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
