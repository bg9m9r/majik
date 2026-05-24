using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sunscorched Desert (Hour of Devastation).
///
/// Land. Oracle text:
///   "Sunscorched Desert enters tapped.
///    When this land enters, it deals 1 damage to any target.
///    {T}: Add {C}."
///
/// ## Implemented (v1)
/// - <b>Land</b> with no printed subtype — Sunscorched Desert is just
///   "Land" (no Desert subtype on the Hour of Devastation printing; the
///   subtype was added on later Desert cycles but never to this card).
/// - <b>Enters-tapped replacement (CR 614.1c)</b> — unconditional
///   "this permanent enters tapped." Registered via
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>, mirroring Geralf's Messenger's
///   unconditional ETB-tapped wiring. The single-arg dispatcher path
///   omits the replacement when no <see cref="ReplacementBus"/> is
///   available — the Desert enters untapped on shape-only paths,
///   matching every other always-tapped factory's posture
///   (Creeping Tar Pit / Valakut / Geralf's Messenger).
/// - <b>ETB triggered ability (CR 603.6a)</b> — "When this land
///   enters, it deals 1 damage to any target." Wired as a self-ETB
///   <see cref="TriggeredAbility"/> via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with a 1..1
///   "any target" <see cref="TargetRequest"/>. On resolution deals 1
///   damage to the chosen target via
///   <see cref="SearingBlazeFactory.DealDamageWithPlaneswalker"/>
///   (Player → <see cref="Player.LoseLife"/>; Creature →
///   <see cref="Creature.TakeDamage"/>; Planeswalker → loyalty removal
///   per CR 306.7) — same Phlage-shape damage dispatch as Valakut's
///   3-damage trigger but at amount 1. CR 608.2b — no chosen target /
///   illegal target at resolution → clean no-op.
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> wired
///   (CR 605.1 — mana abilities don't use the stack; {C} folds into
///   the generic bucket per <see cref="ManaCost.Parse"/>).
///
/// ## Lifecycle
/// The single-arg <see cref="Create(Player)"/> overload attaches the
/// ETB trigger + mana ability for shape inspection. The
/// <see cref="Create(Player, IEventBus?, TriggerManager?, ReplacementBus?)"/>
/// overload wires the ETB trigger against the
/// <see cref="TriggerManager"/> for bus-driven firing AND registers
/// the enters-tapped replacement on the <see cref="ReplacementBus"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>"Any target" agent prompt</b> — v1 honours pre-supplied
///   targets via <see cref="TriggeredAbility.SetChosenTargets"/>; no
///   chosen target → the damage effect no-ops (mirrors Valakut /
///   Earthshaker Khenra / Phlage).
/// </summary>
[CardName("Sunscorched Desert")]
public static class SunscorchedDesertFactory
{
    public const string CardName = "Sunscorched Desert";
    public const int DamageAmount = 1;

    /// <summary>
    /// Construct Sunscorched Desert with no live wiring. The ETB
    /// trigger is attached for shape inspection (not registered with
    /// a <see cref="TriggerManager"/>); the enters-tapped replacement
    /// is omitted (no <see cref="ReplacementBus"/> available). The
    /// Desert enters untapped on this path.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, replacements: null);

    /// <summary>
    /// Construct Sunscorched Desert. When <paramref name="triggers"/>
    /// is supplied the ETB trigger is registered so bus events
    /// auto-queue it. When <paramref name="replacements"/> is supplied
    /// the enters-tapped restriction is registered so the Desert
    /// enters tapped (CR 614.1c).
    /// </summary>
    public static Land Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Sunscorched Desert is just "Land" — no Desert subtype on the
        // Hour of Devastation printing (the later Desert cycles added
        // the subtype but never to this card).
        var card = new Land(CardName);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Enters-tapped replacement — CR 614.1c.
        //   "Sunscorched Desert enters tapped."
        // Unconditional; no gate (contrast Valakut's 5-mountain check).
        // Shape-only path (no ReplacementBus) skips registration and
        // the Desert enters untapped, matching every other always-tapped
        // factory's posture (Creeping Tar Pit / Geralf's Messenger).
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(card));
        }

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this land enters, it deals 1 damage to any target."
        // Single 1..1 "any target" TargetRequest; on resolution the
        // chosen target takes 1 damage via SearingBlazeFactory's
        // Player / Creature / Planeswalker dispatcher (loyalty removal
        // per CR 306.7). Mirrors Valakut's 3-damage ETB shape at 1.
        // CR 608.2b — no target chosen → clean no-op.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var damageEffect = new Effect(
            $"{CardName}: deal {DamageAmount} damage to any target",
            () =>
            {
                if (etbTrigger == null) return;
                if (etbTrigger.ChosenTargets.Count == 0) return;
                if (etbTrigger.ChosenTargets[0].Count == 0) return;

                var target = etbTrigger.ChosenTargets[0][0];
                SearingBlazeFactory.DealDamageWithPlaneswalker(target, DamageAmount);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities don't use the stack. {C} folds into
        // the generic bucket per ManaCost.Parse.
        // ----------------------------------------------------------------
        card.AddAbility(new ManaAbility(card, owner, ManaCost.Parse("C")));

        return card;
    }
}
