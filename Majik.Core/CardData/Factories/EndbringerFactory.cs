using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Endbringer (Oath of the Gatewatch, {5}{C}).
///
/// Creature — Eldrazi 5/5. Oracle text (Scryfall, verified):
///   "Vigilance, reach
///    {T}: Endbringer deals 1 damage to any target.
///    {C}, {T}: Target player draws a card.
///    {C}, {T}: Tap target creature."
///
/// ## Implemented (v1)
/// - 5/5 Creature — Eldrazi at {5}{C}.
/// - <b>Vigilance (CR 702.20)</b> + <b>Reach (CR 702.17)</b> as
///   <see cref="KeywordAbility"/> markers — combat-side consumers read via
///   <see cref="Majik.Core.Combat.CombatAbilities"/>.
/// - <b>{T}: Endbringer deals 1 damage to any target (CR 602)</b>:
///   <see cref="ActivatedAbility"/> with sole cost
///   <see cref="AdditionalCost.Tap"/>(self) + 1..1 "any target"
///   <see cref="TargetRequest"/>. Resolution reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes through
///   <see cref="Fx.DealDamageAny"/> (Player / Creature / Planeswalker
///   funnel per CR 119.3 / CR 306.7), same posture as Pyrite Spellbomb /
///   Walking Ballista.
/// - <b>{C}, {T}: Target player draws a card (CR 602)</b>:
///   <see cref="ActivatedAbility"/> with cost stack
///   <c>[ManaCostCost("{C}"), AdditionalCost.Tap(self)]</c> + 1..1
///   "target player" <see cref="TargetRequest"/>. Resolution reads
///   <see cref="ActivatedAbility.ChosenTargets"/>, falls back to the
///   controller when no target chosen (matches Nihil Spellbomb's
///   deterministic posture), and routes a single draw through
///   <see cref="Fx.DrawCards"/> so future
///   <see cref="Majik.Core.Events.DrawCardIntent"/> replacements (Dredge,
///   etc.) participate.
/// - <b>{C}, {T}: Tap target creature (CR 602 + CR 701.21)</b>:
///   <see cref="ActivatedAbility"/> with cost stack
///   <c>[ManaCostCost("{C}"), AdditionalCost.Tap(self)]</c> + 1..1
///   "target creature" <see cref="TargetRequest"/>. Resolution re-checks
///   target is still a creature on the battlefield (CR 608.2b) and taps
///   via <see cref="Fx.Tap"/>. Tapping an already-tapped target is a
///   no-op (CR 701.21b — "taps" with no effect).
///
/// ## Deferred (v1 gaps)
/// - <b>Colorless mana spend restriction enforcement</b>: the {C} cost
///   pip is parsed by <see cref="ManaCost.Parse"/> and stored as a
///   colorless requirement; <see cref="ManaPaymentResolver"/> currently
///   accepts generic mana for colorless slots (same posture as Eldrazi
///   Temple's colorless-only rider — the engine-wide colorless-only
///   payment gate is pending the per-slot provenance work flagged in
///   `EldraziTempleFactory`).
/// </summary>
[CardName("Endbringer")]
public static class EndbringerFactory
{
    public const string CardName = "Endbringer";
    public const string PrintedManaCost = "{5}{C}";
    public const string ColorlessActivationCost = "{C}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>
    /// Construct Endbringer owned and controlled by <paramref name="owner"/>.
    /// All three activated abilities are attached. Tap / damage / draw /
    /// tap-target resolutions use the primitive <see cref="Fx"/> helpers;
    /// no <see cref="Majik.Core.Services.ZoneService"/> or
    /// <see cref="Majik.Core.Events.IEventBus"/> wiring is required for
    /// the v1 surface.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.20 — Vigilance. CR 702.17 — Reach. Both shipped as
        // KeywordAbility markers consumed by CombatValidator /
        // CombatAbilities, same wiring posture as Atraxa / Sun Titan /
        // World Breaker.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));
        card.AddAbility(new KeywordAbility("Reach", card, owner));

        // ----------------------------------------------------------------
        // {T}: Endbringer deals 1 damage to any target.
        // CR 602 — activated ability. Sole cost is the self-tap; no mana
        // pip. 1..1 "any target" TargetRequest. Resolution funnels through
        // Fx.DealDamageAny (Player / Creature / Planeswalker — CR 119.3,
        // CR 306.7 loyalty conversion).
        // ----------------------------------------------------------------
        ActivatedAbility? damageAbility = null;
        var damageEffect = new Effect(
            $"{CardName}: 1 damage to any target",
            () =>
            {
                if (damageAbility == null) return;
                if (damageAbility.ChosenTargets.Count == 0) return;
                if (damageAbility.ChosenTargets[0].Count == 0) return;

                var target = damageAbility.ChosenTargets[0][0];
                Fx.DealDamageAny(target, 1);
            });

        damageAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { damageEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn),
            });

        card.AddAbility(damageAbility);

        // ----------------------------------------------------------------
        // {C}, {T}: Target player draws a card.
        // CR 602 — activated ability. Cost stack: ManaCostCost("{C}") +
        // AdditionalCost.Tap(self). 1..1 "target player" TargetRequest.
        // Resolution reads ChosenTargets[0][0] as a Player and routes the
        // draw through Fx.DrawCards so DrawCardIntent replacement
        // subscribers (Dredge / future replacement primitives) participate.
        // Falls back to the controller (Nihil Spellbomb posture) when no
        // target is supplied — the v1 deterministic path.
        // ----------------------------------------------------------------
        ActivatedAbility? drawAbility = null;
        var drawEffect = new Effect(
            $"{CardName}: target player draws a card",
            () =>
            {
                Player? targetPlayer = null;
                if (drawAbility != null
                    && drawAbility.ChosenTargets.Count > 0
                    && drawAbility.ChosenTargets[0].Count > 0
                    && drawAbility.ChosenTargets[0][0] is Player chosen)
                {
                    targetPlayer = chosen;
                }
                else
                {
                    targetPlayer = owner;
                }

                Fx.DrawCards(targetPlayer, 1);
            });

        drawAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ColorlessActivationCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { drawEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Draw),
            });

        card.AddAbility(drawAbility);

        // ----------------------------------------------------------------
        // {C}, {T}: Tap target creature.
        // CR 602 + CR 701.21. Cost stack: ManaCostCost("{C}") +
        // AdditionalCost.Tap(self). 1..1 "target creature" TargetRequest.
        // Resolution re-checks the chosen target is still a creature on
        // the battlefield (CR 608.2b — illegal-on-resolution fails
        // silently) and taps via Fx.Tap. Tapping an already-tapped
        // permanent is a no-op (Permanent.Tap is idempotent).
        // ----------------------------------------------------------------
        ActivatedAbility? tapAbility = null;
        var tapEffect = new Effect(
            $"{CardName}: tap target creature",
            () =>
            {
                if (tapAbility == null) return;
                if (tapAbility.ChosenTargets.Count == 0) return;
                if (tapAbility.ChosenTargets[0].Count == 0) return;

                if (tapAbility.ChosenTargets[0][0] is not Permanent target) return;

                // CR 608.2b — recheck legality at resolution.
                if (!target.HasType(CardType.Creature)) return;
                if (target.Zone != ZoneType.Battlefield) return;

                Fx.Tap(target);
            });

        tapAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ColorlessActivationCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { tapEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(tapAbility);

        return card;
    }
}
