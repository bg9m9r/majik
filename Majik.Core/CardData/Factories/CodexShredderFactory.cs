using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for <b>Codex Shredder</b> (Return to Ravnica, {1}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "{T}: Target player mills a card. (They put the top card of their
///    library into their graveyard.)
///    {5}, {T}, Sacrifice this artifact: Return target card from your
///    graveyard to your hand."
///
/// The card's base shape (name, single Artifact card type, {1}) is
/// materialised from the embedded JSON definition (<c>codex-shredder.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="RenegadeMapFactory"/> / <see cref="TheUnderworldCookbookFactory"/>.
/// The two activated abilities are layered on here because the JSON schema
/// doesn't express tap / sacrifice / mana costs, mill, or graveyard recursion.
///
/// ## Implemented (v1)
///
/// 1. <b>"{T}: Target player mills a card."</b> — <see cref="ActivatedAbility"/>
///    (CR 602.1) with a single <see cref="AdditionalCost.Tap"/> cost (no mana
///    pip, no sacrifice — distinct from ability #2). Declares a 1..1
///    <see cref="TargetRequest"/> for "target player". On resolution the chosen
///    player mills ONE card (CR 701.13) via <see cref="MillAction.Apply"/>;
///    when no agent set a target the controller is the deterministic fallback
///    (same posture as <see cref="NihilSpellbombFactory"/>'s target-player
///    resolution).
///
/// 2. <b>"{5}, {T}, Sacrifice this artifact: Return target card from your
///    graveyard to your hand."</b> — <see cref="ActivatedAbility"/> with three
///    costs:
///    <list type="number">
///      <item><see cref="ManaCostCost"/> for {5}.</item>
///      <item><see cref="AdditionalCost.Tap"/> on this artifact ({T}).</item>
///      <item><see cref="AdditionalCost.Sacrifice"/> of this artifact itself
///        (CR 701.16).</item>
///    </list>
///    Unlike <see cref="TheUnderworldCookbookFactory"/>'s creature-only return,
///    Codex Shredder returns ANY card type. Resolution honours the ability's
///    agent-set <see cref="ActivatedAbility.ChosenTargets"/> (CR 602.2b) when
///    present; deterministic first-card-in-graveyard fallback otherwise (same
///    posture as <see cref="TheUnderworldCookbookFactory"/>). Move
///    Graveyard → Hand.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is a no-op stub, so the
///   effect closure performs the Battlefield → Graveyard move directly (same
///   posture as <see cref="RenegadeMapFactory"/> / <see cref="TheUnderworldCookbookFactory"/>).
/// - <b>Target prompts / events</b>: the mill target falls back to the
///   controller and the return target to the first graveyard card when no
///   agent is wired (single-arg dispatcher posture). Grave→hand return uses
///   direct-zone mutation (no CardMovedEvent) — shared gap.
/// </summary>
[CardName(CardName)]
public static class CodexShredderFactory
{
    public const string CardName = "Codex Shredder";
    public const string Slug = "codex-shredder";

    /// <summary>CR 701.13 — "mills a card" is mill ONE.</summary>
    public const int MillCount = 1;

    /// <summary>Mana cost portion of the graveyard-return activation
    /// (CR 117.1).</summary>
    public const string GraveyardReturnManaCost = "{5}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Codex Shredder owned and controlled by
    /// <paramref name="owner"/>. Both activated abilities are attached
    /// structurally; the grave→hand return uses direct-zone mutation
    /// (no <see cref="ZoneService"/> wired in this shape posture — same as
    /// <see cref="TheUnderworldCookbookFactory"/>).
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Artifact, {1}) from the embedded JSON definition.
        var card = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        AttachMillAbility(card, owner);
        AttachGraveyardReturnAbility(card, owner);

        return card;
    }

    /// <summary>
    /// Build + attach "{T}: Target player mills a card." CR 602.1 — activated
    /// ability whose only cost is tapping this artifact (no mana, no
    /// sacrifice). A 1..1 "target player" <see cref="TargetRequest"/> is
    /// declared; on resolution the chosen player mills ONE card (CR 701.13)
    /// with the controller as deterministic fallback when no agent is wired.
    /// </summary>
    private static void AttachMillAbility(Artifact card, Player owner)
    {
        ActivatedAbility? ability = null;

        var millEffect = new Effect(
            $"{CardName}: target player mills {MillCount} (CR 701.13)",
            () =>
            {
                var controller = card.Controller ?? owner;

                // Resolve the target player from ChosenTargets (production
                // path); fall back to the controller (single-arg dispatcher
                // posture — mirrors NihilSpellbombFactory).
                var targetPlayer = controller;
                if (ability != null
                    && ability.ChosenTargets.Count > 0
                    && ability.ChosenTargets[0].Count > 0
                    && ability.ChosenTargets[0][0] is Player chosen)
                {
                    targetPlayer = chosen;
                }

                // CR 701.13 — mill = move top N of library to graveyard.
                MillAction.Apply(targetPlayer, MillCount);
            });

        ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { millEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Mill),
            });

        card.AddAbility(ability);
    }

    /// <summary>
    /// Build + attach "{5}, {T}, Sacrifice this artifact: Return target card
    /// from your graveyard to your hand." CR 602.1 — activated ability composed
    /// of {5} + tap + sacrifice-self, with a graveyard → hand return that
    /// honours the ability's <see cref="TargetRequest"/> chosen at activation
    /// time (CR 602.2b). Unlike The Underworld Cookbook, ANY card type is a
    /// legal target.
    /// </summary>
    private static void AttachGraveyardReturnAbility(Artifact card, Player owner)
    {
        ActivatedAbility? ability = null;

        var returnEffect = new Effect(
            $"{CardName}: sacrifice ~ and return target card from your graveyard to your hand",
            () =>
            {
                var controller = card.Controller ?? owner;
                // CR 701.16 — pay the sacrifice as part of the effect body so
                // behaviour is observable even when the generic
                // AdditionalCost.Sacrifice payment is a no-op stub (same
                // posture as RenegadeMapFactory / TheUnderworldCookbookFactory).
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
                    Description: "target card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: owner.Zones.Graveyard.GetCards()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(ability);
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="card"/> from the battlefield to its
    /// owner's graveyard. Idempotent. Mirrors the closure used by
    /// <see cref="RenegadeMapFactory"/> / <see cref="TheUnderworldCookbookFactory"/>.
    /// </summary>
    private static void SacrificeSelf(Artifact card, Player owner, Player controller)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        controller.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Resolve "Return target card from your graveyard to your hand." Honours
    /// an agent-set <see cref="ActivatedAbility.ChosenTargets"/> when present;
    /// falls back to the first card in the controller's graveyard (single-arg
    /// dispatcher posture). Validates the chosen card is still in the
    /// controller's graveyard at resolution (CR 608.2b — clean no-op on
    /// fizzle). Excludes the just-sacrificed Codex Shredder itself from the
    /// fallback so the artifact doesn't return itself.
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

        // 2) Deterministic fallback — first card in the controller's
        //    graveyard, skipping the just-sacrificed Codex Shredder so the
        //    artifact doesn't recur itself (no-agent / single-arg posture).
        picked ??= controller.Zones.Graveyard.GetCards()
            .FirstOrDefault(c => !ReferenceEquals(c, ability?.Source));

        // Empty graveyard — clean no-op (CR 608.2b).
        if (picked == null) return;

        // CR 608.2b — target must still be a legal card in the controller's
        // graveyard at resolve time.
        if (picked.Zone != ZoneType.Graveyard) return;
        if (!controller.Zones.Graveyard.GetCards().Contains(picked)) return;

        // Move Graveyard → Hand (direct-zone mutation; no ZoneService wired in
        // this construction path — same posture as The Underworld Cookbook).
        controller.Zones.Graveyard.RemoveCard(picked);
        controller.Zones.Hand.AddCard(picked);
        picked.SetZone(ZoneType.Hand);
    }
}
