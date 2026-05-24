using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cursecatcher (Shadowmoor / various reprints,
/// {U}).
///
/// Creature — Merfolk Wizard 1/1. Oracle text:
///   "Sacrifice Cursecatcher: Counter target spell unless its controller
///    pays {1}."
///
/// ## Implemented (v1)
/// - 1/1 Merfolk Wizard with mana cost {U}.
/// - <b>Activated ability (CR 113.3b)</b>: "Sacrifice Cursecatcher: Counter
///   target spell unless its controller pays {1}."
///   - Cost: <see cref="AdditionalCost.Sacrifice"/> (self-sacrifice; same
///     stub posture as Engineered Explosives / Mishra's Bauble / Lotus
///     Petal). The sacrifice zone-move (Battlefield → Graveyard) is
///     performed by the effect body because <see cref="AdditionalCost.Sacrifice"/>
///     Pay is a TODO stub.
///   - Target: 1..1 "target spell" <see cref="TargetRequest"/>.
///   - Resolution (CR 608): if the target spell's controller can pay {1}
///     (via <see cref="Player.PayMana"/>) the controller pays and the spell
///     is NOT countered. Otherwise, the spell is countered via
///     <see cref="OracleSpellBinder.RemoveFromStack"/> and moved to the
///     graveyard (CR 701.5).
///   - v1: payment auto-resolved (no agent prompt) — same posture as
///     Daze / Mana Leak's "unless pay" implementation.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: <see cref="AdditionalCost.Sacrifice"/>
///   Pay is a TODO stub; the effect body performs the zone-move so
///   test-visible behavior is correct. Once Pay is implemented the explicit
///   move-to-graveyard in the effect can be removed.
/// - <b>Agent prompt for "pay {1}"</b>: the controller's mana pool is
///   consulted directly; no interactive "would you like to pay {1}?"
///   prompt. Same gap as Daze / Mana Leak.
/// - <b>"Activate only as a sorcery" / instant-speed gate</b>: Cursecatcher's
///   activated ability is an activated ability with no timing restriction
///   printed on the card — it may be activated at instant speed. No
///   deferred work needed here.
/// - <b>Flash / sorcery timing</b>: none — Cursecatcher is a creature with
///   no flash keyword.
/// </summary>
public static class CursecatcherFactory
{
    public const string CardName = "Cursecatcher";
    public const string ManaCost = "{U}";

    /// <summary>
    /// Construct Cursecatcher. The activated ability is attached with its
    /// cost (Sacrifice) + target request (1..1 spell) + counter-unless-pay
    /// effect. A live <see cref="Majik.Core.Stack.Stack"/> is required for
    /// the counter effect to operate; pass <see langword="null"/> for
    /// shape-only tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, stack: null);

    /// <summary>
    /// Construct Cursecatcher with an optional live stack. When
    /// <paramref name="stack"/> is supplied, the counter-unless-pay effect
    /// removes the target spell from the stack via
    /// <see cref="OracleSpellBinder.RemoveFromStack"/> (CR 701.5). When
    /// <see langword="null"/>, the counter is a no-op (shape-only use).
    /// </summary>
    public static Creature Create(Player owner, Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: ManaCost,
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Merfolk, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Activated ability — "Sacrifice Cursecatcher: Counter target spell
        // unless its controller pays {1}." CR 113.3b.
        //
        // Cost: Sacrifice Cursecatcher (self). AdditionalCost.Sacrifice is
        // a TODO-stub; the effect body moves the card to the graveyard so
        // the correct observable behavior fires (mirrors Mishra's Bauble /
        // Lotus Petal / Engineered Explosives). See class xmldoc.
        //
        // Effect reads ChosenTargets[0][0] for the target spell object.
        // Counter-unless-pay: auto-consults target's controller mana pool
        // (mirrors DazeFactory / ManaLeakFactory). Payment of {1} prevents
        // the counter (CR 118.4). If unable/unwilling (auto-"no" in v1)
        // the spell is countered and moved to the graveyard (CR 701.5).
        // ----------------------------------------------------------------
        ActivatedAbility? ability = null;

        var counterEffect = new Effect(
            "Cursecatcher — sacrifice self, then counter target spell unless its controller pays {1}",
            () =>
            {
                // ---- Sacrifice Cursecatcher (self-zone-move) ----
                // Because AdditionalCost.Sacrifice.Pay() is a no-op stub,
                // the effect body performs the zone-move directly.
                // CR 701.16 — sacrificing moves the permanent from the
                // battlefield to its owner's graveyard.
                if (card.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(card);
                    var sacOwner = card.Owner ?? owner;
                    sacOwner.Zones.Graveyard.AddCard(card);
                    card.SetZone(ZoneType.Graveyard);
                }

                // ---- Counter unless pay {1} ----
                if (ability == null) return;
                var chosen = ability.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not ISpell spell) return;
                if (stack == null) return;

                // CR 118.4 — target's controller may pay {1} to prevent
                // the counter. v1: auto-pay if mana is available in pool.
                if (spell.Controller is not null
                    && spell.Controller.PayMana(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(1)))
                {
                    // Controller paid — spell is NOT countered.
                    return;
                }

                // CR 701.5 — counter the spell: remove from stack,
                // move to graveyard.
                OracleSpellBinder.RemoveFromStack(stack, spell);
                spell.Card.SetZone(ZoneType.Graveyard);
            });

        ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { counterEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target spell",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(ability);

        return card;
    }
}
