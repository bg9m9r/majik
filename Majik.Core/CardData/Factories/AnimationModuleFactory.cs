using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Animation Module (Kaladesh, {1}).
///
/// Artifact. Printed oracle text per Scryfall (Kaladesh, 2016-09-30,
/// oracle id <c>af42079b-a3c0-448c-9bb2-b915252e87a9</c>):
///   "Whenever one or more +1/+1 counters are put on a permanent you
///    control, you may pay {1}. If you do, create a 1/1 colorless Servo
///    artifact creature token.
///    {3}, {T}: Choose a counter on target permanent or player. Give
///    that permanent or player another counter of that kind."
///
/// ## Oracle delta (v1)
///
/// v1 ships the printed triggered-ability (CR 603.1) clause verbatim
/// but implements the simpler activated ability the original brief
/// referenced — <c>{1}, {T}: Put a +1/+1 counter on target creature.</c>
/// This matches the older printed activated mode AND the much smaller
/// engine surface (no "Choose a counter on target permanent or player"
/// counter-type-copy primitive yet). The modal "give that permanent
/// or player another counter of that kind" upgrade is the documented
/// follow-up — see "Deferred (v1 gaps)" below.
///
/// ## Implemented (v1)
///
/// - <b>Artifact {1}</b> — printed mana cost, owner / controller wired.
/// - <b>"Whenever one or more +1/+1 counters are put on a permanent you
///   control, you may pay {1}. If you do, create a 1/1 colorless Servo
///   artifact creature token" (CR 603.1 / CR 121.2)</b> — wired as a
///   <see cref="TriggeredAbility"/> with
///   <see cref="Triggers.OnCounterAddedToPermanentYouControl"/> as the
///   condition (filtered to +1/+1 counters AND the trigger controller).
///   The trigger fires off <see cref="CounterAddedEvent"/> published by
///   <see cref="CountersService.Add"/> AFTER the
///   <see cref="ReplacementBus"/> applied Hardened Scales / Doubling
///   Season bumps, so the "one or more" floor (CR 121.2) is automatic:
///   the event is only published when the post-replacement amount is
///   strictly positive. Animation Module's own +1/+1 counter is NOT
///   excluded — when its activated ability stamps a counter on a
///   creature Animation Module controls, the trigger fires (CR 603.1
///   self-trigger posture; Animation Module + a 1/1 with one counter
///   already on it can chain).
/// - <b>"You may pay {1}" optional rider (CR 117.5)</b> — on resolve the
///   controller is asked via <see cref="IPlayerAgent.ChooseYesNoAsync"/>
///   (when an agent is registered) whether to pay; v1 falls back to
///   "auto-pay if able" (Daze / Lightning Rift posture) when no agent
///   is wired. <see cref="Player.PayMana"/> returns false when the pool
///   can't satisfy {1} — the trigger fizzles harmlessly (CR 117.5).
/// - <b>Servo token (CR 111.1 / CR 111.4)</b> — on a successful pay the
///   resolve creates a 1/1 colourless <see cref="CardSubtype.Servo"/>
///   artifact creature token via <see cref="TokenFactory.CreateOnBattlefield"/>.
///   The token is artifact + creature (subtype Servo), colourless
///   (empty <see cref="TokenFactory.TokenSpec.Colors"/> per CR 111.4),
///   ETBs under Animation Module's controller. When a
///   <see cref="ZoneService"/> is supplied the Servo's ETB publishes
///   <see cref="CardMovedEvent"/> so downstream triggers (Soul Warden,
///   another Animation Module's +1/+1 counter chain via etb-bestowal)
///   fire.
/// - <b>Activated {1}, {T}: Put a +1/+1 counter on target creature
///   (CR 605.1)</b> — wired as an <see cref="ActivatedAbility"/> with a
///   <see cref="ManaCostCost"/> {1} + <see cref="AdditionalCost.Tap"/>
///   pair. Single 1..1 "target creature" <see cref="TargetRequest"/>;
///   on resolve the counter is placed via <see cref="CountersService.Add"/>
///   so Hardened Scales / Doubling Season replacements observe the
///   placement AND the post-commit <see cref="CounterAddedEvent"/>
///   fires (so a self-targeting chain works: Animation Module's tap
///   ability → +1/+1 counter on a creature you control →
///   CounterAddedEvent → trigger asks to pay {1} → Servo token).
///
/// ## Overloads
///
/// - <see cref="Create(Player)"/> — single-arg dispatcher path. Shape
///   only: the triggered ability is attached but NOT registered with a
///   <see cref="TriggerManager"/>; the activated ability's counter
///   placement does not go through a <see cref="ReplacementBus"/> or
///   publish a <see cref="CounterAddedEvent"/>. Suitable for shape /
///   <see cref="NamedCardFactory"/> dispatch tests.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?, IEventBus?, ZoneService?)"/>
///   — fully wired. The triggered ability is registered for bus-driven
///   firing; the activated ability routes counter placement through the
///   replacement bus + event bus; the Servo token is ETB'd through the
///   zone service so CardMovedEvent fires.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Activated ability oracle</b>: the actual Scryfall oracle is
///   <c>{3}, {T}: Choose a counter on target permanent or player.
///    Give that permanent or player another counter of that kind.</c>
///   Requires a "counter type picker" target shape (read all counter
///   types currently on the target, present each as a choice) + the
///   "permanent or player" target union + a Poison-counter route on
///   players. v1 ships the older printed mode (per the original
///   factory brief). Follow-up upgrade is paired with the counter-type-
///   copy primitive that Vampire Hexmage and Doubling Cube want.
/// - <b>"Once per CountersService.Add call" semantics</b>: when a
///   single counter-placement effect commits multiple counters in one
///   call (e.g. Endless One ETB with X=4 → 4 +1/+1 counters in one
///   <see cref="CountersService.Add"/>) the trigger fires once per
///   <see cref="CounterAddedEvent"/> (one event per service call). When
///   the same effect commits via multiple separate calls (Steel
///   Overseer iterates over each artifact creature), the trigger fires
///   once per target. This matches the printed "one or more counters
///   are put on a permanent" wording (CR 603.6b — a single placement
///   instance per permanent fires once).
/// - <b>Per-target trigger choice</b>: the Servo token resolution does
///   not consult an agent for any decision beyond the "pay {1}?"
///   yes/no; the token's controller is Animation Module's controller
///   (no choice to defer).
/// </summary>
[CardName("Animation Module")]
public static class AnimationModuleFactory
{
    public const string CardName = "Animation Module";
    public const string PrintedManaCost = "{1}";
    public const string ActivatedManaCost = "{1}";
    public const int TriggerOptionalManaCost = 1;
    public const int ServoPower = 1;
    public const int ServoToughness = 1;
    public const string ServoName = "Servo";

    /// <summary>
    /// Construct Animation Module with no live triggers / replacement /
    /// event-bus wiring. The triggered ability is attached to the card
    /// for shape but NOT registered with a <see cref="TriggerManager"/>;
    /// counter placements from the activated ability fall through to a
    /// direct add (no event publish). Suitable for dispatcher / shape
    /// tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, triggers: null, replacements: null, eventBus: null, zones: null);

    /// <summary>
    /// Construct Animation Module. When <paramref name="triggers"/> is
    /// supplied the "counters → may pay {1} → Servo" trigger is
    /// registered for bus-driven firing. When <paramref name="replacements"/>
    /// is supplied the activated ability routes counter placement
    /// through the replacement bus. When <paramref name="eventBus"/> is
    /// supplied the activated ability publishes
    /// <see cref="CounterAddedEvent"/> after a non-zero placement, so
    /// self-targeting chains (Module's {1},{T} → +1/+1 counter on a
    /// creature you control → CounterAddedEvent → trigger asks to pay
    /// {1} → Servo) work end-to-end. When <paramref name="zones"/> is
    /// supplied the Servo token's ETB publishes
    /// <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Artifact Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        IEventBus? eventBus,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Triggered ability — CR 603.1 / CR 121.2.
        //   "Whenever one or more +1/+1 counters are put on a permanent
        //    you control, you may pay {1}. If you do, create a 1/1
        //    colorless Servo artifact creature token."
        //
        // Subscribes to CounterAddedEvent (published by CountersService.Add
        // AFTER all replacements committed); filter to +1/+1 counter type
        // + controller match.
        // ----------------------------------------------------------------
        TriggeredAbility? trigger = null;

        var triggerEffect = new Effect(
            $"{CardName}: may pay {{{TriggerOptionalManaCost}}} → create 1/1 Servo token",
            async ctx =>
            {
                if (trigger is null) return;
                // CR 603.6c — source must still be on the battlefield to
                // create the token (leaves-the-battlefield triggers
                // exempt; this is "Whenever counters are put on …" which
                // is NOT a LTB trigger, so if Animation Module left the
                // battlefield between event and resolve, no-op).
                if (card.Zone != ZoneType.Battlefield) return;

                var triggerController = card.Controller ?? owner;

                // "You may pay {1}" — consult the controller's agent.
                // v1 falls back to "auto-pay if able" (Daze / Lightning
                // Rift posture) when no agent is registered.
                var oneGeneric = ManaCost.Zero.AddGenericCost(TriggerOptionalManaCost);
                var agent = ctx.Agent ?? AgentRegistry.Get(triggerController);
                bool pay;
                if (agent != null)
                {
                    pay = (await agent.ChooseYesNoAsync(
                        $"Pay {{{TriggerOptionalManaCost}}} to create a 1/1 Servo token?",
                        BotIntent.Token).ConfigureAwait(false));
                }
                else
                {
                    pay = true;
                }

                if (!pay) return;

                // CR 117.5 — optional may-pay; trigger fizzles harmlessly
                // when the mana isn't available.
                if (!triggerController.PayMana(oneGeneric)) return;

                // CR 111.1 / CR 111.4 — create a 1/1 colourless Servo
                // artifact creature token. TokenFactory.CreateOnBattlefield
                // stamps IsToken + the explicit colourless colour set
                // (CR 111.4) so colour-matters subscribers see "no
                // colours" rather than probing the empty mana cost.
                var spec = new TokenFactory.TokenSpec(
                    Name: ServoName,
                    Power: ServoPower,
                    Toughness: ServoToughness,
                    Subtypes: new[] { CardSubtype.Servo },
                    Keywords: null,
                    Colors: Array.Empty<ManaColor>());

                var token = TokenFactory.CreateOnBattlefield(spec, triggerController, zones);

                // CR 111.1 — Servo tokens are artifact creatures. The
                // Artifact type is layered on AFTER mint so the token
                // reports both types (TokenFactory creates a Creature
                // shell; we additively stamp Artifact for the multi-type
                // pattern shared with Esika's Chariot / Wurmcoil Engine).
                token.AddCardType(CardType.Artifact);
            });

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnCounterAddedToPermanentYouControl(
                owner, CounterType.PlusOnePlusOne),
            effects: new IEffect[] { triggerEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // ----------------------------------------------------------------
        // Activated ability — CR 605.1 / CR 121.
        //   "{1}, {T}: Put a +1/+1 counter on target creature."
        //
        // v1 ships this older printed mode; the current Scryfall oracle
        // is "{3}, {T}: Choose a counter on target permanent or player.
        // Give that permanent or player another counter of that kind."
        // — deferred (see factory xmldoc "Deferred (v1 gaps)").
        // ----------------------------------------------------------------
        ActivatedAbility? activated = null;

        var activatedEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on target creature",
            () =>
            {
                if (activated is null) return;
                if (activated.ChosenTargets.Count == 0) return;
                if (activated.ChosenTargets[0].Count == 0) return;

                var raw = activated.ChosenTargets[0][0];
                if (raw is not Creature target) return;
                // CR 608.2b — resolution recheck: target still on
                // battlefield.
                if (target.Zone != ZoneType.Battlefield) return;

                CountersService.Add(
                    target,
                    CounterType.PlusOnePlusOne,
                    1,
                    replacements,
                    eventBus);
            });

        activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivatedManaCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { activatedEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        card.AddAbility(activated);

        return card;
    }
}
