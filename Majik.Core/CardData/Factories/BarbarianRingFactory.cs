using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Barbarian Ring (Odyssey).
///
/// Land. Oracle text:
///   "{T}: Add {R}. Barbarian Ring deals 1 damage to you.
///    Threshold — {R}, {T}, Sacrifice Barbarian Ring: It deals 2 damage to
///    any target. Activate only if there are seven or more cards in your
///    graveyard."
///
/// ## Implemented (v1)
///
/// - Land identity (non-Basic, no subtype), correct printed name.
///
/// - <b>{T}: Add {R}. This land deals 1 damage to you.</b> — wired as a
///   <see cref="ManaAbility"/> with the additional-cost overload:
///   <c>additionalCostPayer = controller.LoseLife(1)</c>, matching the
///   painland shape from <see cref="PainLandCycleFactory"/>. The
///   <c>canActivateCheck</c> gates on !IsTapped only (CR 119.4 does NOT
///   apply — pain damage is not a "Pay life" cost, so no life-floor gate).
///
/// - <b>Threshold — {R}, {T}, Sacrifice this land: deals 2 damage to any
///   target.</b> — wired as an <see cref="ActivatedAbility"/> with
///   <see cref="AdditionalCost.Tap"/> + <see cref="AdditionalCost.Sacrifice"/>
///   costs (plus an implicit {R} mana cost via <see cref="ManaCostCost"/>).
///   A single any-target request is declared (CR 602.2b). The resolution
///   effect:
///   1. Checks the threshold gate (≥7 cards in controller's graveyard,
///      CR 702.57) — if not met, no-ops silently (mirrors
///      <see cref="MagmaticChannelerFactory"/>'s resolve-time guard pattern).
///   2. Performs the sacrifice (battlefield → owner's graveyard, same closure
///      as Pyrite Spellbomb / Mogg Fanatic).
///   3. Calls <see cref="Fx.DealDamageAny"/> on the chosen target for 2
///      damage (player life loss / creature damage / planeswalker loyalty per
///      CR 306.7).
///
/// ## Threshold (CR 702.57)
///
/// "Activate only if there are seven or more cards in your graveyard" is an
/// activation restriction (CR 602.5b). v1 enforces this via a resolve-time
/// guard inside the effect closure (same safety-net as MagmaticChanneler).
/// The public static <see cref="IsThresholdActive"/> predicate exposes the
/// check for bot policies and future IActivatedAbility.CanActivate wiring.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Sacrifice payment side effects</b>: the generic
///   <see cref="AdditionalCost"/> sacrifice payment is a no-op stub. The
///   effect closure performs the zone move. Mirrors Pyrite Spellbomb.
/// - <b>Damage event routing</b>: the mana-ability's 1 damage goes through
///   <see cref="Player.LoseLife"/>, not a full damage event. Mirrors the
///   painland deferred gap.
/// </summary>
[CardName("Barbarian Ring")]
public static class BarbarianRingFactory
{
    public const string CardName = "Barbarian Ring";
    public const int ThresholdCount = 7;
    public const int SacDamage = 2;

    /// <summary>
    /// Construct Barbarian Ring owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {R}. Barbarian Ring deals 1 damage to you.
        // CR 605.1 — mana ability (doesn't use the stack).
        // Pain rider via additionalCostPayer = LoseLife(1), matching the
        // painland shape (PainLandCycleFactory). No life-floor gate —
        // CR 119.4 does NOT apply to damage-based pain riders.
        // ----------------------------------------------------------------
        var mana = ManaCost.Parse("R");
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: mana,
            canActivateCheck: () => !land.IsTapped,
            additionalCostPayer: p => p.LoseLife(1)));

        // ----------------------------------------------------------------
        // Threshold — {R}, {T}, Sacrifice Barbarian Ring:
        //   It deals 2 damage to any target.
        // CR 602 — activated ability with a single any-target request.
        // Resolve-time guard enforces ≥7 graveyard cards (CR 702.57 /
        // CR 602.5b). Sacrifice performed inside the effect closure
        // (generic AdditionalCost.Sacrifice is a no-op stub — mirrors
        // Pyrite Spellbomb / Mogg Fanatic). Damage routes through
        // Fx.DealDamageAny so planeswalker loyalty removal (CR 306.7)
        // is handled.
        // ----------------------------------------------------------------
        ActivatedAbility? sacAbility = null;
        var sacEffect = new Effect(
            $"{CardName} (Threshold): sacrifice self + deal 2 damage to any target",
            () =>
            {
                // CR 602.5b / CR 702.57 — resolve-time threshold gate.
                // The bot policy and action validator should also gate at
                // activation time via IsThresholdActive(); this guard is
                // the authoritative safety net until IActivatedAbility
                // ships a CanActivate hook.
                var controller = land.Controller ?? owner;
                if (!IsThresholdActive(controller)) return;

                // Sacrifice — battlefield → owner's graveyard.
                // Performed before damage for rules fidelity (CR 117.1c —
                // cost is part of the activation; the sacrifice happens
                // as part of cost payment, then the effect resolves).
                SacrificeSelf(land, owner);

                // Damage resolution — read ChosenTargets set by
                // AbilityActivationFlow before Resolve() is called.
                if (sacAbility != null
                    && sacAbility.ChosenTargets.Count > 0
                    && sacAbility.ChosenTargets[0].Count > 0)
                {
                    var target = sacAbility.ChosenTargets[0][0];
                    Fx.DealDamageAny(target, SacDamage);
                }
            });

        sacAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{R}"),
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land),
            },
            effects: new IEffect[] { sacEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(sacAbility);

        return land;
    }

    /// <summary>
    /// CR 702.57 — Threshold: true iff the controller's graveyard contains
    /// seven or more cards. Public for bot-policy / action-validator use.
    /// </summary>
    public static bool IsThresholdActive(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Graveyard.GetCards().Count() >= ThresholdCount;
    }

    /// <summary>
    /// Move <paramref name="land"/> from the battlefield to its owner's
    /// graveyard. Idempotent — no-op if already off the battlefield.
    /// Mirrors the closure used by Pyrite Spellbomb / Mogg Fanatic.
    /// </summary>
    private static void SacrificeSelf(Land land, Player owner)
    {
        if (land.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(land);
        owner.Zones.Graveyard.AddCard(land);
        land.SetZone(ZoneType.Graveyard);
    }
}
