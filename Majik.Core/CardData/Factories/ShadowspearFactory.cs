using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shadowspear (Theros Beyond Death, {1}).
///
/// Legendary Artifact — Equipment. Oracle text:
///   "{1}, {T}: Target creature loses indestructible and hexproof until
///    end of turn."
///   "Equipped creature gets +1/+1 and has trample and lifelink."
///   "Equip {1}."
///
/// ## Implementation
///
/// - <b>Legendary supertype + Equipment subtype</b>, mana cost {1}.
/// - <b>Static "equipped creature gets +1/+1 and has trample and lifelink"</b>:
///     - +1/+1 P/T boost (CR 613 Layer 7c) via
///       <see cref="AttachedBoostEffect"/>.
///     - Granted "Trample" + "Lifelink" (CR 613.1c — Layer 6 ability
///       addition) via a parallel <see cref="AttachedBoostEffect"/> with
///       <c>grantedKeywords</c> registered at <see cref="Layer.Abilities"/>.
///       Same paired-effect shape as <see cref="CoriSteelCutterFactory"/>'s
///       "+1/+1 and has trample and haste". <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/>
///       and <see cref="Majik.Core.Combat.CombatAbilities.HasLifelink"/>
///       read the keyword markers off the equipped creature's working set.
/// - <b>Activated "{1}, {T}: Target creature loses indestructible and
///   hexproof until end of turn"</b> (CR 602, CR 700.2, CR 514 cleanup
///   expiry):
///     - Cost: <see cref="ManaCostCost"/>("{1}") + <see cref="AdditionalCost.Tap"/>.
///     - Target: 1..1 "target creature" — any creature on the battlefield
///       (not restricted to controller). The agent prompt is honoured via
///       <see cref="ActivatedAbility.ChosenTargets"/>.
///     - Resolution registers TWO
///       <see cref="LoseKeywordUntilEndOfTurnEffect"/> instances against
///       the supplied <see cref="ContinuousEffectsService"/> — one
///       stripping "Indestructible" (CR 702.12), one stripping "Hexproof"
///       (CR 702.11). Both effects expire in the cleanup step (CR 514.2)
///       via the base <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///       flag. Without a <see cref="ContinuousEffectsService"/> the
///       resolution is a no-op (shape-only path).
/// - <b>Equip {1}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive (PR #471) with the
///   Puresteel zero-equip cost-provider hook. Sorcery-speed gate +
///   "creature you control" candidate gathering + attach-on-resolve are
///   encapsulated.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, the +1/+1
/// boost (Layer 7c) and the Trample / Lifelink grant (Layer 6) are
/// registered immediately; each gates on Shadowspear being on the
/// battlefield AND attached to a battlefield permanent (see
/// <see cref="AttachedBoostEffect.IsActive"/>). The activated keyword-
/// strip ability also requires the service: without it the resolve closure
/// is a no-op so factory-shape / dispatch tests don't need to wire
/// continuous-effects plumbing.
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for the Equip activation — v1 picks
///   the first controller-side creature deterministically (same gap as
///   the rest of the equipment cycle).
/// - <b>Target-creature prompt</b> for the keyword-strip activation — the
///   activating player's agent populates
///   <see cref="ActivatedAbility.ChosenTargets"/> via
///   <see cref="Majik.Core.Services.AbilityActivationFlow"/> when one is
///   wired; absent that, callers prime targets directly via
///   <see cref="ActivatedAbility.SetChosenTargets"/> (same posture as
///   Umezawa's Jitte's modal minus-1/-1 mode).
/// </summary>
[CardName("Shadowspear")]
public static class ShadowspearFactory
{
    public const string CardName = "Shadowspear";
    public const string Cost = "{1}";
    public const string EquipCost = "{1}";
    public const string KeywordStripCost = "{1}";

    /// <summary>
    /// Constructs Shadowspear with no live continuous-effects wiring (the
    /// shape / dispatcher path). The +1/+1 boost and Trample / Lifelink
    /// grant are NOT registered against any service; the keyword-strip
    /// activated ability is attached but its resolve closure no-ops
    /// without a <see cref="ContinuousEffectsService"/>. Suitable for
    /// unit / shape tests.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Shadowspear. When <paramref name="continuousEffects"/>
    /// is supplied, the +1/+1 boost (Layer 7c) and the Trample / Lifelink
    /// grant (Layer 6) are registered against it; both gate on Shadowspear
    /// being on the battlefield AND attached. The keyword-strip activated
    /// ability also resolves against the same service (registers two
    /// <see cref="LoseKeywordUntilEndOfTurnEffect"/> instances per
    /// activation).
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Static — "Equipped creature gets +1/+1 and has trample and
        // lifelink." Two AttachedBoostEffects: Layer 7c for the +1/+1,
        // Layer 6 for the granted keywords (CR 613.7c + CR 613.1c). Same
        // paired-effect shape as Cori-Steel Cutter's "+1/+1 and has
        // trample and haste."
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 1, toughness: 1));

            continuousEffects.Register(
                new AttachedBoostEffect(
                    source: card,
                    power: 0,
                    toughness: 0,
                    grantedKeywords: new[] { "Trample", "Lifelink" },
                    layer: Layer.Abilities));
        }

        // --------------------------------------------------------------
        // Activated ability — "{1}, {T}: Target creature loses
        // indestructible and hexproof until end of turn." (CR 602, CR
        // 700.2, CR 514.2 cleanup expiry.)
        //
        // Cost stack: ManaCostCost("{1}") + AdditionalCost.Tap(self).
        // Target: 1..1 target creature (any controller — printed wording
        // is unrestricted; HasProtectionFromColor etc. will filter at
        // target-selection time when the agent layer ships full target
        // legality).
        // Resolve: register two LoseKeywordUntilEndOfTurnEffect instances
        // (Indestructible + Hexproof) against the live continuous-effects
        // service. Without a service the activation no-ops (shape-only
        // path).
        // --------------------------------------------------------------
        ActivatedAbility? stripAbility = null;
        var stripEffect = new Effect(
            $"{CardName}: target creature loses indestructible and hexproof EOT",
            () =>
            {
                if (stripAbility == null) return;
                if (continuousEffects == null) return;
                if (stripAbility.ChosenTargets.Count == 0) return;
                if (stripAbility.ChosenTargets[0].Count == 0) return;
                if (stripAbility.ChosenTargets[0][0] is not Creature creature) return;

                // CR 608.2b — if the target is illegal on resolution
                // (left the battlefield), the rules engine will have
                // dropped it from ChosenTargets; double-check zone here
                // for defence in depth.
                if (creature.Zone != Majik.Core.Zones.ZoneType.Battlefield) return;

                continuousEffects.Register(
                    new LoseKeywordUntilEndOfTurnEffect(creature, "Indestructible"));
                continuousEffects.Register(
                    new LoseKeywordUntilEndOfTurnEffect(creature, "Hexproof"));
            });

        stripAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(KeywordStripCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { stripEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(stripAbility);

        // --------------------------------------------------------------
        // Equip {1} — activated ability (CR 702.6) via the shared
        // EquipActivatedAbility primitive with the Puresteel zero-cost
        // provider hook.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
