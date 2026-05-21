using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Yawgmoth, Thran Physician (Dominaria, {2}{B}).
///
/// Legendary Creature — Phyrexian Human Cleric 2/4.
/// Oracle text:
///   "Protection from Humans
///    Pay 1 life, Sacrifice another creature: Each other player loses 1 life
///    and discards a card. Put a -1/-1 counter on up to one target creature.
///    You draw a card."
///
/// ## Implemented (v1)
/// - Legendary 2/4 Creature with Phyrexian, Human, Cleric subtypes
/// - Activated ability costs: Pay 1 life + Sacrifice another creature
///   (<see cref="AdditionalCost.PayLife"/> + <see cref="SacrificeAnotherCreatureCost"/>)
/// - Effect 1: Each other player loses 1 life
/// - Effect 2: Each other player discards a card (first card in hand,
///   deterministic — no agent prompt yet)
/// - Effect 4: Controller draws a card
///
/// ## Deferred (v1 gaps)
/// - <b>Protection from Humans</b>: no protection-from-subtype infrastructure
///   exists yet. Marked functional in the seed list but the keyword does not
///   affect gameplay.
/// - <b>Sacrifice target prompt</b>: <see cref="SacrificeAnotherCreatureCost.Target"/>
///   must be set by the agent; v1 falls back to the first eligible creature
///   on the battlefield (deterministic).
/// - <b>Effect 3 (-1/-1 counter on target)</b>: skipped entirely — requires
///   the ITarget / TargetResolver targeting system.
/// - <b>Each-other-player resolution</b>: Effects that iterate opponents
///   use a <see cref="Func{T}"/> resolver injected at factory time. When
///   called from <see cref="Majik.Core.CardData.NamedCardFactory"/> (test /
///   console path) the resolver is a no-op; production wiring must supply
///   the full player list via the <paramref name="opponentsResolver"/>
///   overload.
/// - <b>Discard — first non-land preference</b>: v1 picks the first card in
///   hand regardless of card type; full oracle-compliant discard is deferred.
/// </summary>
public static class YawgmothFactory
{
    /// <summary>
    /// Construct Yawgmoth with no opponent resolver (test / vanilla path).
    /// Opponent-targeting effects (lose life, discard) are no-ops in this mode.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, opponentsResolver: null);

    /// <summary>
    /// Construct Yawgmoth with a runtime opponent resolver so that the
    /// activated ability can iterate all other players.
    /// </summary>
    /// <param name="owner">Owner and initial controller of the card.</param>
    /// <param name="opponentsResolver">
    /// Called at ability resolution time to obtain the list of all players
    /// (including the controller). Pass the game's player collection here.
    /// May be null (falls back to empty — effects are silently skipped).
    /// </param>
    public static Creature Create(Player owner, Func<IReadOnlyList<Player>>? opponentsResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Yawgmoth, Thran Physician",
            manaCost: "{2}{B}",
            power: 2,
            toughness: 4,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Human, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Activated ability:
        //   Cost: Pay 1 life, Sacrifice another creature
        //   Effects:
        //     1. Each other player loses 1 life
        //     2. Each other player discards a card (first in hand — v1)
        //     3. Put a -1/-1 counter on up to one target creature (DEFERRED)
        //     4. Draw a card
        //
        // Opponent iteration deferred when opponentsResolver is null — see
        // class xmldoc.
        // ----------------------------------------------------------------

        var sacrificeCost = new SacrificeAnotherCreatureCost(card);

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.PayLife(1),
                sacrificeCost,
            },
            effects: new IEffect[]
            {
                // Effect 1: each other player loses 1 life (CR 119.3).
                new Effect(
                    "Yawgmoth: each other player loses 1 life",
                    () =>
                    {
                        var allPlayers = opponentsResolver?.Invoke();
                        if (allPlayers == null) return;
                        foreach (var p in allPlayers)
                        {
                            if (!ReferenceEquals(p, owner))
                                p.LoseLife(1);
                        }
                    }),

                // Effect 2: each other player discards a card.
                // v1: pick the first card in the opponent's hand
                // deterministically (no agent choice, no "first non-land"
                // preference). Full targeting deferred.
                new Effect(
                    "Yawgmoth: each other player discards a card",
                    () =>
                    {
                        var allPlayers = opponentsResolver?.Invoke();
                        if (allPlayers == null) return;
                        foreach (var p in allPlayers)
                        {
                            if (ReferenceEquals(p, owner)) continue;
                            var pick = p.Zones.Hand.GetCards().FirstOrDefault();
                            if (pick == null) continue;
                            p.Zones.Hand.RemoveCard(pick);
                            p.Zones.Graveyard.AddCard(pick);
                            pick.SetZone(ZoneType.Graveyard);
                        }
                    }),

                // Effect 3: put a -1/-1 counter on up to one target creature.
                // DEFERRED — requires ITarget / TargetResolver infrastructure.
                // See class xmldoc.

                // Effect 4: controller draws a card (CR 120.1).
                new Effect(
                    "Yawgmoth: you draw a card",
                    () =>
                    {
                        var top = owner.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null)
                        {
                            // CR 120.3: drawing from empty library is noted;
                            // SBA will handle loss at next opportunity.
                            owner.MarkTriedToDrawFromEmptyLibrary();
                            return;
                        }
                        owner.Zones.Library.RemoveCard(top);
                        owner.Zones.Hand.AddCard(top);
                        top.SetZone(ZoneType.Hand);
                    }),
            });

        card.AddAbility(ability);
        return card;
    }
}
