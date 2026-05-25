using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glen Elendra Archmage (Eventide, {1}{U}{U}).
///
/// Creature — Faerie Wizard 2/2. Oracle text:
///   "Flying
///    {U}, Sacrifice Glen Elendra Archmage: Counter target noncreature spell.
///    Persist (When this creature dies, if it had no -1/-1 counters on it,
///    return it to the battlefield under its owner's control with a -1/-1
///    counter on it.)"
///
/// ## Implemented (v1)
/// - 2/2 Creature — Faerie Wizard, mana cost {1}{U}{U}.
/// - <see cref="KeywordAbility"/> "Flying" marker (CR 702.9).
/// - <b>Persist (CR 702.79)</b>: wired via the shared
///   <see cref="PersistFactory.Build(Creature)"/> primitive.
/// - <b>Activated ability (CR 113.3b)</b>: "{U}, Sacrifice Glen Elendra
///   Archmage: Counter target noncreature spell."
///   - Cost: <see cref="ManaCostCost"/>("{U}") + <see cref="AdditionalCost.Sacrifice"/>
///     (self-sacrifice). Same posture as Cursecatcher / Pyrite Spellbomb —
///     the effect body performs the zone-move (Battlefield → Graveyard)
///     because <see cref="AdditionalCost.Sacrifice"/>.Pay is a stub.
///   - Target: 1..1 "target noncreature spell" <see cref="TargetRequest"/>.
///   - Resolution: at resolve time the target is gated by the
///     <see cref="CardType.Creature"/> filter (CR 608.2b — if it became a
///     creature spell, the counter does nothing). Otherwise the spell is
///     countered via <see cref="Fx.Counter"/> (CR 701.5). Mirrors
///     <see cref="NegateFactory"/>'s defensive-at-resolve posture.
///
/// ## Deferred (v1 gaps)
/// - <b>Activate the counter on opponents' spells only on the stack</b>:
///   no flash / timing gates required — Glen Elendra's activated ability is
///   instant-speed (no printed restriction). The opponent-restriction is
///   not present in the oracle (counter target noncreature spell — could
///   be your own).
/// - <b>Sacrifice payment</b>: same stub posture as Cursecatcher — the
///   effect body performs the zone-move directly.
/// </summary>
[CardName("Glen Elendra Archmage")]
public static class GlenElendraArchmageFactory
{
    public const string CardName = "Glen Elendra Archmage";
    public const string PrintedManaCost = "{1}{U}{U}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const string ActivationCost = "{U}";

    /// <summary>
    /// Construct Glen Elendra Archmage with no live stack — the counter
    /// activated ability is shape-only (no-op on resolution).
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, stack: null);

    /// <summary>
    /// Construct Glen Elendra Archmage with optional live stack. When
    /// <paramref name="stack"/> is supplied, the activated counter ability
    /// removes the target spell from the stack via <see cref="Fx.Counter"/>
    /// (CR 701.5). When null the counter is a no-op (shape-only).
    /// </summary>
    public static Creature Create(Player owner, Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Faerie, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Flying (CR 702.9) — marker; combat reads this directly.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Activated ability — "{U}, Sacrifice ~: Counter target noncreature
        // spell." CR 113.3b + CR 701.5 + CR 608.2b (illegal-target gate).
        // ----------------------------------------------------------------
        ActivatedAbility? ability = null;

        var counterEffect = new Effect(
            $"{CardName} — sacrifice self, then counter target noncreature spell",
            () =>
            {
                // ---- Sacrifice self ----
                if (card.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(card);
                    var sacOwner = card.Owner ?? owner;
                    sacOwner.Zones.Graveyard.AddCard(card);
                    card.SetZone(ZoneType.Graveyard);
                }

                // ---- Counter target noncreature spell ----
                if (ability == null) return;
                var chosen = ability.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not ISpell spell) return;
                if (stack == null) return;

                // CR 608.2b — illegal-on-resolution: if the chosen target
                // became a creature spell, the counter does nothing.
                if (spell.Card.HasType(CardType.Creature)) return;

                Fx.Counter(stack, spell);
            });

        ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationCost),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { counterEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target noncreature spell",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(ability);

        // ----------------------------------------------------------------
        // Persist (CR 702.79) — from the shared primitive.
        // ----------------------------------------------------------------
        PersistFactory.Build(card);

        return card;
    }
}
