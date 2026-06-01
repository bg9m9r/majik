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
/// CR 701.59 — Earthbend N (Bloomburrow).
///
/// Full rules text:
///   1. Target land you control becomes a 0/0 Elemental creature with haste
///      that's still a land (CR 701.59a).
///   2. Put N +1/+1 counters on it (CR 701.59b) — so Earthbend N → an N/N.
///   3. When that land dies or is exiled, return it to the battlefield
///      tapped under its owner's control (CR 701.59c).
///
/// The animate-land half (step 1) is a proper CR 613 continuous effect via
/// <see cref="AnimateLandEffect"/> when a <see cref="ContinuousEffectsService"/>
/// is supplied: Layer 4 adds <see cref="CardType.Creature"/> +
/// <see cref="CardSubtype.Elemental"/> (the printed Land type stays — "still a
/// land"), Layer 7b sets base P/T 0/0, Layer 6 grants Haste. The service's
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
    public static Land? Apply(Player controller, int n, ContinuousEffectsService? effects = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        if (n <= 0) return null;

        var land = controller.Zones.Battlefield.GetCards()
            .OfType<Land>()
            .FirstOrDefault();
        if (land == null) return null;

        Apply(land, controller, n, effects);
        return land;
    }

    /// <summary>
    /// Apply Earthbend N to an explicit <paramref name="land"/> (the unified
    /// targeting pipeline's chosen "target land you control"). No-op when
    /// <paramref name="n"/> &lt;= 0.
    /// </summary>
    public static void Apply(Land land, Player controller, int n, ContinuousEffectsService? effects = null)
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
            AnimateLandEffect.Register(
                ces, land,
                subtype: CardSubtype.Elemental,
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
        // (CR 701.59c / CR 603.6a). activeZones includes Graveyard and Exile
        // so the trigger stays registered after the zone change.
        var owner = land.Owner ?? controller;

        var returnEffect = new Effect("Earthbend — return to battlefield tapped", () =>
        {
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
            land.Tap(); // CR 701.59c — returns tapped.
        });

        var returnTrigger = new TriggeredAbility(
            land,
            owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                ReferenceEquals(e.Card, land)
                && e.FromZone == ZoneType.Battlefield
                && (e.ToZone == ZoneType.Graveyard || e.ToZone == ZoneType.Exile)),
            effects: new IEffect[] { returnEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard, ZoneType.Exile });

        land.AddAbility(returnTrigger);
    }
}
