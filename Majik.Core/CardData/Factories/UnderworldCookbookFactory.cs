using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Underworld Cookbook (Modern Horizons 2, {1}).
///
/// Artifact. Oracle text:
///   "{1}, Discard a card: Create a Food token. Then draw a card.
///    {2}, Sacrifice a Food: Return target creature card from your
///    graveyard to your hand."
///
/// ## Implemented (v1)
///
/// Two activated abilities on a {1} artifact:
///
/// 1. <b>"{1}, Discard a card: Create a Food token. Then draw a card."</b> —
///    <see cref="ActivatedAbility"/> (CR 602.1) with two costs:
///    <list type="number">
///      <item><see cref="ManaCostCost"/> for {1}.</item>
///      <item><see cref="DiscardACardCost"/> — first-card-in-hand pick by
///        default (matches v1 picker policy of
///        <see cref="InsolentNeonateFactory"/>).</item>
///    </list>
///    Effect: <see cref="TokenFactory.CreateFood"/> + <see cref="Fx.DrawCards"/>(1).
///    Per Modern's Cookbook combo lines, the draw is a single top-of-library
///    draw (CR 121.1).
///
/// 2. <b>"{2}, Sacrifice a Food: Return target creature card from your
///    graveyard to your hand."</b> — <see cref="ActivatedAbility"/> with two
///    costs:
///    <list type="number">
///      <item><see cref="ManaCostCost"/> for {2}.</item>
///      <item><see cref="SacrificeAFoodCost"/> — picks the first Food the
///        controller controls (deterministic v1 picker, same posture as
///        <see cref="SacrificeAnArtifactCost"/>).</item>
///    </list>
///    Effect: pick a creature card in the controller's graveyard
///    (agent-driven via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>'s
///    sibling <see cref="IPlayerAgent.ChooseFromHandAsync"/>-style
///    candidate-prompt — for v1 we use the deterministic
///    first-match fallback shared by
///    <see cref="EternalWitnessFactory"/> / <see cref="WishclawTalismanFactory"/>).
///    Move Graveyard → Hand via <see cref="ZoneService.MoveCard"/> when
///    supplied so CardMovedEvent fires (CR 603.6a / CR 701.20); otherwise
///    direct-zone mutation.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Discard prompt</b>: the embedded <see cref="DiscardACardCost"/>
///   uses its deterministic first-card-in-hand picker. Real agent-driven
///   discard selection is the shared gap.
/// - <b>Sacrifice-Food target prompt</b>: the embedded
///   <see cref="SacrificeAFoodCost"/> picks the first Food deterministically.
///   Agent-driven sacrifice selection is the shared gap.
/// - <b>"Target creature card from your graveyard"</b> — at v1 the resolve
///   body reads the activated ability's chosen targets and falls back to
///   the first creature card in the controller's graveyard (same
///   first-match fallback used by <see cref="EternalWitnessFactory"/>).
///   The ability declares a 1..1 <see cref="TargetRequest"/> so production
///   agents can wire a real target choice via
///   <see cref="ActivatedAbility.SetChosenTargets"/>.
/// </summary>
[CardName("Underworld Cookbook")]
public static class UnderworldCookbookFactory
{
    public const string CardName = "Underworld Cookbook";
    public const string PrintedManaCost = "{1}";

    /// <summary>Mana cost portion of the food-creation activation (CR 117.1).</summary>
    public const string FoodCreationManaCost = "{1}";

    /// <summary>Mana cost portion of the graveyard-return activation
    /// (CR 117.1).</summary>
    public const string GraveyardReturnManaCost = "{2}";

    /// <summary>
    /// Construct Underworld Cookbook with no live wiring. Food token ETB
    /// publishes no CardMovedEvent (the in-effect call site doesn't have a
    /// ZoneService) and the graveyard-return uses direct-zone mutation.
    /// Suitable for shape / activation tests.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, zoneService: null);

    /// <summary>
    /// Construct Underworld Cookbook. When <paramref name="zoneService"/>
    /// is supplied:
    ///   - Created Food tokens enter the battlefield via
    ///     <see cref="ZoneService.MoveCard"/> so downstream subscribers
    ///     observe the ETB (CR 603.6a / CR 701.20).
    ///   - The grave-to-hand return for ability #2 routes through
    ///     <see cref="ZoneService.MoveCard"/> for the same reason.
    /// </summary>
    public static Artifact Create(Player owner, ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        AttachFoodCreationAbility(card, owner, zoneService);
        AttachGraveyardReturnAbility(card, owner, zoneService);

        return card;
    }

    /// <summary>
    /// Build + attach the "{1}, Discard a card: Create a Food token, then
    /// draw a card." activated ability. CR 602.1 — activated ability
    /// composed of <see cref="ManaCostCost"/>({1}) +
    /// <see cref="DiscardACardCost"/> with a token-creation +
    /// single-card-draw effect.
    /// </summary>
    private static void AttachFoodCreationAbility(
        Artifact card,
        Player owner,
        ZoneService? zoneService)
    {
        var foodEffect = new Effect(
            $"{CardName}: create a Food token, then draw a card",
            () =>
            {
                var controller = card.Controller ?? owner;
                // CR 111.10 — Food token shape stamped by TokenFactory.
                TokenFactory.CreateFood(controller, zoneService);
                // CR 121.1 — "then draw a card" is sequenced after the
                // token creation (oracle uses "then"); resolves to a
                // single top-of-library draw (CR 120 / CR 614 replacement
                // bus routing handled inside Fx.DrawCards).
                Fx.DrawCards(controller, 1);
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ManaCost.Parse(FoodCreationManaCost)),
                new DiscardACardCost(),
            },
            effects: new IEffect[] { foodEffect });

        card.AddAbility(ability);
    }

    /// <summary>
    /// Build + attach the "{2}, Sacrifice a Food: Return target creature
    /// card from your graveyard to your hand." activated ability. CR 602.1
    /// — activated ability composed of <see cref="ManaCostCost"/>({2}) +
    /// <see cref="SacrificeAFoodCost"/> with a graveyard → hand return
    /// effect that honours the activated ability's <see cref="TargetRequest"/>
    /// chosen at activation time (CR 602.2b).
    /// </summary>
    private static void AttachGraveyardReturnAbility(
        Artifact card,
        Player owner,
        ZoneService? zoneService)
    {
        ActivatedAbility? ability = null;

        var returnEffect = new Effect(
            $"{CardName}: return target creature card from your graveyard to your hand",
            () => ResolveGraveyardReturn(card, owner, ability, zoneService));

        ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ManaCost.Parse(GraveyardReturnManaCost)),
                new SacrificeAFoodCost(),
            },
            effects: new IEffect[] { returnEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: owner.Zones.Graveyard.GetCards()
                        .Where(c => c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(ability);
    }

    /// <summary>
    /// Shared resolve body for the second activated ability. Honours an
    /// agent-set <see cref="ActivatedAbility.ChosenTargets"/> when present;
    /// falls back to the first creature card in the controller's graveyard
    /// (single-arg dispatcher posture — mirrors <see cref="EternalWitnessFactory"/>'s
    /// first-card fallback). Validates the chosen card is still a creature
    /// card in the controller's graveyard at resolution (CR 608.2b — clean
    /// no-op on fizzle).
    /// </summary>
    private static void ResolveGraveyardReturn(
        Artifact source,
        Player owner,
        ActivatedAbility? ability,
        ZoneService? zoneService)
    {
        // CR 110.2 — "your graveyard" resolves to the source's controller.
        var controller = source.Controller ?? owner;

        ICard? picked = null;

        // 1) Honour the agent-set target (production path).
        if (ability != null
            && ability.ChosenTargets.Count > 0
            && ability.ChosenTargets[0].Count > 0
            && ability.ChosenTargets[0][0] is ICard chosen)
        {
            picked = chosen;
        }

        // 2) Deterministic fallback — first creature card in the
        //    controller's graveyard (no-agent / single-arg dispatcher
        //    posture, matches Eternal Witness's fallback).
        picked ??= controller.Zones.Graveyard.GetCards()
            .FirstOrDefault(c => c.HasType(CardType.Creature));

        // Empty graveyard / no creature card — clean no-op (CR 608.2b).
        if (picked == null) return;

        // CR 608.2b — target must still be a legal creature card in the
        // controller's graveyard at resolve time.
        if (picked.Zone != ZoneType.Graveyard) return;
        if (!picked.HasType(CardType.Creature)) return;
        if (!controller.Zones.Graveyard.GetCards().Contains(picked)) return;

        // Move Graveyard → Hand. ZoneService path publishes CardMovedEvent
        // so any "leaves graveyard" / "returned to hand" triggers fire.
        if (zoneService != null)
        {
            zoneService.MoveCard(picked, ZoneType.Graveyard, ZoneType.Hand, controller);
        }
        else
        {
            controller.Zones.Graveyard.RemoveCard(picked);
            controller.Zones.Hand.AddCard(picked);
            picked.SetZone(ZoneType.Hand);
        }
    }

    /// <summary>
    /// "Sacrifice a Food" — activated-ability cost (CR 117 / CR 701.20)
    /// requiring the controller to sacrifice a Food artifact they control.
    /// Sister shape to <see cref="SacrificeAnArtifactCost"/> with an
    /// additional <see cref="CardSubtype.Food"/> subtype filter (CR 205.3).
    ///
    /// Implements <see cref="ICost"/> so it slots into an
    /// <see cref="ActivatedAbility"/> cost list. v1 picker chooses the
    /// first eligible Food deterministically (same posture as the rest of
    /// the sacrifice-cost surface).
    /// </summary>
    public sealed class SacrificeAFoodCost : ICost
    {
        /// <summary>Optionally set by the agent to nominate which Food
        /// to sacrifice. When null the first eligible Food is chosen
        /// deterministically (v1 picker policy).</summary>
        public Permanent? Target { get; set; }

        /// <summary>The Food actually sacrificed after a successful
        /// <see cref="Pay"/>. Null before payment.</summary>
        public Permanent? Sacrificed { get; private set; }

        /// <inheritdoc/>
        public string Description => "sacrifice a Food";

        /// <inheritdoc/>
        public bool CanPay(Player player)
        {
            if (player == null) return false;
            if (Target != null)
            {
                return ReferenceEquals(Target.Controller, player)
                    && Target.Zone == ZoneType.Battlefield
                    && Target.HasType(CardType.Artifact)
                    && Target.HasSubtype(CardSubtype.Food);
            }
            return player.Zones.Battlefield.GetCards()
                .OfType<Permanent>()
                .Any(p => p.HasType(CardType.Artifact)
                       && p.HasSubtype(CardSubtype.Food));
        }

        /// <inheritdoc/>
        public void Pay(Player player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            var pick = Target ?? player.Zones.Battlefield.GetCards()
                .OfType<Permanent>()
                .FirstOrDefault(p => p.HasType(CardType.Artifact)
                                  && p.HasSubtype(CardSubtype.Food));

            if (pick == null)
                throw new InvalidPlayerActionException(
                    $"Cannot pay {Description}: no Food to sacrifice.");

            player.Zones.Battlefield.RemoveCard(pick);
            player.Zones.Graveyard.AddCard(pick);
            pick.SetZone(ZoneType.Graveyard);
            Sacrificed = pick;
        }
    }
}
