using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ajani, Nacatl Pariah — DFC front face (Modern
/// Horizons 3, {1}{W}).
///
/// Creature — Cat 1/1, Vigilance.
/// Oracle text (front):
///   "Vigilance
///    At the beginning of your end step, you may sacrifice another
///    creature. If you do, transform Ajani, Nacatl Pariah."
///
/// Back face (Ajani, Nacatl Avenger): Legendary Planeswalker — Ajani,
/// loyalty 3. The back-face loyalty abilities are not modelled by this
/// factory — only the DFC plumbing (front face shape + transform trigger).
///
/// ## Implemented (v1)
/// - 1/1 Creature — Cat at {1}{W}, owner / controller set.
/// - <see cref="KeywordAbility"/> Vigilance marker (CR 702.20), consumed by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasVigilance"/>.
/// - End-step triggered ability (CR 500.4 / CR 603.1 + CR 701.16 sacrifice,
///   CR 701.28 transform): on the controller's End step, if the controller
///   controls another creature, sacrifice that creature and flip the
///   attached <see cref="MdfcState"/> to its back face. The "you may" is
///   auto-accepted when a sacrificable creature exists (mirrors every
///   other "you may" v1 deferral — see Sun Titan / Priest of Fell Rites).
/// - <see cref="MdfcState"/> exposed via <see cref="MdfcState"/> property on
///   the returned card so tests / runtime can observe the active face.
///   <see cref="Card.MdfcState"/> is the canonical attachment point for
///   DFC front/back tracking (CR 711).
///
/// ## Deferred (v1 gaps)
/// - <b>Back-face hot-swap on the battlefield.</b> The transform only flips
///   the MdfcState; the Creature object remains in place. A full DFC
///   characteristic-replacement (replace the Cat with a Planeswalker —
///   Ajani body, loyalty 3) would require Layer 0 / per-face cardpool
///   support that the engine does not yet have. The MdfcState flip is the
///   v1 observation surface — combat / loyalty interactions on the back
///   face are deferred.
/// - <b>Back-face loyalty abilities.</b> Nacatl Avenger's [+1] / [-2] /
///   [-X] loyalty abilities are not wired. The back face is shape-only
///   tracked through MdfcState.BackFaceName.
/// - <b>Two 1/1 Cat token rider (actual MH3 print).</b> The shipped MH3
///   card creates two 1/1 white Cat tokens when it transforms. v1 omits
///   that rider to keep the first DFC slice focused on the
///   transform-on-sacrifice plumbing.
/// - <b>"You may" prompt.</b> Auto-accepts the sacrifice when a candidate
///   exists (deterministic first other-creature pick scoped to the
///   controller's battlefield). A real agent-driven yes/no + target
///   prompt is deferred — same queue as Sun Titan / Stoneforge Mystic.
/// </summary>
public static class AjaniNacatlPariahFactory
{
    public const string FrontName = "Ajani, Nacatl Pariah";
    public const string BackName = "Ajani, Nacatl Avenger";
    public const string FrontCost = "{1}{W}";

    /// <summary>
    /// Construct Ajani, Nacatl Pariah with no live TriggerManager wiring
    /// (shape / dispatcher path). The Vigilance keyword and end-step
    /// transform trigger are attached to the card so structural assertions
    /// still see them; the trigger is not registered with a manager.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Ajani, Nacatl Pariah with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied the end-step
    /// transform trigger is registered so the runtime places it on the
    /// stack at the start of the controller's End step.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: FrontName,
            manaCost: FrontCost,
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Cat });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 — attach the DFC face-tracker so callers can observe the
        // active face. Starts on the front face (Ajani, Nacatl Pariah);
        // transform() flips IsBackFace.
        card.MdfcState = new MdfcState(FrontName, BackName);

        // CR 702.20 — Vigilance. KeywordAbility marker consumed by
        // CombatAbilities.HasVigilance / CombatValidator / Attacker.HasVigilance.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // CR 500.4 / CR 603.1 — "At the beginning of your end step, you may
        // sacrifice another creature. If you do, transform Ajani, Nacatl
        // Pariah." Triggers.OnStepBegin filters StepStartedEvent on (End,
        // controller) so it only fires on the controller's own end step.
        //
        // Resolution:
        //   1. Find another creature the controller controls (deterministic
        //      first other-creature pick). If none, no-op (CR 117 — "you may"
        //      with no valid candidate resolves as a no-op).
        //   2. Sacrifice the picked creature (CR 701.16) — move to its
        //      owner's graveyard via OracleSpellBinder.MoveToGraveyard.
        //   3. Transform Ajani by flipping the attached MdfcState
        //      (CR 701.28). The Creature object stays in place — full
        //      Layer 0 / per-face hot-swap is deferred.
        var transformEffect = new Effect(
            $"{FrontName}: end-step may-sacrifice-another → transform",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (card.MdfcState == null || card.MdfcState.IsBackFace) return;

                var controller = card.Controller ?? owner;
                var sacrificeTarget = controller.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .FirstOrDefault(c => !ReferenceEquals(c, card));
                if (sacrificeTarget == null) return;

                OracleSpellBinder.MoveToGraveyard(sacrificeTarget);
                card.MdfcState.Transform();
            });

        var endStepTransform = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, Majik.Core.StateMachine.PhaseStateType.End),
            effects: new IEffect[] { transformEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(endStepTransform);
        triggers?.RegisterTriggeredAbility(endStepTransform);

        return card;
    }
}
