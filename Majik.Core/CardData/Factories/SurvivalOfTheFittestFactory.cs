using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Survival of the Fittest (Exodus, {1}{G}).
///
/// Enchantment. Oracle text:
///   "{G}, Discard a creature card: Search your library for a creature
///    card, reveal it, put it into your hand, then shuffle."
///
/// ## Why it gets its own factory
/// Survival of the Fittest is a battlefield-resident creature tutor — the
/// effect mirrors <see cref="WorldlyTutorFactory"/>'s green-creature
/// predicate but the tutor is gated behind a composite activated-ability
/// cost ({G} + discard a creature card) and is repeatable as long as the
/// enchantment is on the battlefield and the controller can pay. The
/// shape doesn't fit any existing template binder (template binders
/// produce spell-level tutors, not activated abilities), so it lives as
/// its own named factory.
///
/// ## Implemented (v1)
/// - Enchantment shape, mana cost {1}{G}, owner/controller.
/// - Activated ability — "{G}, Discard a creature card: Search your
///   library for a creature card, put it into your hand, then shuffle."
///   Costs are <see cref="ManaCostCost"/>({G}) +
///   <see cref="DiscardACreatureCardCost"/>; effect prompts the agent
///   via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> for a
///   creature card in the controller's library, moves the pick to hand,
///   then shuffles (CR 701.19a / CR 701.20a). Empty candidate list or
///   null pick = no-op (CR 701.19a permits declining to find).
/// - Activation gating: the ability surfaces on the card's
///   <see cref="Card.Abilities"/> list; the live activation pipeline
///   (<see cref="Majik.Core.Services.AbilityActivator"/>) enforces
///   battlefield-only activation (CR 113.6 / CR 602.5b) and cost
///   payability before the effect fires.
///
/// ## Deferred (v1 gaps)
/// - <b>Discard prompt</b>. <see cref="DiscardACreatureCardCost"/> picks
///   the first creature card in hand deterministically when no
///   <see cref="DiscardACreatureCardCost.Target"/> is nominated. Same
///   queue as <see cref="DiscardACardCost"/>'s deferred discard-target
///   prompt (Faithless Looting / Liliana / Connive).
/// - <b>Reveal event</b>. The picked creature moves Library → Hand
///   without publishing a reveal event; same gap as the other search
///   factories.
/// </summary>
[CardName("Survival of the Fittest")]
public static class SurvivalOfTheFittestFactory
{
    public const string CardName = "Survival of the Fittest";
    public const string PrintedManaCost = "{1}{G}";

    /// <summary>
    /// Activation mana-cost piece of the printed "{G}, Discard a
    /// creature card:" composite cost. Distinct from the enchantment's
    /// own printed mana cost ({1}{G}).
    /// </summary>
    public const string ActivationManaCost = "{G}";

    /// <summary>
    /// Build a Survival of the Fittest enchantment owned by
    /// <paramref name="owner"/> with the activated tutor ability wired.
    /// </summary>
    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ------------------------------------------------------------
        // Activated ability — "{G}, Discard a creature card: Search
        // your library for a creature card, reveal it, put it into your
        // hand, then shuffle." CR 602 (activated ability) + CR 701.19a
        // (search) + CR 701.20a (post-search shuffle).
        // ------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: tutor creature -> hand",
            () =>
            {
                // CR 113.6 — ability only functions while on the
                // battlefield. Belt-and-braces guard inside the effect
                // body; the activation pipeline already gates on this.
                if (card.Zone != ZoneType.Battlefield) return;

                static bool Pred(ICard c) => c.HasType(CardType.Creature);

                var caster = card.Controller ?? owner;
                var candidates = caster.Zones.Library.GetCards().Where(Pred).ToList();
                if (candidates.Count == 0)
                {
                    // No candidates — still shuffle per CR 701.19a /
                    // 701.20a (the player chose to search and found
                    // nothing; library still gets shuffled).
                    LibraryShuffle.ShuffleLibrary(caster, "survival-of-the-fittest");
                    return;
                }

                var agent = AgentRegistry.Get(caster);
                ICard? pick = agent != null
                    ? agent.ChooseLibraryPickAsync(
                        ctx: null,
                        candidates,
                        "creature card")
                        .GetAwaiter().GetResult()
                    : candidates[0];

                if (pick != null)
                {
                    caster.Zones.Library.RemoveCard(pick);
                    caster.Zones.Hand.AddCard(pick);
                    pick.SetZone(ZoneType.Hand);
                }
                // CR 701.20a — shuffle whether the search found a card
                // or not.
                LibraryShuffle.ShuffleLibrary(caster, "survival-of-the-fittest");
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ManaCost.Parse(ActivationManaCost)),
                new DiscardACreatureCardCost(),
            },
            effects: new IEffect[] { tutorEffect });

        card.AddAbility(ability);

        return card;
    }
}
