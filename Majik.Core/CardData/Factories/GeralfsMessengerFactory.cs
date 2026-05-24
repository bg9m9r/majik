using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Geralf's Messenger (Dark Ascension, {B}{B}{B}).
///
/// Creature — Zombie 3/2. Oracle text:
///   "Geralf's Messenger enters tapped.
///    When Geralf's Messenger enters, target opponent loses 2 life.
///    Undying (When this creature dies, if it had no +1/+1 counters on it,
///    return it to the battlefield under its owner's control with a +1/+1
///    counter on it.)"
///
/// ## Implemented (v1)
/// - 3/2 Creature — Zombie, mana cost {B}{B}{B}.
/// - <b>Enters-tapped replacement (CR 614.1c)</b> — unconditional "this
///   permanent enters tapped." Registered via
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. CR 614.1c — the same replacement still
///   applies on each entry, so an Undying return enters tapped again
///   (matches paper). The single-arg dispatcher path omits the replacement
///   when no <see cref="ReplacementBus"/> is available — Messenger enters
///   untapped on shape-only paths, mirroring how Creeping Tar Pit / Valakut
///   defer the restriction to the binder layer for shape-only construction.
/// - <b>ETB triggered ability (CR 603.6a + CR 119.3)</b> — "When Geralf's
///   Messenger enters, target opponent loses 2 life." Single 1..1 "target
///   opponent" <see cref="TargetRequest"/>; on resolve the chosen player
///   loses 2 life via <see cref="Player.LoseLife"/>. Mirrors
///   <see cref="HidetsugusSecondRiteFactory.BuildSpellDefinition"/>'s
///   target-opponent shape. CR 608.2b — no chosen target (or non-Player
///   slot) → clean no-op.
/// - <b>Undying (CR 702.93)</b> — keyword marker wired via
///   <see cref="KeywordAbility"/> for shape inspection plus the return-with-
///   +1/+1-counter mechanic via the shared <see cref="UndyingFactory.Build"/>
///   helper (same wiring Young Wolf / Strangleroot Geist would use). The
///   intervening-if (CR 603.4) guarantees a second death after the Undying
///   return doesn't trigger again.
///
/// ## Lifecycle
/// The single-arg <see cref="Create(Player)"/> overload attaches the ETB
/// trigger + Undying keyword for shape inspection. The
/// <see cref="Create(Player, IEventBus?, TriggerManager?, ReplacementBus?)"/>
/// overload wires the ETB trigger + Undying trigger against the
/// <see cref="TriggerManager"/> for bus-driven firing AND registers the
/// enters-tapped replacement on the <see cref="ReplacementBus"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>"Target opponent" agent prompt</b> — v1 honours pre-supplied targets
///   via <see cref="TriggeredAbility.SetChosenTargets"/>; no chosen target
///   → the life-loss effect no-ops (mirrors Valakut / Earthshaker Khenra).
/// </summary>
public static class GeralfsMessengerFactory
{
    public const string CardName = "Geralf's Messenger";
    public const string PrintedManaCost = "{B}{B}{B}";
    public const int LifeLossAmount = 2;

    /// <summary>
    /// Construct Geralf's Messenger with no live wiring. The ETB trigger
    /// and Undying ability are attached for shape inspection (not registered
    /// with a <see cref="TriggerManager"/>); the enters-tapped replacement
    /// is omitted (no <see cref="ReplacementBus"/> available). Messenger
    /// enters untapped on this path.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, replacements: null);

    /// <summary>
    /// Construct Geralf's Messenger. When <paramref name="triggers"/> is
    /// supplied the ETB and Undying abilities are registered so bus events
    /// auto-queue them. When <paramref name="replacements"/> is supplied the
    /// enters-tapped restriction is registered so Messenger enters tapped on
    /// each ETB (initial cast + Undying return both honour CR 614.1c).
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 3,
            toughness: 2,
            subtypes: new[] { CardSubtype.Zombie });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Enters-tapped replacement — CR 614.1c.
        //   "Geralf's Messenger enters tapped."
        // Unconditional; no gate (contrast Valakut's 5-mountain check or
        // shock-land's life payment option). The same replacement applies
        // to every ETB — including Undying returns — so each entry honours
        // the tap. Shape-only path (no ReplacementBus) skips registration
        // and Messenger enters untapped, matching every other always-tapped
        // factory's posture (Creeping Tar Pit etc).
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(card));
        }

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a + CR 119.3.
        //   "When Geralf's Messenger enters, target opponent loses 2 life."
        // Single 1..1 "target opponent" TargetRequest; on resolution the
        // chosen player loses 2 life. Mirrors Hidetsugu's Second Rite's
        // target-opponent shape and Earthshaker Khenra's chosen-target
        // gating (CR 608.2b — no/illegal target = clean no-op).
        // Fires on every ETB, including Undying returns.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var drainEffect = new Effect(
            $"{CardName}: target opponent loses {LifeLossAmount} life",
            () =>
            {
                if (etbTrigger == null) return;
                if (etbTrigger.ChosenTargets.Count == 0) return;
                if (etbTrigger.ChosenTargets[0].Count == 0) return;

                // CR 608.2b — illegal slot type at resolution → clean no-op.
                if (etbTrigger.ChosenTargets[0][0] is not Player target) return;

                target.LoseLife(LifeLossAmount);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { drainEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Undying — CR 702.93.
        //   "When this creature dies, if it had no +1/+1 counters on it,
        //    return it to the battlefield under its owner's control with a
        //    +1/+1 counter on it."
        // Keyword marker wired via KeywordAbility (for shape inspection /
        // KeywordAnalyzer parity); the mechanic itself is wired through the
        // shared UndyingFactory.Build helper (same wiring Young Wolf and
        // Strangleroot Geist would use).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Undying", card, owner));

        var undyingTrigger = UndyingFactory.Build(card);
        card.AddAbility(undyingTrigger);
        triggers?.RegisterTriggeredAbility(undyingTrigger);

        return card;
    }
}
