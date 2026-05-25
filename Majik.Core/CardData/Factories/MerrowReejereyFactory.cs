using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Merrow Reejerey (Lorwyn, {1}{U}, Creature —
/// Merfolk Rogue 2/2).
///
/// Oracle text:
///   "Whenever you cast a Merfolk spell, choose one —
///     • Tap target permanent.
///     • Untap target permanent."
///
/// ## Implementation
///
/// - 2/2 Merfolk Rogue, mana cost {1}{U}.
/// - <b>Spell-cast trigger</b> (CR 603.1, fires off
///   <see cref="SpellCastEvent"/>): predicate is
///   <c>spell.Controller == this card's controller</c> AND the spell's
///   card has the Merfolk subtype (CR 205.3m). Mirrors the shape used by
///   <see cref="SramSeniorEdificerFactory"/>.
/// - <b>Modal "Choose one — Tap target permanent; or Untap target
///   permanent." (CR 700.2)</b>: the engine does not yet model
///   modal-triggered abilities natively (see
///   <see cref="UmezawasJitteFactory"/>'s notes on per-mode fan-out for
///   activated abilities). Triggered abilities have no
///   <c>ChooseModeAsync</c> hook, so v1 collapses the binary mode pick
///   into a deterministic "useful flip" at resolution — same posture as
///   <see cref="PestermiteFactory"/>'s "tap or untap" choice: untap a
///   tapped target, tap an untapped one. This always produces an
///   observable change in board state and is what an agent would pick
///   in nearly every realistic scenario. When the modal-triggered queue
///   ships, this collapses to a real two-mode prompt.
/// - <b>Target permanent 1..1</b> — declared via <see cref="TargetRequest"/>
///   on the triggered ability. <c>LegalCandidates</c> left empty (same
///   posture as Pestermite / Snapcaster / Subtlety — the live agent
///   enumerates the battlefield itself). Intent defaults to
///   <see cref="BotIntent.None"/> — the enum has no Untap/Tap flag and
///   the deterministic "useful flip" already produces an observable
///   change, so a coarse bot intent isn't required for v1.
/// - <b>Resolution-time legality</b>: the chosen permanent must still be
///   on the battlefield (CR 608.2b — illegal-on-resolution → clean no-op).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Trigger is attached for
///   inspection; not registered (no trigger manager supplied).
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully
///   wired. Trigger registered with <paramref name="triggers"/> so
///   <see cref="SpellCastEvent"/>s on the bus route it to the stack.
///
/// ## Deferred (v1 gaps)
/// - <b>Native modal-triggered ability</b>: v1 collapses the binary
///   mode pick to a "useful flip"; real agent-driven mode prompts ship
///   alongside the broader modal-trigger queue (same gap noted on
///   Pestermite, Snapcaster Mage).
/// - <b>Merrow Reejerey self-cast</b>: by Comp Rules the trigger fires
///   when YOU cast a Merfolk spell — and Merrow Reejerey itself is a
///   Merfolk spell, so casting Reejerey also fires its own trigger
///   (CR 603.6d — abilities trigger on events from before the card's
///   ETB). The cast trigger condition matches by controller + Merfolk
///   subtype only; the active-zones filter on the trigger
///   (<see cref="ZoneType.Battlefield"/>) means the ability isn't active
///   yet when Reejerey is on the stack. This matches Reejerey's printed
///   shape (the trigger only watches battlefield-resident copies), and
///   leaves the strictly-correct self-cast fire as a follow-up.
/// </summary>
[CardName("Merrow Reejerey")]
public static class MerrowReejereyFactory
{
    public const string CardName = "Merrow Reejerey";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Merrow Reejerey with no live wiring. The cast-trigger is
    /// attached to the card shape; not registered (no trigger manager
    /// supplied). Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Merrow Reejerey with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the cast-trigger is
    /// registered so <see cref="SpellCastEvent"/>s published on the bus
    /// route through it.
    /// </summary>
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
            subtypes: new[] { CardSubtype.Merfolk, CardSubtype.Rogue });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.1.
        //   "Whenever you cast a Merfolk spell, choose one —
        //    Tap target permanent; or Untap target permanent."
        // "You cast" → the spell's controller is this card's controller.
        // Subtype gate: spell's card has the Merfolk subtype (CR 205.3m).
        // Active-zones gate is the default Battlefield set — Reejerey's
        // own cast does NOT self-fire (the trigger isn't active while
        // Reejerey is on the stack; see class xmldoc for the deferred
        // strictly-correct posture).
        // ----------------------------------------------------------------
        TriggeredAbility? castTrigger = null;

        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 603.1 — controller match for the printed "you cast".
            // Compare against the card's current controller at evaluation
            // time (mirrors the Sram / Monastery Mentor pattern).
            var liveController = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, liveController))
            {
                return false;
            }

            // CR 205.3m — the spell's card must have the Merfolk subtype.
            return e.Spell.Card.HasSubtype(CardSubtype.Merfolk);
        });

        var flipEffect = new Effect(
            $"{CardName} — choose one: tap or untap target permanent",
            () =>
            {
                if (castTrigger == null) return;
                var chosen = castTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution. Target must still be
                // on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;

                // Deterministic "useful flip" — modal pick collapsed (see
                // class xmldoc): untap a tapped target, tap an untapped
                // one. Matches Pestermite's posture for "tap or untap".
                if (target.IsTapped) target.Untap();
                else target.Tap();
            });

        castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { flipEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }
}
