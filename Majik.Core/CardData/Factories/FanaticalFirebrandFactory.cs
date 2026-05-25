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
/// Named-card factory for Fanatical Firebrand (Ixalan / many reprints,
/// {R}).
///
/// Creature — Goblin Pirate 1/1. Oracle text:
///   "Haste.
///    {T}, Sacrifice Fanatical Firebrand: Fanatical Firebrand deals 1
///    damage to any target."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Goblin Pirate, mana cost {R}, owner/controller wired.
/// - <b>Haste</b> (CR 702.10) wired as a <see cref="KeywordAbility"/>
///   marker on the card; <c>CombatAbilities.HasHaste</c> reads it. Same
///   shape as Goblin Chieftain / Bloodbraid Elf / Earthshaker Khenra.
/// - <b>{T}, Sacrifice: 1 damage to any target</b> — wired as an
///   <see cref="ActivatedAbility"/> with <see cref="AdditionalCost.Tap"/>
///   plus <see cref="AdditionalCost.Sacrifice"/> on the firebrand itself.
///   A single <see cref="TargetRequest"/> is declared so the activating
///   player's agent picks an any-target (player / creature / planeswalker)
///   at activation (CR 602.2b). The resolution effect reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes through
///   <see cref="Fx.DealDamageAny"/> so Planeswalker targets convert to
///   loyalty removal (CR 306.7) — same shape as Pyrite Spellbomb's
///   damage mode. Sacrifice is performed by the effect closure (mirrors
///   Pyrite / Aether / Nihil Spellbomb — the generic
///   <see cref="AdditionalCost.Pay"/> sacrifice path is a stub).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: same gap as the spellbomb
///   family — the engine's generic <see cref="AdditionalCost"/> sacrifice
///   payment is currently a no-op stub. The effect closure performs the
///   zone move so behaviour is observable. Remove the explicit
///   move-to-graveyard once <see cref="AdditionalCost.Pay"/> performs
///   the sacrifice itself.
/// </summary>
[CardName("Fanatical Firebrand")]
public static class FanaticalFirebrandFactory
{
    public const string CardName = "Fanatical Firebrand";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Fanatical Firebrand owned and controlled by
    /// <paramref name="owner"/>.
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

        // CR 702.10 — Haste. KeywordAbility marker; CombatAbilities.HasHaste
        // reads it.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // {T}, Sacrifice this creature: ~ deals 1 damage to any target.
        // CR 602 — activated ability with a single any-target request.
        // Resolution reads ChosenTargets and gates on a damage-receiving
        // shape (Player / Creature / Planeswalker) via Fx.DealDamageAny.
        // Illegal-on-resolution targets fail silently (CR 608.2b) — the
        // sacrifice still resolves because the cost was paid.
        // ----------------------------------------------------------------
        ActivatedAbility? damageAbility = null;
        var damageEffect = new Effect(
            $"{CardName}: 1 damage to any target + sac self",
            () =>
            {
                if (damageAbility != null
                    && damageAbility.ChosenTargets.Count > 0
                    && damageAbility.ChosenTargets[0].Count > 0)
                {
                    var target = damageAbility.ChosenTargets[0][0];
                    Fx.DealDamageAny(target, 1);
                }

                SacrificeSelf(card, owner);
            });

        damageAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(card),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { damageEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(damageAbility);

        return card;
    }

    /// <summary>
    /// Move <paramref name="firebrand"/> from the battlefield to its
    /// owner's graveyard. Idempotent — no-op if already off the
    /// battlefield. Mirrors the closure used by Pyrite / Aether / Nihil
    /// Spellbomb's sac-self effects.
    /// </summary>
    private static void SacrificeSelf(Creature firebrand, Player owner)
    {
        if (firebrand.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(firebrand);
        owner.Zones.Graveyard.AddCard(firebrand);
        firebrand.SetZone(ZoneType.Graveyard);
    }
}
