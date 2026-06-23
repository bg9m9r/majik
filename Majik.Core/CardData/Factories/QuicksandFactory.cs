using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Quicksand (multiple printings; Mirage and reprints).
///
/// Land. Oracle text (verified against Scryfall 2026-06-23):
///   "{T}: Add {C}.
///    {T}, Sacrifice this land: Target attacking creature without flying gets
///    -1/-2 until end of turn."
///
/// ## Build path
///
/// Identity + the {T}: Add {C} mana ability are authored in the embedded JSON
/// definition (<c>Majik.Core/CardData/Cards/quicksand.json</c>) and
/// materialized through <see cref="CardDefinitionFactory"/> — the same vanilla
/// colorless-land mana shape used by Nephalia Drownyard / Rogue's Passage. The
/// targeted "{T}, Sacrifice this land: Target attacking creature without flying
/// gets -1/-2 until end of turn" activated ability is hand-attached on top
/// because the data-driven <see cref="CardDefinitionFactory"/> does not yet
/// express a sacrifice-cost / attacker-targeted pump effect (same posture as
/// <see cref="NephaliaDrownyardFactory"/>'s hand-rolled targeted mill).
///
/// ## Implemented (v1)
///
/// - <b>{T}: Add {C}</b> — JSON <c>"mana"</c> ability producing {C}.
///   CR 605.1 — mana abilities don't use the stack.
/// - <b>{T}, Sacrifice this land: Target attacking creature without flying
///   gets -1/-2 until end of turn</b> — an <see cref="ActivatedAbility"/>
///   (CR 602.1, uses the stack) whose costs are <see cref="AdditionalCost.Tap"/>
///   on the land + <see cref="AdditionalCost.Sacrifice"/> of the land itself.
///   The 1..1 "target attacking creature without flying" <see cref="TargetRequest"/>
///   draws its candidates from the caller-injected <paramref name="attackerLookup"/>
///   (production wires <see cref="CombatManager.CurrentCombat"/>'s
///   <see cref="Combat.Attackers"/> per CR 506.2; tests inject directly),
///   filtered to creatures that do NOT have flying
///   (<see cref="Creature.HasEffectiveKeyword"/>("Flying")). On resolution the
///   factory reads <see cref="ActivatedAbility.ChosenTargets"/>[0][0] and, when
///   the choice is a <see cref="Creature"/> still on the battlefield, registers
///   a <see cref="PumpUntilEndOfTurnEffect"/>(-1, -2) on its
///   <see cref="Creature.ActiveEffects"/> (CR 514.2 — "until end of turn"
///   Layer 7c effect). A non-Creature / off-battlefield target makes the
///   ability no-op per CR 608.2b (illegal target at resolution). When
///   ActiveEffects is null (shape-only tests with no live
///   ContinuousEffectsService) the registration is a no-op.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Target legality in ActionValidator</b>: the "attacking, non-flying"
///   constraint is enforced only via the candidate gatherer + the
///   resolution-time guard (CR 608.2b). Same posture as Condemn / Nephalia
///   Drownyard — the activator's pick is honoured verbatim by the test harness.
/// </summary>
[CardName("Quicksand")]
public static class QuicksandFactory
{
    public const string CardName = "Quicksand";
    public const string Slug = "quicksand";

    /// <summary>Power delta applied to the target until end of turn.</summary>
    public const int PowerDelta = -1;

    /// <summary>Toughness delta applied to the target until end of turn.</summary>
    public const int ToughnessDelta = -2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Quicksand. Identity + the {T}: Add {C} mana ability come from
    /// JSON; the {T},Sacrifice targeted-pump activated ability is hand-attached
    /// on top.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="attackerLookup">Returns the attacking creatures currently in
    /// combat. Production callers wire this from
    /// <see cref="CombatManager.CurrentCombat"/>'s <see cref="Combat.Attackers"/>
    /// (CR 506.2); test callers inject a list directly. Null (or a delegate
    /// returning null / empty) is legal — the shape-only / dispatcher path with
    /// no live combat reports no candidates.</param>
    public static Land Create(
        Player owner,
        Func<IReadOnlyList<Creature>>? attackerLookup = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + {T}: Add {C} from the embedded JSON definition.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {T}, Sacrifice this land: Target attacking creature without flying
        //   gets -1/-2 until end of turn.
        // CR 602.1 — ordinary activated ability (uses the stack).
        // CR 506.2 — "attacking creature" = a creature currently attacking
        //   this combat.
        // CR 514.2 — "until end of turn" effects last through the cleanup step.
        // CR 608.2b — an illegal (non-Creature / off-battlefield) target at
        //   resolution → no-op.
        // ----------------------------------------------------------------
        ActivatedAbility? pumpAbility = null;
        var pumpEffect = new Effect(
            $"{CardName}: target attacking creature without flying gets {PowerDelta}/{ToughnessDelta} until end of turn",
            () =>
            {
                if (pumpAbility == null) return;
                if (pumpAbility.ChosenTargets.Count == 0) return;
                if (pumpAbility.ChosenTargets[0].Count == 0) return;
                if (pumpAbility.ChosenTargets[0][0] is not Creature target) return;

                // CR 608.2b — target must still be a creature on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;

                // CR 514.2 — register a -1/-2 EOT-scoped Layer 7c effect.
                // Same pattern as DismemberFactory. When ActiveEffects is null
                // (shape-only tests) the registration is a no-op.
                if (target.ActiveEffects == null) return;
                target.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(target, PowerDelta, ToughnessDelta));
            });

        pumpAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land),
            },
            effects: new IEffect[] { pumpEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target attacking creature without flying",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer: only attacking creatures from the current
                    // combat (CR 506.2) that do NOT have flying. Injected by
                    // the caller so the factory stays testable without a live
                    // game loop.
                    CandidateGatherer: _ =>
                    {
                        if (attackerLookup == null)
                            return Array.Empty<object>();

                        var pool = attackerLookup() ?? Array.Empty<Creature>();
                        return pool
                            .Where(c => !c.HasEffectiveKeyword("Flying"))
                            .Cast<object>()
                            .ToList();
                    }),
            });

        land.AddAbility(pumpAbility);

        return land;
    }
}
