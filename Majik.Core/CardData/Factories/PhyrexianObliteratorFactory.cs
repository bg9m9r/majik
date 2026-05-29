using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phyrexian Obliterator (New Phyrexia, {B}{B}{B}{B}).
///
/// Creature — Phyrexian Horror 5/5. Oracle text (Scryfall verified):
///   "Trample
///    Whenever a source deals damage to this creature, that source's
///    controller sacrifices that many permanents of their choice."
///
/// ## Implemented (v1)
/// - 5/5 Creature — Phyrexian Horror, mana cost {B}{B}{B}{B}.
/// - <b>Trample</b> (CR 702.19): <see cref="KeywordAbility"/> marker — read
///   by <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> for combat
///   damage assignment. Same shape as the granted-Trample factories
///   (Temur Battle Rage / Berserk).
/// - <b>Damage-received trigger</b> (CR 603.1) wired over
///   <see cref="DamageDealtEvent"/> filtered to <c>TargetCard == this</c> —
///   the identical wiring style to <see cref="BorosReckonerFactory"/>.
///   The triggering damage's amount and the source's controller are
///   captured off the event (closure shared with the resolved effect). On
///   resolution the source's controller sacrifices that many permanents
///   of their choice (CR 701.16) — see <see cref="Sacrifice"/>.
///
/// ## "That source's controller" (CR 119.1 / 109.5)
/// The source of damage is normally a card (a creature in combat, a burn
/// spell, an ability source). Its controller is read off
/// <see cref="DamageDealtEvent.SourceCard"/>'s <see cref="Card.Controller"/>;
/// when the event carries no source card (rare engine-internal pings) the
/// effect falls back to <see cref="DamageDealtEvent.SourcePlayer"/>. If no
/// controller can be resolved the effect is a clean no-op.
///
/// ## "Sacrifices that many permanents of their choice" (CR 701.16)
/// The chooser is the source's controller. The choice is agent-driven when
/// an <see cref="IPlayerAgent"/> is registered for that player (via
/// <see cref="AgentRegistry"/>), one permanent at a time, with a
/// deterministic first-permanent fallback otherwise — the same posture as
/// <see cref="ArchonOfCrueltyFactory"/>'s opponent-sacrifice step. The
/// count clamps to the number of permanents the controller actually
/// controls (CR 701.16e — "if you can't sacrifice that many, sacrifice as
/// many as you can"). Each sacrifice routes through <see cref="Fx.Sacrifice"/>
/// (CR 701.16 — bypasses Indestructible / regeneration; not a "destroy").
///
/// ## Replacement-effect note
/// Like Boros Reckoner, this is modelled as a triggered ability over
/// <see cref="DamageDealtEvent"/>: the damage resolves on the Obliterator
/// first (marked damage / SBAs apply), then the trigger goes on the stack
/// and the sacrifice happens on resolution. The printed card is itself a
/// triggered ability ("Whenever a source deals damage to this creature,
/// …"), so this matches the card faithfully — there is no replacement
/// shift involved.
/// </summary>
[CardName("Phyrexian Obliterator")]
public static class PhyrexianObliteratorFactory
{
    public const string CardName = "Phyrexian Obliterator";
    public const string PrintedManaCost = "{B}{B}{B}{B}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>
    /// Construct Phyrexian Obliterator with no live event-bus /
    /// TriggerManager wiring. The damage-received trigger is attached for
    /// shape but not registered. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Phyrexian Obliterator with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the damage-received trigger
    /// is registered so a <see cref="DamageDealtEvent"/> automatically
    /// queues the ability.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Optional event bus (parity with other
    /// damage-trigger factories; reserved for future event routing).</param>
    /// <param name="triggers">TriggerManager — when supplied the
    /// damage-received trigger is registered.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: null,
            subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Horror });

        card.SetOwner(owner);
        card.SetController(owner);

        // Trample (CR 702.19). Keyword marker read by
        // CombatAbilities.HasTrample for combat damage assignment.
        card.AddAbility(new KeywordAbility("Trample", source: card, controller: owner));

        // ----------------------------------------------------------------
        // Damage-received trigger — CR 603.1.
        //   "Whenever a source deals damage to this creature, that source's
        //    controller sacrifices that many permanents of their choice."
        // Matches DamageDealtEvent (and its CombatDamageDealtEvent subclass)
        // where TargetCard is this Obliterator. The amount and the source's
        // controller are captured in closures shared with the resolved
        // effect. (eventBus reserved for parity with sibling factories.)
        // ----------------------------------------------------------------
        _ = eventBus;
        int capturedAmount = 0;
        Player? sourceController = null;

        var effect = new Effect(
            $"{CardName}: source's controller sacrifices captured-amount permanents of their choice",
            () =>
            {
                var amount = capturedAmount;
                var chooser = sourceController;

                // Clear captured state so a later fire can't reuse it.
                capturedAmount = 0;
                sourceController = null;

                if (amount <= 0 || chooser is null) return;
                Sacrifice(chooser, amount);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<DamageDealtEvent>((e, _) =>
            {
                if (e.TargetCard is not Creature recv) return false;
                if (!ReferenceEquals(recv, card)) return false;
                if (e.Amount <= 0) return false; // CR 119.4 — 0 damage is no damage.

                // CR 119.1 / 109.5 — "that source's controller". Prefer the
                // source card's controller; fall back to the event's source
                // player for sourceless engine-internal pings.
                var controller = e.SourceCard?.Controller ?? e.SourcePlayer;
                if (controller is null) return false;

                capturedAmount = e.Amount;
                sourceController = controller;
                return true;
            }),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// CR 701.16 — <paramref name="chooser"/> sacrifices up to
    /// <paramref name="count"/> permanents they control, of their choice.
    /// Agent-driven one at a time when a registered <see cref="IPlayerAgent"/>
    /// is available; deterministic first-permanent fallback otherwise. The
    /// count clamps to the number of permanents controlled (CR 701.16e).
    /// </summary>
    private static void Sacrifice(Player chooser, int count)
    {
        var agent = AgentRegistry.Get(chooser);

        for (var i = 0; i < count; i++)
        {
            // Re-snapshot each iteration — the previous sacrifice removed a
            // permanent, so the candidate list shrinks.
            var candidates = chooser.Zones.Battlefield.GetCards()
                .Cast<ICard>()
                .ToList();
            if (candidates.Count == 0) return; // CR 701.16e — nothing left.

            var pick = PickSacrifice(chooser, candidates, agent);
            Fx.Sacrifice(pick);
        }
    }

    private static ICard PickSacrifice(Player chooser, List<ICard> candidates, IPlayerAgent? agent)
    {
        if (agent is null) return candidates[0];

        var pick = agent
            .ChooseFromBattlefieldAsync(chooser, candidates, BotIntent.None)
            .GetAwaiter().GetResult();

        // Validate the agent's pick is a live permanent the chooser still
        // controls on the battlefield; otherwise fall back deterministically.
        if (pick is null
            || pick.Zone != ZoneType.Battlefield
            || !ReferenceEquals(pick.Controller, chooser))
        {
            return candidates[0];
        }
        return pick;
    }
}
