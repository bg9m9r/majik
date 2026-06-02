using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cephalid Coliseum (Torment, Land).
///
/// Oracle text:
///   "{T}: Add {U}. This land deals 1 damage to you.
///    Threshold — {U}, {T}, Sacrifice this land: Target player draws three
///    cards, then discards three cards. Activate only if there are seven or
///    more cards in your graveyard."
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — non-basic, no subtype, owner / controller wired.
/// - <b>{T}: Add {U}. This land deals 1 damage to you.</b> — built via the
///   additional-cost overload of <see cref="ManaAbility"/> (the painland
///   shape, cf. <see cref="PainLandCycleFactory"/>): tapping pays {T}; the
///   <c>additionalCostPayer</c> then reduces the controller's life by 1
///   (CR 120.3 — "deals 1 damage to you" reduces life by the damage amount).
///   No life-floor gate — Cephalid Coliseum can deal lethal damage to you
///   (CR 119.4's "you can't pay life you don't have" gates only "Pay N life"
///   costs, not damage). Same simplification the painland cycle takes: the
///   1 damage goes through <see cref="Player.LoseLife"/>, not a
///   <c>DamageDealtEvent</c>, so damage-prevention subscribers don't
///   intercept it.
/// - <b>Threshold loot-3 activated ability</b> (CR 602) — a single
///   <see cref="ActivatedAbility"/> with three costs:
///   <see cref="ManaCostCost"/> {U}, <see cref="AdditionalCost.Tap"/> ({T}),
///   and <see cref="AdditionalCost.Sacrifice"/> (Sacrifice this land —
///   CR 701.16). The Threshold rider (CR 702.84 — "Activate only if there
///   are seven or more cards in your graveyard") is enforced as the
///   ability's <c>canActivateCheck</c>: it counts the cards in the
///   <b>controller's</b> graveyard ("your graveyard") and gates activation
///   at ≥ 7. The ability declares one 1..1 "target player"
///   <see cref="TargetRequest"/>; on resolution it reads the chosen player
///   off <see cref="ActivatedAbility.ChosenTargets"/> and runs the printed
///   loot: <see cref="Fx.DrawCards"/>(target, 3) then
///   <see cref="Fx.Discard"/>(target, 3). CR 608.2b — if no legal target is
///   present at resolution the effect is a silent no-op.
///
/// ## Deferred (v1 gaps)
/// - <b>Live agent target prompt / TriggerManager wiring</b>: the factory
///   attaches the activated ability structurally. Tests set the chosen
///   player via <see cref="ActivatedAbility.SetChosenTargets"/> and fire the
///   effect directly — same posture as
///   <see cref="GuideOfSoulsFactory"/>'s pump ability.
/// - <b>Discard selection</b>: <see cref="Fx.Discard"/> uses the v1
///   deterministic "first card in hand" discard policy (no agent-driven
///   choice of which three cards to pitch). Note Cephalid Coliseum's printed
///   wording lets the targeted PLAYER choose which three they discard;
///   modelling that choice awaits agent-driven discard prompting.
/// </summary>
[CardName("Cephalid Coliseum")]
public static class CephalidColiseumFactory
{
    public const string CardName = "Cephalid Coliseum";

    /// <summary>CR 702.84 — Threshold turns on at seven cards in your graveyard.</summary>
    public const int ThresholdGraveyardCount = 7;

    /// <summary>Cards the target player draws, then discards (the loot count).</summary>
    public const int LootCount = 3;

    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {U}. This land deals 1 damage to you.
        // Painland shape (CR 120.3 — damage to you reduces life by 1). No
        // life-floor gate (CR 119.4 doesn't apply to damage). Tapping pays
        // {T}; the additionalCostPayer then loses the controller 1 life.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("U"),
            canActivateCheck: () => !land.IsTapped,
            additionalCostPayer: p => p.LoseLife(1)));

        // ----------------------------------------------------------------
        // Threshold — {U}, {T}, Sacrifice this land:
        //   Target player draws three cards, then discards three cards.
        //   Activate only if there are seven or more cards in your graveyard.
        //
        // canActivateCheck enforces the Threshold rider (CR 702.84) against
        // the CONTROLLER's graveyard ("your graveyard"). The loot reads the
        // chosen player and runs draw-3-then-discard-3 (CR 602 resolution).
        // ----------------------------------------------------------------
        ActivatedAbility? threshold = null;
        var lootEffect = new Effect(
            "Cephalid Coliseum — target player draws three cards, then discards three cards",
            () =>
            {
                if (threshold == null) return;
                var chosen = threshold.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return; // CR 608.2b — no target.

                if (chosen[0][0] is not Player target) return;

                // Printed order: draw three, THEN discard three.
                Fx.DrawCards(target, LootCount);
                Fx.Discard(target, LootCount);
            });

        threshold = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{U}"),
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land),
            },
            effects: new IEffect[] { lootEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Draw,
                    CandidateGatherer: ctx => ctx.AllPlayers.Cast<object>().ToList()),
            },
            // CR 702.84 — Threshold: seven or more cards in YOUR graveyard.
            canActivateCheck: () =>
                (land.Controller ?? owner).Zones.Graveyard.Count >= ThresholdGraveyardCount);

        land.AddAbility(threshold);

        return land;
    }
}
