using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for <b>The Underworld Cookbook</b> (Modern Horizons 3,
/// {1}). Distinct printing from the MH2 <see cref="UnderworldCookbookFactory"/>
/// — same flavour, different oracle text.
///
/// Artifact — Book. Oracle text (verified against Scryfall):
///   "{T}, Discard a card: Create a Food token. (It's an artifact with
///    "{2}, {T}, Sacrifice this token: You gain 3 life.")
///    {4}, {T}, Sacrifice this artifact: Return target creature card from
///    your graveyard to your hand."
///
/// The card's base shape (name, single Artifact card type, {1}) is
/// materialised from the embedded JSON definition
/// (<c>the-underworld-cookbook.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="RenegadeMapFactory"/>. The two activated abilities are layered
/// on here because the JSON schema doesn't express tap/sacrifice/discard
/// costs or token-creation effects.
///
/// ## Implemented (v1)
///
/// 1. <b>"{T}, Discard a card: Create a Food token."</b> —
///    <see cref="ActivatedAbility"/> (CR 602.1) with two NON-mana costs:
///    <list type="number">
///      <item><see cref="AdditionalCost.Tap"/> on this artifact (CR 602.1 —
///        the {T} symbol).</item>
///      <item><see cref="DiscardACardCost"/> — first-card-in-hand pick by
///        default (v1 picker policy shared with the MH2 Cookbook).</item>
///    </list>
///    Effect: <see cref="TokenFactory.CreateFood"/>. NOTE: unlike the MH2
///    Underworld Cookbook this printing has NO "then draw a card" rider —
///    the activation creates a single Food token and stops (CR 111.10 — the
///    Food token's own "{2}, {T}, Sacrifice this token: gain 3 life" is
///    stamped by <see cref="TokenFactory.CreateFood"/>).
///
/// 2. <b>"{4}, {T}, Sacrifice this artifact: Return target creature card
///    from your graveyard to your hand."</b> — <see cref="ActivatedAbility"/>
///    with three costs:
///    <list type="number">
///      <item><see cref="ManaCostCost"/> for {4}.</item>
///      <item><see cref="AdditionalCost.Tap"/> on this artifact ({T}).</item>
///      <item><see cref="AdditionalCost.Sacrifice"/> of this artifact itself
///        (CR 701.16) — distinct from the MH2 Cookbook's "Sacrifice a
///        Food".</item>
///    </list>
///    Effect: honour the activated ability's agent-set
///    <see cref="ActivatedAbility.ChosenTargets"/> (CR 602.2b) when present;
///    deterministic first-creature-in-graveyard fallback otherwise (same
///    posture as <see cref="UnderworldCookbookFactory"/> /
///    <see cref="EternalWitnessFactory"/>). Move Graveyard → Hand.
///
/// ## Deferred (v1 gaps)
/// - <b>Discard prompt</b>: <see cref="DiscardACardCost"/> uses its
///   deterministic first-card-in-hand picker — shared discard-prompt gap.
/// - <b>Target creature card prompt</b>: the resolve body reads any
///   agent-set target then falls back to the first creature card in the
///   controller's graveyard (shared first-match fallback). The ability
///   declares a 1..1 <see cref="TargetRequest"/> so production agents can
///   wire a real choice via <see cref="ActivatedAbility.SetChosenTargets"/>.
/// - <b>Food-token ETB / grave-return events</b>: the in-effect call sites
///   have no <see cref="ZoneService"/>, so the Food ETB and the grave→hand
///   return use direct-zone mutation (no CardMovedEvent) — same posture as
///   <see cref="UnderworldCookbookFactory"/>.
/// </summary>
[CardName(CardName)]
public static class TheUnderworldCookbookFactory
{
    public const string CardName = "The Underworld Cookbook";
    public const string Slug = "the-underworld-cookbook";

    /// <summary>Mana cost portion of the graveyard-return activation
    /// (CR 117.1).</summary>
    public const string GraveyardReturnManaCost = "{4}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct The Underworld Cookbook owned and controlled by
    /// <paramref name="owner"/>. Both activated abilities are attached
    /// structurally; the Food-token ETB and the grave→hand return use
    /// direct-zone mutation (no <see cref="ZoneService"/> wired in this
    /// shape-test posture).
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Artifact, {1}) from the embedded JSON definition.
        var card = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        AttachFoodCreationAbility(card, owner);
        AttachGraveyardReturnAbility(card, owner);

        return card;
    }

    /// <summary>
    /// Build + attach "{T}, Discard a card: Create a Food token." CR 602.1 —
    /// activated ability with two non-mana costs (tap this artifact + discard
    /// a card) creating a single Food token. No "then draw a card" rider on
    /// this printing.
    /// </summary>
    private static void AttachFoodCreationAbility(Artifact card, Player owner)
    {
        var foodEffect = new Effect(
            $"{CardName}: create a Food token",
            () =>
            {
                var controller = card.Controller ?? owner;
                // CR 111.10 — Food token shape (incl. its own
                // "{2},{T},Sac: gain 3 life") stamped by TokenFactory.
                TokenFactory.CreateFood(controller);
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(card),
                new DiscardACardCost(),
            },
            effects: new IEffect[] { foodEffect });

        card.AddAbility(ability);
    }

    /// <summary>
    /// Build + attach "{4}, {T}, Sacrifice this artifact: Return target
    /// creature card from your graveyard to your hand." CR 602.1 — activated
    /// ability composed of {4} + tap + sacrifice-self, with a graveyard →
    /// hand return that honours the ability's <see cref="TargetRequest"/>
    /// chosen at activation time (CR 602.2b).
    /// </summary>
    private static void AttachGraveyardReturnAbility(Artifact card, Player owner)
    {
        ActivatedAbility? ability = null;

        var returnEffect = new Effect(
            $"{CardName}: sacrifice ~ and return target creature card from your graveyard to your hand",
            () =>
            {
                var controller = card.Controller ?? owner;
                // CR 701.16 — pay the sacrifice as part of the effect body so
                // behaviour is observable even when the generic
                // AdditionalCost.Sacrifice payment is a no-op stub (same
                // posture as RenegadeMapFactory / ExpeditionMapFactory).
                SacrificeSelf(card, owner, controller);
                ResolveGraveyardReturn(controller, ability);
            });

        ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ManaCost.Parse(GraveyardReturnManaCost)),
                AdditionalCost.Tap(card),
                AdditionalCost.Sacrifice(card),
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
    /// CR 701.16 — move <paramref name="card"/> from the battlefield to its
    /// owner's graveyard. Idempotent. Mirrors the closure used by
    /// <see cref="RenegadeMapFactory"/>.
    /// </summary>
    private static void SacrificeSelf(Artifact card, Player owner, Player controller)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        controller.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Resolve "Return target creature card from your graveyard to your
    /// hand." Honours an agent-set <see cref="ActivatedAbility.ChosenTargets"/>
    /// when present; falls back to the first creature card in the
    /// controller's graveyard (single-arg dispatcher posture — mirrors
    /// <see cref="UnderworldCookbookFactory"/>'s fallback). Validates the
    /// chosen card is still a creature card in the controller's graveyard at
    /// resolution (CR 608.2b — clean no-op on fizzle).
    /// </summary>
    private static void ResolveGraveyardReturn(Player controller, ActivatedAbility? ability)
    {
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
        //    posture, matches the MH2 Cookbook's fallback).
        picked ??= controller.Zones.Graveyard.GetCards()
            .FirstOrDefault(c => c.HasType(CardType.Creature));

        // Empty graveyard / no creature card — clean no-op (CR 608.2b).
        if (picked == null) return;

        // CR 608.2b — target must still be a legal creature card in the
        // controller's graveyard at resolve time.
        if (picked.Zone != ZoneType.Graveyard) return;
        if (!picked.HasType(CardType.Creature)) return;
        if (!controller.Zones.Graveyard.GetCards().Contains(picked)) return;

        // Move Graveyard → Hand (direct-zone mutation; no ZoneService wired
        // in this construction path — same posture as the MH2 Cookbook).
        controller.Zones.Graveyard.RemoveCard(picked);
        controller.Zones.Hand.AddCard(picked);
        picked.SetZone(ZoneType.Hand);
    }
}
