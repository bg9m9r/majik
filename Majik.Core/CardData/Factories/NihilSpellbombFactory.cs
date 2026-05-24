using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nihil Spellbomb (Scars of Mirrodin / reprints).
///
/// Artifact — {B}. Oracle text:
///   "{T}, Sacrifice Nihil Spellbomb: Exile all cards from target player's graveyard.
///    When Nihil Spellbomb is put into a graveyard from the battlefield, you may pay
///    {B}. If you do, draw a card."
///
/// ## Implemented (v1)
/// - Artifact {B} with owner/controller wiring.
/// - <b>{T}, Sacrifice this: Exile all cards from target player's graveyard</b>:
///   cost is tap + self-sacrifice (Battlefield → Graveyard). Sacrifice is
///   performed by the effect closure (AdditionalCost.Pay is a stub — same
///   pattern as Aether Spellbomb / Mishra's Bauble). A 1..1 TargetRequest
///   for "target player" is declared. On resolution, v1 auto-picks the target
///   player from ChosenTargets[0][0] (falls back to controller); every card
///   in that player's graveyard is moved to their Exile zone.
/// - <b>Dies trigger — CR 603.6c</b>: "When Nihil Spellbomb is put into a
///   graveyard from the battlefield, you may pay {B}. If you do, draw a card."
///   Fires on a CardMovedEvent (Battlefield → Graveyard). v1 auto-pays {B}
///   from the controller's mana pool if available ("you may" defaults to
///   accepting when mana is available — same posture as Sneak Attack /
///   Tireless Tracker). If the pool can't cover {B}, no draw.
///   When <paramref name="triggers"/> is supplied, the trigger is
///   registered with TriggerManager; otherwise the trigger is attached
///   to the card shape only (suitable for unit tests invoking the effect
///   directly). The activeZones include Battlefield + Graveyard so the
///   trigger still matches after ZoneService stamps Zone = Graveyard
///   before publishing the event.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt for {B} payment</b>: v1 auto-accepts payment when
///   the mana pool has {B} (same posture as Sneak Attack / Tireless Tracker).
///   Real prompt deferred until IPlayerAgent grows a ChooseYesNoAsync surface.
/// - <b>Target player prompt</b>: v1 reads ChosenTargets[0][0] and falls
///   back to the controller. Full agent-prompt targeting deferred.
/// - <b>Sacrifice payment side effects</b>: same no-op stub as
///   Aether Spellbomb — the effect closure performs the zone move.
/// </summary>
public static class NihilSpellbombFactory
{
    public const string CardName = "Nihil Spellbomb";

    /// <summary>
    /// Construct Nihil Spellbomb. The dies trigger is attached to the card
    /// shape but not registered with a TriggerManager (suitable for shape
    /// and dispatcher tests). The activated ability operates on the controller's
    /// graveyard only when no target is provided.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Nihil Spellbomb with optional TriggerManager wiring. When
    /// <paramref name="triggers"/> is supplied, the dies trigger is registered
    /// so a Battlefield → Graveyard CardMovedEvent places it on the stack
    /// automatically.
    /// </summary>
    public static Artifact Create(
        Player owner,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var spellbomb = new Artifact(CardName, "{B}");
        spellbomb.SetOwner(owner);
        spellbomb.SetController(owner);

        // ----------------------------------------------------------------
        // Activated ability: {T}, Sacrifice Nihil Spellbomb:
        //   Exile all cards from target player's graveyard.
        //
        // CR 605 — not a mana ability (exile effect, goes on stack).
        // Cost: tap + self-sacrifice (Battlefield → Graveyard).
        // Target: 1..1 TargetRequest "target player".
        // On resolve: iterate target player's graveyard, move each card
        // to that player's Exile. Sacrifice is performed by the effect
        // closure (AdditionalCost.Pay is a stub — same as Aether Spellbomb).
        // ----------------------------------------------------------------
        ActivatedAbility? exileAbility = null;
        var exileEffect = new Effect(
            "Nihil Spellbomb: exile all cards from target player's graveyard + sac self",
            () =>
            {
                // Sacrifice payment stub: move spellbomb Battlefield → Graveyard.
                SacrificeSelf(spellbomb, owner);

                // Resolve target player from ChosenTargets; fall back to
                // controller (v1 deterministic path).
                Player? targetPlayer = null;
                if (exileAbility != null
                    && exileAbility.ChosenTargets.Count > 0
                    && exileAbility.ChosenTargets[0].Count > 0
                    && exileAbility.ChosenTargets[0][0] is Player chosenPlayer)
                {
                    targetPlayer = chosenPlayer;
                }
                else
                {
                    targetPlayer = owner;
                }

                var graveyardCards = targetPlayer.Zones.Graveyard.GetCards().ToList();
                foreach (var card in graveyardCards)
                {
                    targetPlayer.Zones.Graveyard.RemoveCard(card);
                    targetPlayer.Zones.Exile.AddCard(card);
                    card.SetZone(ZoneType.Exile);
                }
            });

        exileAbility = new ActivatedAbility(
            source: spellbomb,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(spellbomb),
                AdditionalCost.Sacrifice(spellbomb),
            },
            effects: new IEffect[] { exileEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        spellbomb.AddAbility(exileAbility);

        // ----------------------------------------------------------------
        // Dies trigger — CR 603.6c.
        //   "When Nihil Spellbomb is put into a graveyard from the
        //    battlefield, you may pay {B}. If you do, draw a card."
        //
        // Fires on a Battlefield → Graveyard CardMovedEvent matching
        // this specific card. v1 auto-pays {B} when the controller's
        // mana pool can cover it; draws one card on success.
        // activeZones includes both Battlefield and Graveyard so the
        // trigger is still evaluated after ZoneService stamps the card's
        // zone to Graveyard before publishing (mirrors WurmcoilEngine /
        // Undying pattern).
        // ----------------------------------------------------------------
        var diesEffect = new Effect(
            "Nihil Spellbomb: may pay {B} to draw a card",
            () =>
            {
                // "You may pay {B}. If you do, draw a card."
                // v1 auto-accepts when the pool has the mana.
                var cost = ManaCost.Parse("{B}");
                if (!owner.ManaPool.CanPay(cost)) return;

                owner.PayMana(cost);

                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    owner.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var diesTrigger = new TriggeredAbility(
            source: spellbomb,
            controller: owner,
            condition: Triggers.OnDies(spellbomb),
            effects: new IEffect[] { diesEffect },
            // activeZones: Battlefield + Graveyard so the trigger still
            // matches after ZoneService stamps Zone = Graveyard before
            // publishing (mirrors Wurmcoil Engine / Undying pattern).
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        spellbomb.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return spellbomb;
    }

    /// <summary>
    /// Move <paramref name="spellbomb"/> from the battlefield to its owner's
    /// graveyard. Idempotent — no-op if the card is already off the battlefield.
    /// </summary>
    private static void SacrificeSelf(Artifact spellbomb, Player owner)
    {
        if (spellbomb.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(spellbomb);
        owner.Zones.Graveyard.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Graveyard);
    }
}
