using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Welding Jar (Mirrodin, {0}).
///
/// Artifact. Oracle text:
///   "Sacrifice Welding Jar: Regenerate target artifact."
///
/// Cheap Affinity / Hardened Scales / Mox Opal artifact-density enabler:
/// a 0-mana artifact that doubles as a one-shot regenerate shield for any
/// other artifact (Cranial Plating, Arcbound Ravager, Mox Opal itself).
///
/// ## Implementation
///
/// - <b>Identity</b>: Artifact (no Equipment subtype), mana cost {0}.
///   Same convention as <see cref="MemniteFactory"/> /
///   <see cref="MoxOpalFactory"/> / <see cref="BoneSawFactory"/> for the
///   literal {0} string.
/// - <b>Activated ability (CR 602 / 701.18)</b>: a single
///   <see cref="ActivatedAbility"/> with
///   <see cref="AdditionalCost.Sacrifice"/> as the sole cost (no mana
///   pip). Targets exactly one artifact (1..1 <see cref="TargetRequest"/>
///   scoped to <see cref="CardType.Artifact"/>). On resolution:
///     1. <b>Sacrifice self</b>: battlefield → graveyard mutation
///        performed in the effect body (matching the Ranger-Captain of Eos
///        / Glen Elendra Archmage / Hope of Ghirapur posture — the
///        <see cref="AdditionalCost.Sacrifice"/> <c>Pay</c> stub is a
///        no-op today). CR 701.16.
///     2. <b>Register a regen shield</b> on the targeted artifact via
///        <see cref="RegenerationShieldEffect"/>. The shield is a
///        one-shot <see cref="IReplacementEffect{T}"/> over
///        <see cref="DestroyIntent"/> — the next destroy of the target
///        is replaced by tap+clear-damage, then the shield is consumed
///        (CR 701.18 — "the next time it would be destroyed this turn").
/// - <b>Replacement bus wiring</b>: when the artifact's controller has a
///   <see cref="Player.Replacements"/> bus attached, the shield is
///   registered there. Absent a bus the resolution is a structural no-op
///   on the shield half (same posture as
///   <see cref="ContainmentPriestExileReplacementEffect"/>'s shape-only
///   path) — the sacrifice still resolves so factory-shape tests can
///   observe the cost half.
///
/// ## Lifecycle
///
/// - <see cref="Create(Player)"/> — shape only. The activated ability is
///   attached; no replacement bus interaction.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"Until end of turn" expiry</b>: <see cref="RegenerationShieldEffect"/>
///   is a single-fire <c>OneShot</c> replacement; it does not expire at
///   end of turn if the destroy never lands. CR 701.18 specifies "the
///   next time it would be destroyed this turn" — a stale shield
///   surviving into the next turn is a known engine gap (same posture
///   as the rest of the regenerate family).
/// - <b>"Remove from combat"</b>: the shield taps + clears damage but
///   does not remove the regenerated permanent from combat. The
///   <c>RegenerationShieldEffect</c> xmldoc flags the same gap; combat
///   removal lands when <c>CombatFlow</c> exposes a per-creature
///   removal hook.
/// </summary>
[CardName("Welding Jar")]
public static class WeldingJarFactory
{
    public const string CardName = "Welding Jar";
    public const string PrintedManaCost = "{0}";

    /// <summary>
    /// Construct Welding Jar owned and controlled by
    /// <paramref name="owner"/>. The activated regenerate ability is
    /// attached structurally; callers wanting bus-driven shield
    /// registration must ensure the targeted artifact's controller has
    /// a <see cref="ReplacementBus"/> attached via
    /// <see cref="Player.AttachReplacementBus"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: PrintedManaCost);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Activated ability — CR 602 / 701.18.
        //   "Sacrifice Welding Jar: Regenerate target artifact."
        // Cost: AdditionalCost.Sacrifice(self) — no mana pip. Resolution
        // body sacrifices self (CR 701.16) then registers a one-shot
        // RegenerationShieldEffect on the chosen artifact (CR 701.18).
        // ----------------------------------------------------------------
        ActivatedAbility? regenAbility = null;

        var regenEffect = new Effect(
            $"{CardName}: sacrifice self, then regenerate target artifact",
            () =>
            {
                // ---- Sacrifice self (CR 701.16) ----
                if (card.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(card);
                    var sacOwner = card.Owner ?? owner;
                    sacOwner.Zones.Graveyard.AddCard(card);
                    card.SetZone(ZoneType.Graveyard);
                }

                // ---- Register the regen shield on the targeted artifact ----
                if (regenAbility == null) return;
                var slots = regenAbility.ChosenTargets;
                if (slots.Count == 0 || slots[0].Count == 0) return;
                if (slots[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution. Target must still
                // be on the battlefield AND still an artifact.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Artifact)) return;

                var ctrl = target.Controller ?? target.Owner;
                var bus = ctrl?.Replacements;
                if (bus == null) return; // shape-only path — see class xmldoc.

                bus.Register(new RegenerationShieldEffect(target));
            });

        regenAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Sacrifice(card) },
            effects: new IEffect[] { regenEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(regenAbility);

        return card;
    }
}
