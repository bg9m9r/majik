using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Asylum Visitor (Shadows over Innistrad, {1}{B}).
///
/// Creature — Vampire Wizard 3/1. Oracle text (current Scryfall):
///   "At the beginning of each player's upkeep, if that player has no cards in
///    hand, you draw a card and you lose 1 life.
///    Madness {1}{B} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Shape source
/// Card identity (name, {1}{B}, 3/1, Creature — Vampire Wizard) is loaded from
/// <c>Majik.Core/CardData/Cards/asylum-visitor.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The upkeep trigger + Madness wiring are
/// attached in code below.
///
/// ## Implemented (v1)
///
/// - 3/1 Creature — Vampire Wizard at {1}{B}.
/// - <b>Each-player's-upkeep triggered ability (CR 603.1 / CR 500.4) with an
///   intervening-if (CR 603.4)</b>: fires on <see cref="StepStartedEvent"/>
///   matching <see cref="PhaseStateType.Upkeep"/> for ANY player (the printed
///   text reads "each player's upkeep", not "your upkeep" — same symmetric
///   shape as <see cref="SulfuricVortexFactory"/>). The condition captures the
///   active upkeep player off <see cref="StepStartedEvent.Player"/>; the
///   intervening-if re-checks "that player has no cards in hand" at trigger time
///   AND on resolution (CR 603.4). On resolution the <b>controller</b> (read
///   live via <c>card.Controller</c>) draws a card and loses 1 life — NOT the
///   upkeep player. The draw routes through <see cref="Fx.DrawCards"/> (CR 121.1
///   / CR 614 replacement-aware) and the life loss through
///   <see cref="Player.LoseLife"/>.
/// - <b>Madness {1}{B} (CR 702.35)</b> — wired through the shared reusable
///   mechanic: <see cref="MadnessReplacement"/> (discard → exile) +
///   <see cref="Costs.MadnessAlternativeCost"/> (cast from exile for {1}{B}).
///   The factory registers the discard-replacement on the supplied
///   <see cref="ReplacementBus"/> and exposes the madness cost. Same posture as
///   <see cref="FieryTemperFactory"/>, but on a permanent — Madness is
///   card-type-agnostic (the redirect is a Hand → Graveyard
///   <see cref="ZoneMoveIntent"/> rewrite; casting from exile resolves the
///   creature onto the battlefield via the normal stack path).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The upkeep trigger is attached
///   for shape observability; nothing is registered on a trigger manager or
///   replacement bus. Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, TriggerManager?)"/> — upkeep trigger registered.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?)"/> — fully
///   wired: upkeep trigger + Madness discard-replacement.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Full <see cref="DamageDealtEvent"/> / draw-replacement nuance</b>: the
///   life loss is delivered via <see cref="Player.LoseLife"/> (life loss is not
///   damage, so this is correct for the "you lose 1 life" clause — CR 119.3).
///   No deferral there. The Madness cast-or-graveyard window after the
///   discard→exile redirect is driven by <see cref="Keywords.MadnessHelper"/>
///   at the call site (same as every other Madness card); this factory supplies
///   the replacement + cost only.
/// </summary>
[CardName("Asylum Visitor")]
public static class AsylumVisitorFactory
{
    public const string CardName = "Asylum Visitor";
    public const string PrintedManaCost = "{1}{B}";
    public const string MadnessCost = "{1}{B}";
    public const int LifeLoss = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("asylum-visitor");

    /// <summary>The madness alternative cost for casting from exile (CR 702.35).</summary>
    public static Costs.MadnessAlternativeCost MadnessAltCost { get; } =
        new(ManaCost.Parse(MadnessCost));

    /// <summary>
    /// Construct Asylum Visitor with no live runtime services. The upkeep
    /// trigger is attached for shape observability; nothing is registered on a
    /// trigger manager or replacement bus. Suitable for dispatcher / shape
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Asylum Visitor with optional <see cref="TriggerManager"/>
    /// wiring (no Madness replacement bus).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers) =>
        Create(owner, triggers, replacements: null);

    /// <summary>
    /// Construct Asylum Visitor with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager — when supplied, the
    /// each-player's-upkeep trigger registers so the bus drives it
    /// automatically.</param>
    /// <param name="replacements">Replacement bus — when supplied, the Madness
    /// discard → exile replacement (CR 702.35) registers so discarding this
    /// card sends it to exile (castable for {1}{B}) instead of the
    /// graveyard.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Each-player's-upkeep trigger with intervening-if — CR 603.1 /
        // CR 500.4 / CR 603.4.
        //   "At the beginning of each player's upkeep, if that player has no
        //    cards in hand, you draw a card and you lose 1 life."
        // The condition captures the active upkeep player off the
        // StepStartedEvent (symmetric — fires on EVERY player's upkeep, like
        // Sulfuric Vortex). The intervening-if re-checks "that player has no
        // cards in hand" both at trigger time (CanBePutOnStack) and on
        // resolution (CR 603.4). On resolution the CONTROLLER draws + loses
        // 1 life — not the upkeep player.
        // ----------------------------------------------------------------
        Player? upkeepPlayer = null;

        var upkeepCondition = new EventTriggerCondition<StepStartedEvent>((e, _) =>
        {
            if (e.StepType != PhaseStateType.Upkeep) return false;
            upkeepPlayer = e.Player;
            return true;
        });

        // CR 603.4 — "if that player has no cards in hand". Re-checked when the
        // trigger would be put on the stack and again on resolution.
        bool InterveningIf() =>
            upkeepPlayer != null && !upkeepPlayer.Zones.Hand.GetCards().Any();

        var upkeepEffect = new Effect(
            $"{CardName}: controller draws a card and loses {LifeLoss} life",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                // CR 603.4 — re-check the intervening-if on resolution.
                if (!InterveningIf()) return;

                var controller = card.Controller ?? owner;
                // CR 121.1 — "you draw a card" routes through Fx.DrawCards
                // (replacement-aware; marks empty-library draw for SBA).
                Fx.DrawCards(controller, 1);
                // CR 119.3 — "you lose 1 life" (life loss, not damage).
                controller.LoseLife(LifeLoss);
            });

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: upkeepCondition,
            effects: new IEffect[] { upkeepEffect },
            interveningIf: InterveningIf,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        // ----------------------------------------------------------------
        // Madness {1}{B} — CR 702.35. Register the discard → exile
        // replacement so discarding this card sends it to exile (castable
        // for {1}{B}) instead of the graveyard.
        // ----------------------------------------------------------------
        replacements?.Register<ZoneMoveIntent>(new MadnessReplacement(card));

        return card;
    }
}
