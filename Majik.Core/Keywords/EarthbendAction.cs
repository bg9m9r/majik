using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// Earthbend N (Avatar: The Last Airbender).
///
/// Full rules text (keyword reminder):
///   1. Target land you control becomes a 0/0 creature with haste that's
///      still a land (no creature subtype).
///   2. Put N +1/+1 counters on it — so Earthbend N → an N/N.
///   3. When that land dies or is exiled, return it to the battlefield
///      tapped under its owner's control.
///
/// The animate-land half (step 1) is a proper CR 613 continuous effect via
/// <see cref="AnimateLandEffect"/> when a <see cref="ContinuousEffectsService"/>
/// is supplied: Layer 4 adds <see cref="CardType.Creature"/> (the printed Land
/// type stays — "still a land"; no creature subtype is granted),
/// Layer 7b sets base P/T 0/0, Layer 6 grants Haste. The service's
/// creature-row upgrade (a Layer-4 Creature grant on a non-creature permanent)
/// makes the 0/0 base + the +1/+1 counters surface as an N/N through
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> and therefore
/// combat math. This is the manland mechanism (Creeping Tar Pit class).
///
/// CR 701.59 attaches no duration to the type/P/T change, so the animation
/// persists while the land is on the battlefield (the effects self-terminate
/// on leave-the-battlefield via <see cref="ContinuousEffect.IsActive"/>).
///
/// Target selection: when an explicit <c>target</c> is passed it is used
/// (the unified <c>TargetRequest</c>/<c>ChooseAsync</c> path picks it
/// upstream — "target land you control"); otherwise this auto-picks the
/// first land the controller controls (legacy convenience for direct callers
/// / tests).
/// </summary>
public static class EarthbendAction
{
    /// <summary>
    /// Apply Earthbend N for <paramref name="controller"/>, auto-targeting the
    /// first land the controller controls. The animate-land continuous effect
    /// is registered against <paramref name="effects"/> when supplied (so P/T
    /// surfaces through Compute); when null only the +1/+1 counters and the
    /// return-tapped trigger are applied.
    ///
    /// Returns the targeted land, or <c>null</c> if <paramref name="n"/> &lt;= 0
    /// or the controller has no lands on the battlefield.
    /// </summary>
    public static Permanent? Apply(Player controller, int n, ContinuousEffectsService? effects = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        if (n <= 0) return null;

        // "target land you control" = any permanent whose CURRENT card types
        // include Land — a plain Land, an animated land, OR a Land Creature
        // built as a Creature C# instance (Dryad Arbor). Filter on the land
        // TYPE, not the Land C# class (CR 701.59 / 115.4).
        var land = controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault(p => p.HasType(CardType.Land));
        if (land == null) return null;

        Apply(land, controller, n, effects);
        return land;
    }

    /// <summary>
    /// Apply Earthbend N to an explicit <paramref name="land"/> (the unified
    /// targeting pipeline's chosen "target land you control"). No-op when
    /// <paramref name="n"/> &lt;= 0. The target is typed as a
    /// <see cref="Permanent"/> rather than the <see cref="Land"/> C# class so a
    /// Land Creature (Dryad Arbor — a <see cref="Creature"/> instance that is
    /// also a land) is a legal Earthbend target, per CR 701.59 ("target land
    /// you control" is any permanent whose types include Land).
    /// </summary>
    public static void Apply(Permanent land, Player controller, int n, ContinuousEffectsService? effects = null)
    {
        if (land == null) throw new ArgumentNullException(nameof(land));
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        if (n <= 0) return;

        // Step 1 — animate: 0/0 Elemental creature with haste, still a land
        // (CR 701.59a). Modelled as a CR 613 continuous effect so the type /
        // P/T / haste surface through Compute and combat. When no service is
        // wired (shape-only direct callers), the effect is skipped — the
        // counters + return trigger still apply.
        var ces = effects ?? land.ActiveEffects;
        if (ces != null)
        {
            // Ensure the land consults this service for its computed
            // characteristics (it may not have been wired — lands skip the
            // creature ActiveEffects hookup in the prod binder).
            land.ActiveEffects ??= ces;
            // CR — Earthbend grants the Creature type only, with NO creature
            // subtype ("becomes a 0/0 creature with haste that's still a
            // land"). subtype: null adds Creature but no Elemental/etc.
            AnimateLandEffect.Register(
                ces, land,
                subtype: null,
                basePower: 0,
                baseToughness: 0,
                grantsHaste: true,
                expiresAtEndOfTurn: false);
        }

        // Step 2 — put N +1/+1 counters on it (CR 701.59b). With base P/T 0/0
        // and N counters, the Layer-7c postlude surfaces N/N (CR 613.3).
        land.Counters.Add(CounterType.PlusOnePlusOne, n);

        // Step 3 — delayed triggered ability: "When [land] dies or is exiled,
        // return it to the battlefield tapped under its owner's control."
        // This is a ONE-SHOT delayed trigger (CR 603.7a): it fires exactly
        // once, the next time THIS animated land dies or is exiled, then is
        // gone. A returned land is a fresh permanent — plain (the animate
        // effect self-prunes when the land leaves the battlefield), with no
        // standing return trigger. Modelled as a DelayedTriggeredAbility so
        // the TriggerManager auto-unregisters it after it fires.
        var owner = land.Owner ?? controller;

        DelayedTriggeredAbility? returnTrigger = null;
        var returnEffect = new Effect("Earthbend — return to battlefield tapped", () =>
        {
            // Detach the one-shot from the land's ability list so a later
            // zone change of the (now plain) returned land can't re-register
            // and re-fire it via SyncCardRegistration. TriggerManager already
            // dropped it from its own set when it fired (CR 603.7a).
            if (returnTrigger != null) land.RemoveAbility(returnTrigger);

            if (land.Zone == ZoneType.Graveyard)
            {
                owner.Zones.Graveyard.RemoveCard(land);
            }
            else if (land.Zone == ZoneType.Exile)
            {
                owner.Zones.Exile.RemoveCard(land);
            }
            else
            {
                return;
            }

            owner.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
            land.SetController(owner);
            land.MarkEnteredBattlefield();
            land.Tap(); // returns tapped.
        });

        returnTrigger = new DelayedTriggeredAbility(
            land,
            owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                ReferenceEquals(e.Card, land)
                && e.FromZone == ZoneType.Battlefield
                && (e.ToZone == ZoneType.Graveyard || e.ToZone == ZoneType.Exile)),
            effects: new IEffect[] { returnEffect });

        land.AddAbility(returnTrigger);
    }
}
