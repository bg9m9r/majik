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
/// Named-card factory for Fanatical Firebrand (Dominaria + Core 2019 +
/// many reprints, {R}).
///
/// Creature — Goblin Pirate 1/1. Oracle text (Scryfall, verified):
///   "Haste
///    {T}, Sacrifice this creature: It deals 1 damage to any target."
///
/// ## Implemented (v1)
///
/// - 1/1 Creature — Goblin Pirate at printed cost {R}; owner / controller
///   wired. Both <see cref="CardSubtype.Goblin"/> and
///   <see cref="CardSubtype.Pirate"/> are stamped — Goblin Chieftain /
///   Krenko-tribal scopes see Firebrand correctly, and Pirate tribal
///   anchors (Hullbreacher's tribal cousins / Admiral Beckett Brass) also
///   pick it up.
/// - <b>Haste (CR 702.10)</b>: <see cref="KeywordAbility"/> marker — combat
///   helpers in <c>CombatAbilities.HasHaste</c> read it directly, same
///   posture as Monastery Swiftspear / Goblin Chieftain.
/// - <b>{T}, Sacrifice this creature: It deals 1 damage to any target
///   (CR 602)</b>: <see cref="ActivatedAbility"/> with:
///   <list type="number">
///     <item><see cref="AdditionalCost.Tap"/> on the Firebrand (CR 602.1b
///       — tap symbol resolves to tap-this-permanent).</item>
///     <item><see cref="AdditionalCost.Sacrifice"/> on the Firebrand
///       itself — the cost surface registers the intent; the actual
///       battlefield → graveyard zone move is performed inside the effect
///       closure (mirrors Pyrite Spellbomb / Caustic Caterpillar — the
///       generic <see cref="AdditionalCost.Pay"/> sacrifice path is a
///       no-op stub).</item>
///   </list>
///   A single any-target request is declared so the activating player's
///   agent picks a damage-receiving target (player / creature /
///   planeswalker) at activation (CR 602.2b). The resolution effect reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes through
///   <see cref="Fx.DealDamageAny"/> so Planeswalker targets convert to
///   loyalty removal (CR 306.7) — same shape as Pyrite Spellbomb /
///   Lightning Bolt / Helix.
///
/// ## Order of operations
///
/// CR 117.1c — all costs for an activated ability are paid simultaneously
/// from the player's perspective. The implementation taps Firebrand via
/// the cost surface, then performs the sacrifice + damage inside the
/// resolution closure; legality is checked before any payment.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move so behaviour is
///   observable. Mirrors Pyrite Spellbomb / Insolent Neonate.
/// - <b>Self-targeting at activation</b>: Firebrand could theoretically
///   pick itself as the damage target — but since the sac cost happens
///   first and removes it from the battlefield, the self-target becomes
///   illegal-on-resolution (CR 608.2b). The closure's
///   <see cref="Fx.DealDamageAny"/> route silently no-ops on a creature
///   no longer on the battlefield (lethal-damage check requires zone =
///   battlefield), matching real-card behaviour.
/// </summary>
[CardName("Fanatical Firebrand")]
public static class FanaticalFirebrandFactory
{
    public const string CardName = "Fanatical Firebrand";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int PingDamage = 1;

    /// <summary>
    /// Construct Fanatical Firebrand owned and controlled by
    /// <paramref name="owner"/>. Haste keyword marker + the tap-sac-ping
    /// activated ability are attached to the card. The ability is fully
    /// self-contained — no service wiring required.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Pirate });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste keyword marker. CombatAbilities.HasHaste reads
        // it; same shape as Monastery Swiftspear / Goblin Chieftain.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // {T}, Sacrifice this creature: It deals 1 damage to any target.
        // CR 602 — activated ability with a single any-target request.
        // Resolution reads ChosenTargets and routes through
        // Fx.DealDamageAny so Planeswalker loyalty removal (CR 306.7) is
        // handled correctly. The sacrifice payment is performed inside
        // the effect closure because the generic AdditionalCost.Sacrifice
        // payment is a no-op stub (mirrors Pyrite Spellbomb).
        // ----------------------------------------------------------------
        ActivatedAbility? pingAbility = null;
        var pingEffect = new Effect(
            $"{CardName}: 1 damage to any target + sac self",
            () =>
            {
                // Sacrifice payment — battlefield → owner's graveyard.
                // CR 701.16 — idempotent guard against stale activations.
                // Sac BEFORE the damage so the "it deals 1 damage" source
                // is the Firebrand on its way to the graveyard; for the
                // damage-receiving-target check this doesn't matter
                // (Fx.DealDamageAny only inspects the target), but the
                // zone-state semantics line up with CR 117.5 (last-known-
                // information on a card that's left the battlefield).
                if (card.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(card);
                    owner.Zones.Graveyard.AddCard(card);
                    card.SetZone(ZoneType.Graveyard);
                }

                if (pingAbility != null
                    && pingAbility.ChosenTargets.Count > 0
                    && pingAbility.ChosenTargets[0].Count > 0)
                {
                    var target = pingAbility.ChosenTargets[0][0];
                    Fx.DealDamageAny(target, PingDamage);
                }
            });

        pingAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(card),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { pingEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(pingAbility);

        return card;
    }
}
