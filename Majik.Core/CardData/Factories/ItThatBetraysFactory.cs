using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for It That Betrays (Rise of the Eldrazi, {12}).
///
/// Creature — Eldrazi 11/11. Oracle text (Scryfall, verified):
///   "Annihilator 2 (Whenever this creature attacks, defending player
///    sacrifices two permanents of their choice.)
///    Whenever an opponent sacrifices a nontoken permanent, put that card
///    onto the battlefield under your control."
///
/// ## Implemented (v1)
/// - 11/11 Creature — Eldrazi at {12}; owner / controller wired.
/// - <b>Annihilator 2 (CR 702.86)</b>: shipped via
///   <see cref="AnnihilatorFactory.Build"/> — the per-attacker trigger
///   fires on <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
///   and routes the two sacrifice picks through
///   <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> when an agent
///   selector is supplied; deterministic first-two-permanents fallback
///   otherwise. When an <see cref="IEventBus"/> is supplied each sacrifice
///   publishes a <see cref="PermanentSacrificedEvent"/> — the very surface
///   the steal trigger below feeds on (defending player = sacrificing
///   player, CR 701.16a). A discoverability
///   <see cref="KeywordAbility"/>("Annihilator", arg: 2) marker is stamped
///   alongside.
/// - <b>Sacrifice-steal trigger (CR 603.1 + CR 701.16)</b>: "Whenever an
///   opponent sacrifices a nontoken permanent, put that card onto the
///   battlefield under your control." A <see cref="TriggeredAbility"/> over
///   the dedicated <see cref="PermanentSacrificedEvent"/>, filtered to
///   (<see cref="PermanentSacrificedEvent.SacrificingPlayer"/> is an
///   opponent of the controller AND
///   <see cref="PermanentSacrificedEvent.WasToken"/> is false). On
///   resolution the sacrificed card — already in its owner's graveyard
///   (CR 701.16a) — is pulled onto the controller's battlefield under their
///   control via <see cref="Fx.ReturnFromGraveyardToBattlefield"/> (CR
///   701.20). Steals nontoken permanents only: a token in the graveyard
///   ceases to exist as an SBA (CR 111.7) so there is nothing to put onto
///   the battlefield.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Keyword marker, an unbound
///   Annihilator trigger (agent-less, no bus — first-two-permanents
///   fallback) and the steal trigger are attached but not registered with
///   any <see cref="TriggerManager"/>. Suitable for dispatcher / structural
///   tests.
/// - <see cref="Create(Player, TriggerManager?, IEventBus?, Func{Player, IPlayerAgent?}?)"/>
///   — fully wired. Both triggers register with <paramref name="triggers"/>;
///   the Annihilator trigger publishes the sacrifice event on
///   <paramref name="eventBus"/> so the steal trigger lands on the stack
///   (CR 603.2). The steal effect routes the reanimate through
///   <paramref name="zones"/> when supplied so ETB triggers fire.
///
/// ## Deferred (v1 gaps)
/// - <b>Target prompting on the steal</b>: the steal has no chosen target —
///   it operates on the card carried by the triggering event ("that card"),
///   captured off the live <see cref="PermanentSacrificedEvent"/> at trigger
///   time. No agent prompt is needed.
/// - <b>Annihilator "attacks each combat if able"</b>: It That Betrays has
///   no must-attack restriction (only Ulamog's Crusher prints it), so
///   nothing is deferred there.
/// </summary>
[CardName("It That Betrays")]
public static class ItThatBetraysFactory
{
    public const string CardName = "It That Betrays";
    public const string PrintedManaCost = "{12}";
    public const int Power = 11;
    public const int Toughness = 11;
    public const int AnnihilatorValue = 2;

    /// <summary>
    /// Construct It That Betrays with no live wiring. Keyword marker + an
    /// unbound Annihilator trigger (agent-less, no bus) + the steal trigger
    /// are attached; none are registered with a <see cref="TriggerManager"/>.
    /// Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null, agentSelector: null, zones: null);

    /// <summary>
    /// Construct It That Betrays with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, both triggers register with the
    /// bus so the corresponding events land them on the stack (CR 603.2).</param>
    /// <param name="eventBus">When supplied, the Annihilator trigger
    /// publishes a <see cref="PermanentSacrificedEvent"/> for each
    /// sacrifice so the steal trigger observes it.</param>
    /// <param name="agentSelector">When supplied, the defender's Annihilator
    /// sacrifice picks consult
    /// <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>; null falls back
    /// to deterministic first-two-permanents.</param>
    /// <param name="zones">When supplied, the steal routes the reanimate
    /// through <see cref="ZoneService.MoveCard"/> so ETB triggers fire.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus,
        Func<Player, IPlayerAgent?>? agentSelector,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.86 — Annihilator 2. Marker for discoverability + the wired
        // trigger (AnnihilatorFactory.Build). Passing the bus makes the
        // defender's sacrifices publish PermanentSacrificedEvent — the
        // surface the steal trigger below consumes (CR 701.16a — defending
        // player is the sacrificing player).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility(
            "Annihilator", card, owner, arg: AnnihilatorValue));

        var annihilator = AnnihilatorFactory.Build(
            source: card,
            n: AnnihilatorValue,
            agentSelector: agentSelector,
            eventBus: eventBus);
        card.AddAbility(annihilator);
        triggers?.RegisterTriggeredAbility(annihilator);

        // ----------------------------------------------------------------
        // Sacrifice-steal trigger — CR 603.1 + CR 701.16.
        //   "Whenever an opponent sacrifices a nontoken permanent, put that
        //    card onto the battlefield under your control."
        // Fires on the dedicated PermanentSacrificedEvent, scoped to an
        // OPPONENT of the controller (CR 109.5) and a NONTOKEN permanent
        // (CR 111.7 — a token in the graveyard ceases to exist). "That card"
        // is captured off the live event at match time.
        // ----------------------------------------------------------------
        ICard? capturedCard = null;
        var stealCondition = new EventTriggerCondition<PermanentSacrificedEvent>((e, _) =>
        {
            // "an opponent" — any player that is not the controller (CR
            // 102.1). Re-read the live controller so a control change is
            // honoured.
            var controller = card.Controller ?? owner;
            if (ReferenceEquals(e.SacrificingPlayer, controller)) return false;
            // "a nontoken permanent" — tokens cease to exist in the
            // graveyard (CR 111.7), nothing to steal.
            if (e.WasToken) return false;
            capturedCard = e.SacrificedCard;
            return true;
        });

        var stealEffect = new Effect(
            $"{CardName}: put the sacrificed nontoken permanent onto the battlefield under your control",
            () =>
            {
                if (capturedCard is null) return;
                var controller = card.Controller ?? owner;
                // CR 701.20 — the sacrificed card is in its owner's
                // graveyard (CR 701.16a); pull it onto the controller's
                // battlefield under their control. Guard: only steal a card
                // still resident in a graveyard (CR 608.2 — if it has since
                // moved, the steal does nothing).
                if (capturedCard.Zone != ZoneType.Graveyard) return;
                Fx.ReturnFromGraveyardToBattlefield(capturedCard, controller, zones);
            });

        var stealTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: stealCondition,
            effects: new IEffect[] { stealEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(stealTrigger);
        triggers?.RegisterTriggeredAbility(stealTrigger);

        return card;
    }
}
