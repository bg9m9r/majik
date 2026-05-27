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
/// Named-card factory for Mogg Fanatic (Tempest / many reprints, {R}).
///
/// Creature — Goblin 1/1. Oracle text (Scryfall, verified):
///   "Sacrifice Mogg Fanatic: It deals 1 damage to any target."
///
/// ## Implemented (v1)
///
/// - 1/1 Creature — Goblin at printed cost {R}; owner / controller wired.
/// - <b>Sacrifice this creature: It deals 1 damage to any target
///   (CR 602)</b>: <see cref="ActivatedAbility"/> with:
///   <list type="number">
///     <item><see cref="AdditionalCost.Sacrifice"/> on Mogg Fanatic
///       itself — the cost surface registers the intent; the actual
///       battlefield → graveyard zone move is performed inside the effect
///       closure (mirrors Fanatical Firebrand / Pyrite Spellbomb / Caustic
///       Caterpillar — the generic <see cref="AdditionalCost.Pay"/>
///       sacrifice path is a no-op stub).</item>
///   </list>
///   No <see cref="AdditionalCost.Tap"/> — the printed cost has no tap
///   symbol; the ability is sac-only (distinguishes Mogg Fanatic from
///   Fanatical Firebrand, which is {T}, Sac for the same effect).
///   A single any-target request is declared so the activating player's
///   agent picks a damage-receiving target (player / creature /
///   planeswalker) at activation (CR 602.2b). The resolution effect reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes through
///   <see cref="Fx.DealDamageAny"/> so Planeswalker targets convert to
///   loyalty removal (CR 306.7) — same shape as Pyrite Spellbomb /
///   Lightning Bolt / Helix / Fanatical Firebrand.
///
/// ## Distinct from Fanatical Firebrand
///
/// - <b>No Haste</b>: Mogg Fanatic's printed text has no Haste keyword
///   (the Tempest printing is summoning-sick on turn-of-cast — the
///   sacrifice ability isn't a tap ability so summoning sickness doesn't
///   prevent activation per CR 302.1, but the creature can't attack the
///   turn it's cast).
/// - <b>No tap cost</b>: the ability is "Sacrifice Mogg Fanatic" only,
///   not "{T}, Sacrifice" — Fanatic can sacrifice while tapped, while
///   summoning-sick, or any other state that would block a tap activation.
/// - <b>Subtype</b>: Mogg Fanatic is "Creature — Goblin" (no Warrior /
///   Berserker), so Krenko-tribal / Goblin Chieftain / Goblin Warchief
///   anchors see it correctly but Warrior tribal (Munitions Expert /
///   Foundry Street Denizen / Goblin Piledriver buffs) does NOT.
///
/// ## Order of operations
///
/// CR 117.1c — all costs for an activated ability are paid simultaneously
/// from the player's perspective. The implementation performs the
/// sacrifice + damage inside the resolution closure; target legality is
/// checked at activation, payment at resolution.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move so behaviour is
///   observable. Mirrors Fanatical Firebrand / Pyrite Spellbomb / Insolent
///   Neonate.
/// - <b>Self-targeting at activation</b>: Mogg Fanatic could theoretically
///   pick itself as the damage target — but since the sac cost happens
///   first and removes it from the battlefield, the self-target becomes
///   illegal-on-resolution (CR 608.2b). The closure's
///   <see cref="Fx.DealDamageAny"/> route silently no-ops on a creature
///   no longer on the battlefield (lethal-damage check requires zone =
///   battlefield), matching real-card behaviour. Same gap as Fanatical
///   Firebrand.
/// </summary>
[CardName("Mogg Fanatic")]
public static class MoggFanaticFactory
{
    public const string CardName = "Mogg Fanatic";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int PingDamage = 1;

    /// <summary>
    /// Construct Mogg Fanatic owned and controlled by
    /// <paramref name="owner"/>. The sac-ping activated ability is attached
    /// to the card. The ability is fully self-contained — no service wiring
    /// required.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Sacrifice Mogg Fanatic: It deals 1 damage to any target.
        // CR 602 — activated ability with a single any-target request.
        // Resolution reads ChosenTargets and routes through
        // Fx.DealDamageAny so Planeswalker loyalty removal (CR 306.7) is
        // handled correctly. The sacrifice payment is performed inside
        // the effect closure because the generic AdditionalCost.Sacrifice
        // payment is a no-op stub (mirrors Fanatical Firebrand / Pyrite
        // Spellbomb).
        // ----------------------------------------------------------------
        ActivatedAbility? pingAbility = null;
        var pingEffect = new Effect(
            $"{CardName}: 1 damage to any target + sac self",
            () =>
            {
                // Sacrifice payment — battlefield → owner's graveyard.
                // Sac BEFORE the damage so the "it deals 1 damage" source
                // is the Fanatic on its way to the graveyard; for the
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
