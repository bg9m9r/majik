using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Izzet Staticaster (Return to Ravnica,
/// Creature — Human Wizard {1}{U}{R} 0/3).
///
/// Oracle text:
///   "Flash
///    {T}: Izzet Staticaster deals 1 damage to target creature and each
///    other creature with the same name as that creature."
///
/// ## Implemented (v1)
/// - 0/3 Human Wizard with mana cost {1}{U}{R}, owner/controller assigned.
/// - <b>Flash</b> (CR 702.8) — <see cref="KeywordAbility"/> marker.
///   <c>TimingRules.CanCastAtInstantSpeed</c> consumes this marker.
/// - <b>Activated ability {T}</b>: tap cost via
///   <see cref="AdditionalCost.Tap"/>. Declares a 1..1
///   <see cref="TargetRequest"/> for "target creature". On resolution:
///   <ol>
///     <li>Deal 1 damage to the chosen target creature.</li>
///     <li>Iterate ALL creatures reachable via
///         <paramref name="allCreaturesResolver"/> and deal 1 damage to
///         each creature whose <see cref="ICard.Name"/> equals the target's
///         name, excluding the already-damaged target ("each OTHER creature
///         with the same name" — CR 109.2 exact string match).</li>
///   </ol>
///   "Same name" = <c>creature.Name == target.Name</c> (exact string match,
///   case-sensitive; matches the Plague Engineer / Surgical Extraction name-
///   equality convention used elsewhere in the engine).
///   Damage is dispatched via <c>Creature.TakeDamage(1)</c>.
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — single-arg dispatcher path. Attaches the
///   tap ability to the card for shape tests / <see cref="NamedCardFactory"/>
///   dispatch. The name-sweep body is a no-op (no
///   <paramref name="allCreaturesResolver"/> wired) — only the primary
///   target takes damage.
/// - <see cref="Create(Player, Func{IReadOnlyList{Creature}})"/> — fully-
///   wired overload. Supplies the name-sweep iterator so all creatures on
///   all battlefields with the same name as the chosen target also take 1
///   damage.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent prompt for target legality</b>: the ActionValidator does not
///   filter candidates to creatures only at activation time; the resolution-
///   time guard catches non-creature picks (CR 608.2b).
/// - <b>Indestructible / damage prevention</b>: damage routing goes through
///   <c>TakeDamage</c> directly (same as other ping effects). Prevention
///   replacement effects (CR 615) are not wired at this call site.
/// </summary>
[CardName("Izzet Staticaster")]
public static class IzzetStaticasterFactory
{
    public const string CardName = "Izzet Staticaster";

    /// <summary>
    /// Construct Izzet Staticaster with no cross-creature name-sweep.
    /// The activated ability damages the chosen target only — suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, allCreaturesResolver: null);

    /// <summary>
    /// Construct a fully-wired Izzet Staticaster. When
    /// <paramref name="allCreaturesResolver"/> is non-null, the activated
    /// ability iterates every creature it returns and deals 1 damage to
    /// each whose name matches the chosen target's name (excluding the
    /// target itself — "each OTHER creature" per the oracle text).
    /// </summary>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Creature>>? allCreaturesResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: "{1}{U}{R}",
            power: 0,
            toughness: 3,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Flash — CR 702.8. Allows casting at instant speed.
        // TimingRules.CanCastAtInstantSpeed checks for this keyword.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // ----------------------------------------------------------------
        // {T}: Izzet Staticaster deals 1 damage to target creature and each
        // other creature with the same name as that creature.
        //
        // CR 602 — activated ability. Tap cost expressed via AdditionalCost.Tap.
        // CR 602.2b — TargetRequest declares the target at activation time.
        // Resolution reads ChosenTargets[0][0] for the primary target.
        // ----------------------------------------------------------------
        ActivatedAbility? pingAbility = null;

        var pingEffect = new Effect(
            "Izzet Staticaster: 1 damage to target creature and each other creature with the same name",
            () =>
            {
                if (pingAbility == null) return;
                if (pingAbility.ChosenTargets.Count == 0) return;
                if (pingAbility.ChosenTargets[0].Count == 0) return;

                var chosen = pingAbility.ChosenTargets[0][0];
                if (chosen is not Creature targetCreature) return;

                // Deal 1 damage to the primary target.
                targetCreature.TakeDamage(1);

                // CR 608.2b — "each other creature with the same name":
                // iterate every reachable creature; skip the primary target
                // (already damaged) and any creature whose name doesn't match.
                if (allCreaturesResolver == null) return;

                var allCreatures = allCreaturesResolver();
                foreach (var other in allCreatures)
                {
                    if (ReferenceEquals(other, targetCreature)) continue;
                    if (other.Name != targetCreature.Name) continue;
                    other.TakeDamage(1);
                }
            });

        pingAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { pingEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(pingAbility);

        return card;
    }
}
