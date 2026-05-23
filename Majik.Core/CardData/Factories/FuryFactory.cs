using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fury (Modern Horizons 2, {3}{R}).
///
/// Creature — Elemental Incarnation 3/3. Oracle text:
///   "Double strike
///    When this creature enters, it deals X damage divided as you choose
///    among any number of target creatures and/or planeswalkers, where X
///    is the number of cards in your hand.
///    Evoke—Exile a red card from your hand."
///
/// Pattern mirrors <see cref="SolitudeFactory"/> — Evoke alt-cost wired via
/// <see cref="Majik.Core.Costs.EvokeAlternativeCost"/>, evoke sacrifice
/// trigger wired via <see cref="EvokeFactory"/>, and the printed ETB
/// triggered ability is attached inline here.
///
/// ## Implemented (v1)
/// - 3/3 Elemental Incarnation with Double strike + Evoke keyword markers.
/// - Evoke alt-cost = "exile a red card from hand" (Solitude analogue).
/// - Evoke sacrifice trigger (CR 702.74b).
/// - ETB damage-distribution trigger: X = card count in controller's hand
///   at resolution time. A caller-supplied <c>distribute</c> Func receives
///   <c>(controller, X)</c> and returns the per-permanent allocation. The
///   default distribution (used when no Func is provided) deals all X to
///   the first chosen target — "acceptable degradation for v1" per the
///   ship plan; tests exercise both the explicit-Func path and the
///   default path.
///
/// ## Deferred (v1 gaps)
/// - <b>Real distribute-damage prompt</b>: CR 601.2d / CR 119.4 require the
///   caster to announce the damage assignment during target selection.
///   The engine has no agent-driven distribution prompt yet — the Func
///   is a stand-in until that ships.
/// - <b>Card-source threading on damage events</b>: emitting
///   <see cref="DamageDealtEvent"/> with a proper source card requires
///   plumbing the resolving permanent into the trigger effect — deferred
///   for parity with Solitude's lifelink wiring.
/// </summary>
public static class FuryFactory
{
    /// <summary>Construct Fury owned and controlled by <paramref name="owner"/>.
    /// When <paramref name="distribute"/> is null the ETB damage falls back to
    /// "all X to the first chosen target".</summary>
    public static Creature Create(
        Player owner,
        Func<Player, int, IReadOnlyDictionary<Permanent, int>>? distribute = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Fury",
            manaCost: "{3}{R}",
            power: 3,
            toughness: 3,
            subtypes: new[] { CardSubtype.Elemental, CardSubtype.Incarnation });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Keyword markers — CR 702.4 (Double strike), CR 702.74 (Evoke).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Double strike", card, owner));
        card.AddAbility(new KeywordAbility("Evoke", card, owner));

        // ----------------------------------------------------------------
        // Printed evoke sacrifice trigger (CR 702.74b).
        // ----------------------------------------------------------------
        card.AddAbility(EvokeFactory.Build(card));

        // ----------------------------------------------------------------
        // ETB damage-distribution trigger (CR 603.6a / CR 119).
        // X = controller's hand size at resolution time. Distribution
        // strategy is plumbed through `distribute` — see class docs.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var condition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var effect = new Effect(
            "Fury — deal X damage (X = cards in hand) divided among target creatures/planeswalkers",
            () =>
            {
                if (etbTrigger == null) return;
                var controller = card.Controller ?? card.Owner;
                if (controller == null) return;

                // X is the number of cards in the controller's hand at
                // resolution. Fury itself has already left the hand (it's
                // resolving on the stack / battlefield), so this read is
                // safe vs. "Fury counts itself" pitfalls.
                var x = controller.Zones.Hand.GetCards().Count();
                if (x <= 0) return;

                // Filter chosen targets to permanents still on the
                // battlefield (CR 608.2b — illegal targets are skipped at
                // resolution).
                var chosen = etbTrigger.ChosenTargets;
                var targets = new List<Permanent>();
                if (chosen.Count > 0)
                {
                    foreach (var raw in chosen[0])
                    {
                        if (raw is Permanent p
                            && p.Zone == ZoneType.Battlefield
                            && (p is Creature || p is Planeswalker))
                        {
                            targets.Add(p);
                        }
                    }
                }
                if (targets.Count == 0) return;

                // Either honour the caller-supplied distribution or fall
                // back to "all X to the first target" (documented v1
                // degradation).
                IReadOnlyDictionary<Permanent, int> allocation;
                if (distribute != null)
                {
                    allocation = distribute(controller, x) ?? new Dictionary<Permanent, int>();
                }
                else
                {
                    allocation = new Dictionary<Permanent, int> { [targets[0]] = x };
                }

                foreach (var (perm, amount) in allocation)
                {
                    if (amount <= 0) continue;
                    if (perm.Zone != ZoneType.Battlefield) continue; // illegal at resolution
                    switch (perm)
                    {
                        case Creature c:
                            c.TakeDamage(amount);
                            break;
                        case Planeswalker pw:
                            pw.RemoveLoyalty(amount);
                            break;
                    }
                }
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new[] { effect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any number of target creatures and/or planeswalkers",
                    MinTargets: 0,
                    MaxTargets: int.MaxValue,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);

        return card;
    }
}
