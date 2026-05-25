using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

// NOTE: ProtectionAbility is referenced in xmldoc only — the structural
// ETB effect is a no-op pending a player-scoped protection layer.

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for The One Ring (Tales of Middle-earth, {4}).
///
/// Legendary Artifact — {4}. Oracle text:
///   "Indestructible."
///   "When The One Ring enters, if you cast it, you gain protection from
///    everything until your next turn."
///   "At the beginning of your upkeep, you lose 1 life for each burden
///    counter on The One Ring."
///   "{T}: Put a burden counter on The One Ring, then draw a card for
///    each burden counter on The One Ring."
///
/// ## Implemented (v1)
/// - Legendary Artifact with mana cost {4}, owner/controller wired.
/// - <see cref="KeywordAbility"/>("Indestructible") marker so SBA 704.5g
///   skips destruction (read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasIndestructible"/> +
///   <see cref="Majik.Core.Rules.Sba.Checks.CreatureDeathCheck"/>).
/// - <b>ETB trigger</b> (CR 603.1, CR 113.5): "When The One Ring enters,
///   if you cast it, you gain protection from everything until your next
///   turn." Wired as a self-ETB <see cref="TriggeredAbility"/>. The "if
///   you cast it" intervening-if clause now gates on the persistent
///   <see cref="Majik.Core.Cards.Card.WasCast"/> stamp written by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> at stack push, so the
///   effect body short-circuits when the Ring entered the battlefield
///   via Show and Tell / reanimation / blink / etc. The "until your
///   next turn" expiry and the player-scoped "protection from
///   everything" grant remain deferred (no per-player delayed cleanup,
///   no Player.AddAbility surface and no player-scoped protection layer
///   in the damage / targeting / blocking pipelines yet); the effect
///   body is still a no-op on the protection side, but the cast gate
///   itself is now load-bearing for any future caller that wires the
///   protection layer in.
/// - <b>Upkeep trigger</b> (CR 500.4 / CR 603.1): "At the beginning of
///   your upkeep, you lose 1 life for each burden counter on The One
///   Ring." Wired via <see cref="Triggers.OnStepBegin"/> filtered to
///   <see cref="Majik.Core.StateMachine.PhaseStateType.Upkeep"/> and the
///   controller. Resolution reads the live burden count off the
///   permanent's <see cref="Permanent.Counters"/> bag and calls
///   <see cref="Player.LoseLife"/>.
/// - <b>Activated {T}</b> (CR 602.1): "Put a burden counter on The One
///   Ring, then draw a card for each burden counter on The One Ring."
///   Add-then-draw — the draw count includes the just-added counter, so
///   the first activation draws 1, the second draws 2, etc. The "then"
///   is sequenced inside the single effect closure (CR 608.2c — order
///   matters: add first, then sample the new count for the draw).
///   Empty library flags
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> per CR 704.5b
///   for each draw attempt that fails.
///
/// ## Printed-vs-task oracle reconciliation
/// The task brief lists two ability variants: the "poison counter on
/// upkeep with ≥4 burdens" Initiative-style text, and the actual
/// LotR-set oracle text (life-loss + tap-draw). The factory implements
/// the actual oracle — poison-counter accrual is out of scope.
///
/// ## Deferred (v1 gaps)
/// - <b>"Until your next turn" expiry</b>: no per-player delayed
///   cleanup primitive yet.
/// - <b>"Protection from everything" semantics</b>: ProtectionAbility
///   markers are scoped to <see cref="ICard"/>; there is no
///   player-scoped protection surface that the damage / targeting /
///   blocking pipelines consult. Effect body is structural-only.
/// - <b>Live TriggerManager wiring</b>: the single-arg factory attaches
///   the upkeep + ETB triggers to the card but does NOT register them
///   with a <see cref="TriggerManager"/>. Tests fire the trigger
///   manually or invoke the effect directly. The (owner, eventBus,
///   triggers) overload registers both triggers so bus-driven firing
///   works end-to-end.
/// </summary>
[CardName("The One Ring")]
public static class TheOneRingFactory
{
    public const string CardName = "The One Ring";
    public const string PrintedManaCost = "{4}";

    /// <summary>
    /// Construct The One Ring with no live bus / trigger-manager wiring.
    /// Triggers are attached for shape inspection; tests fire them by
    /// invoking the effects directly. Suitable for dispatcher / shape
    /// tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct The One Ring with optional event bus + trigger manager.
    /// When <paramref name="triggers"/> is supplied, the upkeep + ETB
    /// triggers are registered so the bus surfaces them automatically.
    /// </summary>
    public static Artifact Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var ring = new Artifact(
            name: CardName,
            manaCost: PrintedManaCost,
            supertypes: new[] { CardSupertype.Legendary });

        ring.SetOwner(owner);
        ring.SetController(owner);

        // ----------------------------------------------------------------
        // Indestructible (CR 702.12). Marker only — SBA reads
        // KeywordAbility via CombatAbilities.HasIndestructible.
        // ----------------------------------------------------------------
        ring.AddAbility(new KeywordAbility("Indestructible", ring, owner));

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.1, CR 113.5.
        //   "When The One Ring enters, if you cast it, you gain protection
        //    from everything until your next turn."
        // The "if you cast it" intervening-if clause now gates on the
        // persistent <see cref="Card.WasCast"/> stamp (SpellCastFlow
        // writes it at stack push; ZoneService clears it on LTB). The
        // "until your next turn" expiry and the player-scoped
        // protection grant remain deferred — see class xmldoc.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            "The One Ring: grant controller protection from everything (gated on WasCast)",
            () =>
            {
                // CR 113.5 — intervening-if check. Skip when the Ring
                // arrived on the battlefield via a non-cast path
                // (Show and Tell, reanimation, blink, etc.).
                if (!ring.WasCast) return;

                // Structural-only — no player-scoped protection surface
                // exists yet. The cast gate is now real; the protection
                // grant is still a no-op until the player-protection
                // layer ships.
                _ = ring.Controller ?? owner;
            });

        var etbTrigger = new TriggeredAbility(
            source: ring,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(ring),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        ring.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Upkeep trigger — CR 603.1 / CR 500.4.
        //   "At the beginning of your upkeep, you lose 1 life for each
        //    burden counter on The One Ring."
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            "The One Ring: lose 1 life per burden counter",
            () =>
            {
                var controller = ring.Controller ?? owner;
                var burdens = ring.Counters.Count(CounterType.Burden);
                if (burdens > 0)
                {
                    controller.LoseLife(burdens);
                }
            });

        var upkeepTrigger = new TriggeredAbility(
            source: ring,
            controller: owner,
            condition: Triggers.OnStepBegin(
                owner, Majik.Core.StateMachine.PhaseStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        ring.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        // ----------------------------------------------------------------
        // Activated {T} — CR 602.1.
        //   "{T}: Put a burden counter on The One Ring, then draw a card
        //    for each burden counter on The One Ring."
        // Order matters (CR 608.2c): add the counter first, then sample
        // the (new) burden count for the draw. So:
        //   - 1st tap: 0 → 1 burden, draw 1
        //   - 2nd tap: 1 → 2 burdens, draw 2
        //   - 3rd tap: 2 → 3 burdens, draw 3 …
        // ----------------------------------------------------------------
        var tapEffect = new Effect(
            "The One Ring: add a burden counter, draw N",
            () =>
            {
                ring.Counters.Add(CounterType.Burden);

                var controller = ring.Controller ?? owner;
                var burdens = ring.Counters.Count(CounterType.Burden);
                for (var i = 0; i < burdens; i++)
                {
                    var top = controller.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        controller.MarkTriedToDrawFromEmptyLibrary();
                        continue;
                    }
                    controller.Zones.Library.RemoveCard(top);
                    controller.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            });

        var tapAbility = new ActivatedAbility(
            source: ring,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(ring) },
            effects: new IEffect[] { tapEffect });

        ring.AddAbility(tapAbility);

        return ring;
    }
}
