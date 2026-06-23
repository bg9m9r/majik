using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Emeria, the Sky Ruin (Worldwake).
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    At the beginning of your upkeep, if you control seven or more Plains,
///    you may return target creature card from your graveyard to the
///    battlefield.
///    {T}: Add {W}."
///
/// ## Identity comes from JSON
///
/// Name / Land type and the <b>{T}: Add {W}</b> mana ability are loaded from
/// the embedded JSON definition (<c>emeria-the-sky-ruin.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>, mirroring the JSON-driven posture of
/// <see cref="JwarIsleRefugeFactory"/>. The enters-tapped replacement and the
/// upkeep reanimation trigger are attached in code (the declarative schema
/// models neither).
///
/// ## Implemented (v1)
/// - <b>Land, nonbasic, no printed subtype</b>. Owner / controller wired.
/// - <b>{T}: Add {W}</b> — single <see cref="ManaAbility"/> producing one
///   white mana (CR 605.1 — mana ability, no stack), from JSON.
/// - <b>"This land enters tapped." (CR 614.1c)</b> — an unconditional
///   <see cref="EntersTappedReplacement"/> registered on the supplied
///   <see cref="ReplacementBus"/>. The shape-only single-arg path skips the
///   registration (no bus available), matching
///   <see cref="JwarIsleRefugeFactory"/>.
/// - <b>Upkeep triggered ability with intervening-if (CR 603.4)</b>:
///   "At the beginning of your upkeep, if you control seven or more Plains,
///   you may return target creature card from your graveyard to the
///   battlefield." Wired via <see cref="Triggers.OnStepBegin"/> filtered to
///   the controller's own Upkeep step (CR 500.4 — same posture as
///   <see cref="PhyrexianArenaFactory"/> / <see cref="BitterblossomFactory"/>).
///   The intervening-if counts permanents with the
///   <see cref="CardSubtype.Plains"/> subtype on the controller's battlefield
///   and gates the trigger at ≥ 7 (CR 603.4 — checked at stack-push time; the
///   count includes any permanent with the Plains subtype regardless of basic
///   / nonbasic, mirroring <see cref="MysticSanctuaryFactory"/>'s island
///   count). Emeria itself has no Plains subtype, so it is never part of the
///   count (no "other" qualifier needed). A 1..1 <see cref="TargetRequest"/>
///   declares the "target creature card in your graveyard" slot; the "you may"
///   is a first-class <see cref="OptionalTriggerPrompt"/> (CR 603.5) tagged
///   <see cref="BotIntent.Reanimate"/> so the default agent heuristic
///   auto-accepts (preserving the auto-take posture). On resolution the chosen
///   creature card is moved Graveyard → battlefield under the controller's
///   control via <see cref="Fx.ReturnFromGraveyardToBattlefield"/>, routed
///   through the registered <see cref="ZoneService"/> when present so ETB
///   triggers on the reanimated creature fire (CR 603.6a). CR 608.2b
///   illegal-on-resolution rechecks gate out a target that is no longer a
///   creature card in the controller's graveyard.
///
/// ## Lifecycle — two overloads
/// The single-arg <see cref="Create(Player)"/> overload produces the correct
/// card shape — the upkeep trigger is attached for shape inspection but not
/// registered with a <see cref="TriggerManager"/>, and the enters-tapped
/// replacement is omitted (no bus). This is the overload
/// <see cref="NamedCardFactory"/> dispatches to. Use the
/// <see cref="Create(Player, ReplacementBus?, TriggerManager?)"/> overload for
/// full bus-driven wiring.
///
/// ## Deferred (v1 gaps)
/// - <b>Resolution-time intervening-if recheck</b>: CR 603.4 evaluates the
///   condition at trigger time AND on resolution.
///   <see cref="TriggeredAbility.CanBePutOnStack"/> runs it at stack-push
///   time; a second recheck at resolution is deferred (same posture as
///   <see cref="MysticSanctuaryFactory"/>).
/// - <b>Agent target legality at choose-time</b>: <see cref="TargetRequest"/>
///   carries empty <c>LegalCandidates</c>; the live trigger/activation path
///   fills the choice and the resolution guard enforces the creature-card +
///   graveyard + controller checks per CR 608.2b.
/// </summary>
[CardName("Emeria, the Sky Ruin")]
public static class EmeriaTheSkyRuinFactory
{
    public const string CardName = "Emeria, the Sky Ruin";
    public const string Slug = "emeria-the-sky-ruin";
    public const int PlainsThreshold = 7;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// CR 603.5 — the "you may" gate on the upkeep reanimation. The
    /// <see cref="BotIntent.Reanimate"/> classifier auto-accepts under the
    /// default agent heuristic (preserving the auto-take posture); a human /
    /// search agent may decline, in which case the trigger resolves as a no-op.
    /// </summary>
    private static readonly OptionalTriggerPrompt ReanimatePrompt =
        new("Return target creature card from your graveyard to the battlefield?",
            BotIntent.Reanimate);

    /// <summary>
    /// Construct Emeria, the Sky Ruin with no runtime service wiring (the
    /// shape / dispatcher path). The {T}: Add {W} mana ability (from JSON) and
    /// the upkeep trigger are attached but the trigger is not registered with a
    /// <see cref="TriggerManager"/>, and the enters-tapped replacement is
    /// omitted (no bus). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null, triggers: null);

    /// <summary>
    /// Construct Emeria, the Sky Ruin with optional runtime services. When
    /// <paramref name="replacements"/> is supplied the unconditional
    /// enters-tapped restriction (CR 614.1c) is registered. When
    /// <paramref name="triggers"/> is supplied the upkeep reanimation trigger
    /// is registered so an Upkeep <see cref="Majik.Core.Events.StepStartedEvent"/>
    /// for the controller automatically places it on the stack (subject to the
    /// 7+ Plains intervening-if).
    /// </summary>
    public static Land Create(Player owner, ReplacementBus? replacements, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {W} mana ability come from JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // "This land enters tapped." — CR 614.1c. Unconditional enters-tapped
        // replacement. Shape-only path (no ReplacementBus) skips registration;
        // same posture as JwarIsleRefugeFactory.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // Upkeep trigger with intervening-if (CR 603.4 / CR 500.4).
        //   "At the beginning of your upkeep, if you control seven or more
        //    Plains, you may return target creature card from your graveyard
        //    to the battlefield."
        // Triggers.OnStepBegin filters StepStartedEvent on (Upkeep,
        // controller) so only the controller's own upkeeps fire. The
        // intervening-if counts Plains on the controller's battlefield at
        // stack-push time and gates at >= 7.
        // ----------------------------------------------------------------
        TriggeredAbility? upkeep = null;
        var reanimateEffect = new Effect(
            $"{CardName}: return target creature card from your graveyard to the battlefield",
            () =>
            {
                if (upkeep is null) return;
                if (upkeep.ChosenTargets.Count == 0) return;
                if (upkeep.ChosenTargets[0].Count == 0) return;
                if (upkeep.ChosenTargets[0][0] is not Card target) return;

                // CR 608.2b — illegal-on-resolution rechecks: still a creature
                // card in the controller's graveyard.
                if (target.Zone != ZoneType.Graveyard) return;
                if (!owner.Zones.Graveyard.ContainsCard(target)) return;
                if (!target.HasType(CardType.Creature)) return;

                // CR 701.20 — reanimate to the controller's battlefield, routed
                // through the registered ZoneService so ETB triggers on the
                // reanimated creature fire (CR 603.6a).
                var zones = ZoneServiceRegistry.Get(owner);
                Fx.ReturnFromGraveyardToBattlefield(target, owner, zones);
            });

        upkeep = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.Upkeep),
            effects: new IEffect[] { reanimateEffect },
            interveningIf: () => CountPlains(owner) >= PlainsThreshold,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            optionalPrompt: ReanimatePrompt);

        land.AddAbility(upkeep);
        triggers?.RegisterTriggeredAbility(upkeep);

        return land;
    }

    /// <summary>
    /// Count permanents on <paramref name="controller"/>'s battlefield that
    /// have the <see cref="CardSubtype.Plains"/> subtype (CR 603.4 — checked
    /// at trigger time). Includes all Plains regardless of basic / nonbasic
    /// and any permanent that has been granted the Plains subtype (dual lands
    /// such as Sacred Foundry, retype effects, etc.), mirroring Mystic
    /// Sanctuary's island count. Emeria, the Sky Ruin itself has no Plains
    /// subtype, so it is never counted.
    /// </summary>
    private static int CountPlains(Player controller) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Plains));
}
