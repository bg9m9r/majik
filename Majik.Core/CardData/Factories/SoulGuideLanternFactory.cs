using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Soul-Guide Lantern (Ikoria / reprints — {1}).
///
/// Artifact. Oracle text:
///   "When this artifact enters, exile target card from a graveyard.
///    {T}, Sacrifice this artifact: Exile each opponent's graveyard.
///    {1}, {T}, Sacrifice this artifact: Draw a card."
///
/// ## Implemented (v1)
/// - Artifact {1} with owner/controller wiring (mirrors
///   <see cref="RelicOfProgenitusFactory"/> / <see cref="TormodsCryptFactory"/>
///   for the cheap graveyard-hate artifact shape).
/// - <b>ETB triggered ability — "When this artifact enters, exile target
///   card from a graveyard."</b> Wired as a <see cref="TriggeredAbility"/>
///   over <see cref="Triggers.OnEnterBattlefieldSelf"/> + a 1..1
///   "target card in a graveyard" <see cref="TargetRequest"/>. Resolution
///   reads <c>ChosenTargets[0][0]</c>, rechecks legality (CR 608.2b — the
///   target card must still be in a graveyard), then routes the move
///   through the target's owner's Graveyard → Exile (mirrors Cling to
///   Dust's exile branch).
/// - <b>{T}, Sacrifice: Exile each opponent's graveyard.</b> Wired as an
///   <see cref="ActivatedAbility"/> with <see cref="AdditionalCost.Tap"/>
///   + <see cref="AdditionalCost.Sacrifice"/>. The self-sac zone move is
///   performed by the effect closure because the generic Sacrifice cost
///   path is a no-op stub today (same posture as Tormod's Crypt / Relic of
///   Progenitus). Resolution reads the opponents from the LIVE game
///   (<c>rc.Game.AllPlayers</c>, filtered to non-controller) at resolution —
///   NOT a captured resolver — and moves every card in each opponent's
///   graveyard to that opponent's exile zone. Without a live game context
///   (shape-only path) the sweep is a safe no-op.
/// - <b>{1}, {T}, Sacrifice: Draw a card.</b> Wired as a second
///   <see cref="ActivatedAbility"/> with <see cref="ManaCostCost"/>{1} +
///   <see cref="AdditionalCost.Tap"/> + <see cref="AdditionalCost.Sacrifice"/>.
///   The self-sac zone move is also in-closure (same rationale). Empty
///   library is a silent no-op (SBAs handle the loss condition).
///
/// ## Deferred (v1 gaps)
/// - <b>Self-sacrifice additional cost</b>: <see cref="AdditionalCost.Pay"/>'s
///   sacrifice path is a TODO stub today (same gap shared by every
///   sac-self artifact in the cycle). Closures perform the zone move.
/// - <b>Choose-time target filtering</b>: ETB
///   <see cref="TargetRequest.LegalCandidates"/> is empty by default —
///   the agent picks any object; resolve-time legality is the live
///   gate. Choose-time filtering depends on the deferred graveyard
///   gather plumbing (Cling to Dust shares the same posture).
/// - <b>Agent-driven ETB target prompt</b>: trigger honours pre-set
///   <see cref="TriggeredAbility.ChosenTargets"/>; the factory does NOT
///   wire an <see cref="IPlayerAgent"/> prompt.
/// </summary>
[CardName("Soul-Guide Lantern")]
public static class SoulGuideLanternFactory
{
    public const string CardName = "Soul-Guide Lantern";
    public const string PrintedManaCost = "{1}";

    /// <summary>
    /// Construct Soul-Guide Lantern. The {T}, Sacrifice graveyard-sweep ability
    /// reads its opponents from the LIVE resolution context at resolution time
    /// (a safe no-op when resolved without a live game). This is the overload
    /// <see cref="NamedCardFactory"/> / the routed prod build dispatches to.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Soul-Guide Lantern with optional <see cref="TriggerManager"/>
    /// registration for the ETB trigger. When <paramref name="triggers"/> is
    /// supplied the ETB trigger is registered for bus-driven firing.
    ///
    /// The {T}, Sacrifice graveyard-sweep ability reads its opponents from the
    /// LIVE game at RESOLUTION (<c>rc.Game.AllPlayers</c>, filtered to
    /// non-controller) — NOT a captured resolver. Previously the sweep captured
    /// a <c>Func&lt;IReadOnlyList&lt;Player&gt;&gt; opponentsResolver</c> at
    /// build time; the production routed build
    /// (<c>GameFacade.BuildDeckCard → NamedCardFactory.Create(name, owner,
    /// effects)</c>) dispatches the single-arg shape build, which left that
    /// resolver null, so the sweep exiled ZERO graveyards in real games (only
    /// the resolver-injecting factory-direct tests saw it run). Reading the live
    /// context fixes the routed build (mirrors Stormbreath #2540 / Yawgmoth +
    /// Priest #2543).
    /// </summary>
    public static Artifact Create(
        Player owner,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var lantern = new Artifact(CardName, PrintedManaCost);
        lantern.SetOwner(owner);
        lantern.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — "When this artifact enters, exile target card
        // from a graveyard."
        // CR 603.6c — ETB trigger that goes on the stack with the printed
        // target. Resolution recheck: the chosen card must still be in a
        // graveyard (CR 608.2b). Mirrors Cling to Dust's exile branch.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbEffect = new Effect(
            $"{CardName}: exile target card from a graveyard",
            () => ResolveEtbExileTarget(etbTrigger));

        etbTrigger = new TriggeredAbility(
            source: lantern,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(lantern),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target card in a graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        lantern.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {T}, Sacrifice this artifact: Exile each opponent's graveyard.
        // CR 605 — not a mana ability; goes on the stack.
        // Cost: tap + self-sac (closure performs the sacrifice zone move).
        // ----------------------------------------------------------------
        var sweepEffect = new Effect(
            $"{CardName}: exile each opponent's graveyard",
            rc =>
            {
                SacrificeSelf(lantern, owner);
                // Read opponents from the LIVE game at resolution
                // (rc.Game.AllPlayers, filtered to non-controller) — NOT a
                // captured resolver — so the sweep is correct on the routed
                // prod build (the resolver-null bug this fix addresses). With no
                // game context (shape-only Resolve) there are no opponents, so
                // the sweep is a safe no-op.
                var controller = lantern.Controller ?? owner;
                ExileEachOpponentGraveyard(controller, rc.Game?.AllPlayers);
                return default;
            });

        var sweepAbility = new ActivatedAbility(
            source: lantern,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(lantern),
                AdditionalCost.Sacrifice(lantern), // self-sac; zone move in closure
            },
            effects: new IEffect[] { sweepEffect });

        lantern.AddAbility(sweepAbility);

        // ----------------------------------------------------------------
        // {1}, {T}, Sacrifice this artifact: Draw a card.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card",
            () =>
            {
                SacrificeSelf(lantern, owner);
                DrawOne(owner);
            });

        var drawAbility = new ActivatedAbility(
            source: lantern,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Tap(lantern),
                AdditionalCost.Sacrifice(lantern), // self-sac; zone move in closure
            },
            effects: new IEffect[] { drawEffect });

        lantern.AddAbility(drawAbility);

        return lantern;
    }

    // --- ETB / sweep / draw helpers ---------------------------------------

    private static void ResolveEtbExileTarget(TriggeredAbility? etbTrigger)
    {
        if (etbTrigger == null) return;
        if (etbTrigger.ChosenTargets.Count == 0) return;
        if (etbTrigger.ChosenTargets[0].Count == 0) return;

        var raw = etbTrigger.ChosenTargets[0][0];
        if (raw is not ICard targetCard) return;

        // CR 608.2b — the target card must still be in a graveyard.
        if (targetCard.Zone != ZoneType.Graveyard) return;

        var targetOwner = targetCard.Owner;
        if (targetOwner == null) return;

        targetOwner.Zones.Graveyard.RemoveCard(targetCard);
        targetOwner.Zones.Exile.AddCard(targetCard);
        targetCard.SetZone(ZoneType.Exile);
    }

    private static void SacrificeSelf(Artifact lantern, Player owner)
    {
        // Self-sacrifice: Battlefield → Graveyard. Idempotent (mirrors
        // Tormod's Crypt).
        if (lantern.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(lantern);
        owner.Zones.Graveyard.AddCard(lantern);
        lantern.SetZone(ZoneType.Graveyard);
    }

    private static void ExileEachOpponentGraveyard(
        Player controller,
        IReadOnlyList<Player>? allPlayers)
    {
        if (allPlayers == null) return;
        foreach (var opp in allPlayers)
        {
            // CR 102.1 — the controller is never their own opponent.
            if (ReferenceEquals(opp, controller)) continue;
            var graveyardCards = opp.Zones.Graveyard.GetCards().ToList();
            foreach (var card in graveyardCards)
            {
                opp.Zones.Graveyard.RemoveCard(card);
                opp.Zones.Exile.AddCard(card);
                card.SetZone(ZoneType.Exile);
            }
        }
    }

    private static void DrawOne(Player owner)
    {
        // Empty library = silent no-op (SBAs handle the loss condition;
        // mirrors Pyrite Spellbomb's cantrip mode).
        var top = owner.Zones.Library.GetCards().FirstOrDefault();
        if (top == null) return;
        owner.Zones.Library.RemoveCard(top);
        owner.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }
}
