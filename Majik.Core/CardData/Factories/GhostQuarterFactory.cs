using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ghost Quarter (Dissension / reprints).
///
/// Land.
/// Oracle text:
///   "{T}: Add {C}.
///    {T}, Sacrifice Ghost Quarter: Destroy target land. Its controller
///    may search their library for a basic land card, put it onto the
///    battlefield, then shuffle."
///
/// ## Implemented (v1)
/// - Land identity (nonbasic, no printed supertype, no subtype).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{T}, Sacrifice Ghost Quarter: Destroy target land. Its controller
///   may search ...</b> — <see cref="ActivatedAbility"/> with
///   <see cref="AdditionalCost.Tap"/> + inline self-sacrifice (same shape
///   as <see cref="WastelandFactory"/> / <see cref="StripMineFactory"/>
///   since <see cref="AdditionalCost.Sacrifice"/> Pay() is still a no-op
///   stub).
/// - Target predicate is <c>any land</c> (basics are legal targets — only
///   Wasteland restricts to nonbasic).
/// - Resolution effect, in order:
///   <list type="number">
///     <item>Self-sacrifice → owner's graveyard (CR 701.16).</item>
///     <item>Snapshot the destroyed land's <b>controller</b> BEFORE moving
///       it (we need to know who searches after the destroy resolves;
///       CR 109.3 — once the card moves to the graveyard it is a new
///       object with no controller).</item>
///     <item>Validate target legality at resolution (CR 608.2b): still on
///       battlefield, still a Land, has an Owner. If illegal, skip the
///       destroy + search — Ghost Quarter is still sacrificed (the cost
///       was paid).</item>
///     <item>Destroy the land → owner's graveyard (CR 701.7b).</item>
///     <item>Ask the destroyed land's snapshotted controller's agent
///       (<see cref="IPlayerAgent.ChooseYesNoAsync"/> with
///       <see cref="BotIntent.Tutor"/>) whether to search. The default
///       heuristic accepts upside Tutor prompts (a free basic land is
///       strict upside) — preserves the legacy auto-accept posture for
///       agentless tests.</item>
///     <item>On yes: search the library for a basic land (CR 205.4a —
///       Basic supertype + Land card type), let the agent
///       (<see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) pick one,
///       move it via <see cref="ZoneServiceRegistry"/> so ETB-tapped
///       replacements + CardMovedEvent subscribers (Amulet of Vigor,
///       bounce-land ETB) fire, then shuffle. Mirrors
///       <see cref="PrismaticVistaFactory"/>'s <c>TutorBasicLand…</c>
///       helper.</item>
///   </list>
/// - <b>Instant speed</b>: no sorcery-speed rider on the oracle.
///
/// ## Deferred (v1 gaps)
/// - <b><see cref="AdditionalCost.Sacrifice"/></b> Pay() stub — sac
///   inlined in the effect (same posture as Wasteland / Strip Mine /
///   EE / Prismatic Vista).
/// - <b><see cref="Rules.ActionValidator"/> target legality</b> does not
///   restrict the agent's target list to Lands. Resolution-time guard
///   catches illegal picks (CR 608.2b).
/// - <b>Destroy-pipeline ZoneService</b>: raw zone move for the destroy
///   step (mirrors Wasteland). The basic-land tutor IS routed through
///   ZoneService so its ETB triggers fire.
/// </summary>
[CardName("Ghost Quarter")]
public static class GhostQuarterFactory
{
    public const string CardName = "Ghost Quarter";

    /// <summary>
    /// Construct Ghost Quarter owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {T}, Sacrifice Ghost Quarter: Destroy target land. Its
        // controller may search their library for a basic land card,
        // put it onto the battlefield, then shuffle.
        // ----------------------------------------------------------------
        ActivatedAbility? destroyAbility = null;
        var destroyEffect = new Effect(
            "Ghost Quarter: destroy target land; its controller may tutor a basic land",
            async ctx =>
            {
                if (destroyAbility == null) return;

                // Self-sacrifice first (CR 701.16) — the cost was declared
                // on activation, the engine's Sacrifice cost is still a
                // stub, so we move Ghost Quarter to its owner's graveyard
                // before the destroy step.
                SacrificeToOwnersGraveyard(land);

                if (destroyAbility.ChosenTargets.Count == 0) return;
                if (destroyAbility.ChosenTargets[0].Count == 0) return;

                var chosen = destroyAbility.ChosenTargets[0][0];
                if (chosen is not ICard card) return;
                if (!card.HasType(CardType.Land)) return;
                if (card.Owner == null) return;
                if (card.Zone != ZoneType.Battlefield) return;

                // Snapshot the destroyed land's controller BEFORE the
                // destroy step — once the card is in the graveyard it has
                // no controller (CR 109.3 / 110.2). The "its controller"
                // referent on the search rider is resolved here.
                var destroyedController = card.Controller ?? card.Owner;

                // Destroy → owner's graveyard (CR 701.7b).
                DestroyToOwnersGraveyard(card);

                if (destroyedController == null) return;

                // "May search" — consult the agent. Default heuristic
                // accepts Tutor-tagged prompts (free basic land is strict
                // upside), preserving the auto-accept posture for
                // agentless test paths.
                var agent = ctx.Agent ?? AgentRegistry.Get(destroyedController);
                if (agent != null)
                {
                    var yes = (await agent.ChooseYesNoAsync(
                        "Search your library for a basic land card?",
                        BotIntent.Tutor).ConfigureAwait(false));
                    if (!yes)
                    {
                        // CR 701.20a — even when the player declines the
                        // optional search, no shuffle occurs (the trigger
                        // text says "may search ... then shuffle" — the
                        // shuffle is conditional on the search happening).
                        return;
                    }
                }

                // The searcher is the destroyed land's controller (which may
                // be an opponent of the Ghost Quarter activator) — prompt
                // THAT player's agent, not ctx.Agent. Thread it through a
                // context bound to the destroyed-land controller.
                var searchCtx = ResolutionContext.For(
                    destroyedController,
                    agent ?? AgentRegistry.Get(destroyedController),
                    ctx.Game, chosenTargets: null, ctx.Ct);
                await TutorBasicLandToBattlefieldAsync(destroyedController, searchCtx)
                    .ConfigureAwait(false);
            });

        destroyAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { destroyEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(destroyAbility);

        return land;
    }

    /// <summary>
    /// Move <paramref name="self"/> from the battlefield to its owner's
    /// graveyard as the sacrifice payment (CR 701.16).
    /// </summary>
    private static void SacrificeToOwnersGraveyard(Land self)
    {
        var ownerOfSelf = self.Owner;
        if (ownerOfSelf == null) return;
        if (self.Zone != ZoneType.Battlefield) return;

        var holder = self.Controller ?? ownerOfSelf;
        holder.Zones.Battlefield.RemoveCard(self);
        ownerOfSelf.Zones.Graveyard.AddCard(self);
        self.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Move the destroyed target <paramref name="card"/> from the
    /// battlefield to its owner's graveyard (CR 701.7b).
    /// </summary>
    private static void DestroyToOwnersGraveyard(ICard card)
    {
        var ownerOfCard = card.Owner;
        if (ownerOfCard == null) return;

        var holder = card.Controller ?? ownerOfCard;
        holder.Zones.Battlefield.RemoveCard(card);
        ownerOfCard.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for a basic land card
    /// (CR 205.4a — Basic supertype + Land card type), consult the agent
    /// for the pick (falls back to the first deterministic match), move
    /// the chosen card to the battlefield untapped via
    /// <see cref="ZoneServiceRegistry"/> so ETB triggers + replacements
    /// fire, then shuffle (CR 701.20a). Mirrors
    /// <see cref="PrismaticVistaFactory"/>.
    /// </summary>
    private static async ValueTask TutorBasicLandToBattlefieldAsync(Player player, ResolutionContext ctx)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic))
            .ToList();

        // CR 701.19a — prompt agent even on zero candidates so the human
        // searcher sees the failed search (see LibrarySearch xmldoc).
        var pick = await Majik.Core.Zones.LibrarySearch.PromptOnlyAsync(
            ctx, player, candidates, "basic land card").ConfigureAwait(false);

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, player);
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(player);
            }
        }

        // CR 701.20a — shuffle whether or not a card was found.
        LibraryShuffle.ShuffleLibrary(player, "ghost-quarter");
    }
}
