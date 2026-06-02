using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Steelshaper Apprentice (Fifth Dawn, {2}{W}{W}).
///
/// Creature — Human Soldier 1/3. Oracle text (verified against Scryfall):
///   "{W}, {T}, Return this creature to its owner's hand: Search your library
///    for an Equipment card, reveal that card, put it into your hand, then
///    shuffle."
///
/// ## How it is composed
/// Steelshaper Apprentice is a creature-bodied version of the
/// <see cref="SteelshapersGiftFactory"/> Equipment tutor: the same
/// search-for-an-Equipment-to-hand effect (CR 205.3g — Equipment is an
/// artifact SUBTYPE, so the predicate is a subtype filter mirroring Stoneforge
/// Mystic) is the resolve body of an <see cref="ActivatedAbility"/> (CR 602.1)
/// rather than a sorcery's on-resolve effect.
///
/// The activation cost has three components, all standard
/// <see cref="ActivatedAbility"/> cost primitives:
///  - <b>{W}</b> — a <see cref="ManaCostCost"/>.
///  - <b>{T}</b> — <see cref="AdditionalCost.Tap"/>(this creature). The
///    central tap-cost gate (CR 302.6 / 605.3a) refuses payment while the
///    creature is summoning-sick (no haste) or already tapped.
///  - <b>Return this creature to its owner's hand</b> — a
///    <see cref="ReturnSelfToHandCost"/> (CR 118 — non-mana cost), the
///    self-targeting hand analogue of <see cref="SacrificeSelfCost"/>. Modelled
///    inline as an <see cref="ICost"/> so the engine's generic cost-legality
///    check (CR 601/602) refuses the activation when the creature is no longer
///    on the battlefield, and so the bounce happens as a cost (CR 601.2h /
///    602.1) — before the ability resolves — never an effect.
///
/// The base shape (name, Creature, Human Soldier subtypes, {2}{W}{W}, 1/3) is
/// materialised from the embedded JSON definition
/// (<c>steelshaper-apprentice.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="WirewoodSymbioteFactory"/>); the activated ability is layered on
/// here because the JSON <c>AbilityDefinition</c> schema doesn't express a
/// return-self cost or a library search.
///
/// ## Implemented (v1)
/// - 1/3 Human Soldier with mana cost {2}{W}{W}, owner/controller wired.
/// - Activated ability with cost { {W}, {T}, return self to hand } and a
///   resolve effect that pre-filters the controller's library to cards whose
///   subtypes include <see cref="CardSubtype.Equipment"/> (CR 205.3g), prompts
///   the controller's agent (via <see cref="LibrarySearch.PromptOnlyAsync"/>)
///   for a pick, and moves the pick Library → Hand. No agent registered = the
///   deterministic first-match fallback the rest of the search factories use.
///   Null pick = no-op (CR 701.19a permits declining to find).
/// - CR 701.20a — the library is shuffled after the search via the shared
///   <see cref="LibraryShuffle"/> helper (publishes a <c>LibraryShuffledEvent</c>
///   when an EventBus is registered), whether or not a card was found.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>. The picked Equipment moves Library → Hand without
///   publishing a reveal event; same gap as <see cref="SteelshapersGiftFactory"/>
///   / <see cref="StoneforgeMysticFactory"/> and the other library-tutor
///   factories.
/// </summary>
[CardName("Steelshaper Apprentice")]
public static class SteelshaperApprenticeFactory
{
    public const string CardName = "Steelshaper Apprentice";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "steelshaper-apprentice";

    /// <summary>
    /// Dispatcher path (used by <see cref="NamedCardFactory"/>). Materialises
    /// the 1/3 Human Soldier from the embedded JSON and layers on the
    /// { {W}, {T}, return self to hand } : tutor-an-Equipment-to-hand ability.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Creature, Human Soldier, {2}{W}{W}, 1/3) from the
        // embedded JSON definition. The JSON carries no abilities — the
        // activated ability is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        card.SetController(owner);

        // CR 602.1 — the activated ability. Cost = { {W}, {T}, return this
        // creature to its owner's hand }; effect = search library for an
        // Equipment card, put it in hand, then shuffle. NOT a mana ability
        // (CR 605.1 — produces no mana).
        var tutorEffect = new Effect(
            $"{CardName}: search library for an Equipment card -> hand, then shuffle",
            async ctx =>
            {
                // CR 205.3g — "Equipment" is an artifact SUBTYPE; match by
                // subtype, not card type (mirrors Steelshaper's Gift /
                // Stoneforge Mystic).
                static bool Pred(ICard c) => c.HasSubtype(CardSubtype.Equipment);

                var candidates = owner.Zones.Library.GetCards().Where(Pred).ToList();

                // CR 701.19a — prompt the agent (even on an empty candidate
                // list, so a human searcher SEES the failed search). Returning
                // null = decline to find, which is legal. No agent registered =
                // deterministic first-match fallback (shape / dispatcher path).
                var pick = await LibrarySearch.PromptOnlyAsync(
                    ResolutionContext.For(
                        owner, ctx.Agent ?? AgentRegistry.Get(owner),
                        game: ctx.Game, chosenTargets: null, ctx.Ct),
                    owner, candidates, "Equipment card")
                    .ConfigureAwait(false);
                if (pick != null)
                {
                    owner.Zones.Library.RemoveCard(pick);
                    owner.Zones.Hand.AddCard(pick);
                    pick.SetZone(ZoneType.Hand);
                }

                // CR 701.20a — shuffle after the search effect, regardless of
                // whether a card was actually found.
                LibraryShuffle.ShuffleLibrary(owner, Slug);
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{W}"),
                AdditionalCost.Tap(card),
                new ReturnSelfToHandCost(card),
            },
            effects: new IEffect[] { tutorEffect });

        card.AddAbility(ability);

        return card;
    }
}

/// <summary>
/// "Return [this permanent] to its owner's hand." A self-targeting
/// return-to-hand activation cost (CR 118 — a non-mana cost), the hand analogue
/// of <see cref="SacrificeSelfCost"/>. Used by Steelshaper Apprentice's
/// "{W}, {T}, Return this creature to its owner's hand:" ability.
///
/// Implements <see cref="ICost"/> so it slots directly into an
/// <see cref="ActivatedAbility"/> cost list alongside the mana ({W}) and tap
/// ({T}) costs. Because the bounce is a cost, it is paid before the ability
/// resolves (CR 601.2h / 602.1) — the creature is in its owner's hand by the
/// time the search effect runs.
///
/// Activation zone: Battlefield. The cost cannot be paid if the permanent is
/// not currently on the battlefield (CR 118 — you can only pay a cost you can
/// actually perform).
/// </summary>
public sealed class ReturnSelfToHandCost : ICost
{
    private readonly Permanent _self;

    public ReturnSelfToHandCost(Permanent self)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
    }

    /// <summary>The bounced permanent — same reference passed at construction.
    /// Exposed for tests / effects that need to read the source after payment.
    /// </summary>
    public Permanent Self => _self;

    /// <inheritdoc/>
    public string Description => $"Return {_self.Name} to its owner's hand";

    /// <inheritdoc/>
    /// <remarks>
    /// The permanent must be on the battlefield. Control is what governs who
    /// may pay (CR 118), so the activating player must control the permanent.
    /// </remarks>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return ReferenceEquals(_self.Controller, player)
               && _self.Zone == ZoneType.Battlefield
               && player.Zones.Battlefield.ContainsCard(_self);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Moves the permanent from its controller's battlefield to its OWNER's
    /// hand (CR 701.10 — "return to hand" always goes to the owner's hand, even
    /// for a stolen permanent whose Controller differs from its Owner). Routes
    /// through the registered <see cref="ZoneService"/> when one exists so LTB
    /// / CardMovedEvent fire; falls back to raw zone manipulation otherwise
    /// (unit-test shape with no live game).
    /// </remarks>
    public void Pay(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!CanPay(player))
            throw new Majik.Core.Domain.Exceptions.InvalidPlayerActionException(
                $"Cannot pay {Description}: {_self.Name} is not on "
                + $"{player.Name}'s battlefield.");

        // CR 701.10 — returned permanents go to their OWNER's hand. Route
        // through the owner so this behaves correctly when the activating
        // player has stolen the permanent (Controller is the caster, Owner
        // stays put).
        var owner = _self.Owner ?? player;
        var holder = _self.Controller ?? owner;

        var zones = ZoneServiceRegistry.Get(holder);
        if (zones != null)
        {
            zones.MoveCard(_self, ZoneType.Battlefield, ZoneType.Hand, owner);
        }
        else
        {
            holder.Zones.Battlefield.RemoveCard(_self);
            owner.Zones.Hand.AddCard(_self);
            _self.SetZone(ZoneType.Hand);
        }
    }
}
