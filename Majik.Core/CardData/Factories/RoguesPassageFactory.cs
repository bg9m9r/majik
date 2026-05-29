using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rogue's Passage (Innistrad / Magic Origins and
/// many reprints).
///
/// Land. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {4}, {T}: Target creature can't be blocked this turn."
///
/// ## Build path
///
/// Identity + the {T}: Add {C} mana ability are authored in the embedded
/// JSON definition (<c>Majik.Core/CardData/Cards/rogues-passage.json</c>)
/// and materialized through <see cref="CardDefinitionFactory"/> — the same
/// vanilla colorless-land mana shape used by Karn's Bastion / Sea Gate
/// Wreckage / the pain-land cycle. The targeted "{4}, {T}: can't be
/// blocked" activated ability is hand-attached on top because the
/// data-driven <see cref="CardDefinitionFactory"/> does not yet express
/// targeted combat-restriction grants (mirrors the Liquimetal Coating
/// "{T}: Target permanent …" shape, which is likewise hand-rolled).
///
/// ## Implemented (v1)
///
/// - <b>{T}: Add {C}</b> — JSON <c>"mana"</c> ability producing {C}.
///   CR 605.1 — mana abilities don't use the stack.
/// - <b>{4}, {T}: Target creature can't be blocked this turn</b> —
///   <see cref="ActivatedAbility"/> (CR 602.1) with a <see cref="ManaCostCost"/>
///   for the {4} generic mana + an <see cref="AdditionalCost"/>.Tap, and a
///   1..1 <see cref="TargetRequest"/> for "target creature". On resolution
///   the factory reads <see cref="ActivatedAbility.ChosenTargets"/> and,
///   when the choice is a battlefield <see cref="Creature"/>, registers a
///   single-target <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBeBlocked"/> (CR 509.1c) against
///   the supplied <see cref="ContinuousEffectsService"/>. The restriction
///   carries the default <c>expiresAtEndOfTurn = true</c> — "this turn"
///   (CR 514.2 cleanup-step expiry). <see cref="Majik.Core.Combat.BlockLegality"/>
///   consults the restriction directly at declare-blockers.
///   Untargeted, non-creature, or off-battlefield choices resolve as a
///   no-op (CR 608.2b — illegal target → effect does nothing).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter targets to "any creature" — the resolution-time guard handles
///   illegal targets (CR 608.2b), same posture as Liquimetal Coating.
/// - <b>No live continuous-effects service</b>: when <paramref name="effects"/>
///   is null the resolution path no-ops (the tap + {4} are still part of
///   the cost surface). Matches the Liquimetal Coating / Phantom Warrior
///   shape-only path.
/// </summary>
[CardName("Rogue's Passage")]
public static class RoguesPassageFactory
{
    public const string CardName = "Rogue's Passage";

    /// <summary>The {4} generic-mana component of the unblockable ability.</summary>
    public const string ActivationCost = "{4}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("rogues-passage");

    /// <summary>
    /// Construct Rogue's Passage with no live continuous-effects service.
    /// The {4},{T} ability is attached for shape observability but its
    /// "can't be blocked" grant no-ops on resolution. Suitable for
    /// dispatcher / structural tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Rogue's Passage. When <paramref name="effects"/> is
    /// supplied, activating the {4},{T} ability and resolving against a
    /// battlefield <see cref="Creature"/> target registers a single-target
    /// CR 509.1c "can't be blocked" restriction on that creature until end
    /// of turn (CR 514.2).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for the
    /// can't-be-blocked grant. May be null — the grant is then skipped
    /// (shape-only path).</param>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + {T}: Add {C} from the embedded JSON definition.
        var passage = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {4}, {T}: Target creature can't be blocked this turn.
        // CR 602.1 — ordinary activated ability.
        // CR 509.1c — "can't be blocked" combat restriction.
        // CR 514.2 — "this turn" wears off at the cleanup step.
        // ----------------------------------------------------------------
        ActivatedAbility? unblockableAbility = null;
        var unblockableEffect = new Effect(
            $"{CardName}: target creature can't be blocked this turn",
            () =>
            {
                if (passage.Zone != ZoneType.Battlefield) return;
                if (effects == null) return; // shape-only path

                if (unblockableAbility == null) return;
                if (unblockableAbility.ChosenTargets.Count == 0) return;
                if (unblockableAbility.ChosenTargets[0].Count == 0) return;

                if (unblockableAbility.ChosenTargets[0][0] is not Creature target)
                    return; // CR 608.2b — illegal / non-creature target → no-op
                if (target.Zone != ZoneType.Battlefield)
                    return; // target left the battlefield in response

                // expiresAtEndOfTurn defaults to true → "this turn".
                effects.Register(new CombatRestrictionEffect(
                    CombatRestriction.CannotBeBlocked,
                    target: target));
            });

        unblockableAbility = new ActivatedAbility(
            source: passage,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationCost),
                AdditionalCost.Tap(passage),
            },
            effects: new IEffect[] { unblockableEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        passage.AddAbility(unblockableAbility);

        return passage;
    }
}
