using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Escape Tunnel — Murders at Karlov Manor land.
///
/// Oracle text (verified against Scryfall):
///   "{T}, Sacrifice this land: Search your library for a basic land card, put
///    it onto the battlefield tapped, then shuffle.
///    {T}, Sacrifice this land: Target creature with power 2 or less can't be
///    blocked this turn."
///
/// ## Build path
///
/// Identity (a nonbasic Land with no supertype/subtype, producing no mana on
/// its own; CR 305.6) is loaded from
/// <c>Majik.Core/CardData/Cards/escape-tunnel.json</c> via
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="TerramorphicExpanseFactory"/> / <see cref="RoguesPassageFactory"/>.
/// Both activated abilities are hand-attached because the data-driven
/// <see cref="AbilityDefinition"/> schema does not yet express
/// search/sacrifice/enters-tapped tutor abilities nor targeted
/// combat-restriction grants (it currently covers mana abilities only).
///
/// This is a fusion of two existing land shapes:
/// - the sac-to-fetch-basic-tapped ability of <see cref="TerramorphicExpanseFactory"/>
///   / <see cref="EvolvingWildsFactory"/>, and
/// - the targeted "can't be blocked this turn" ability of
///   <see cref="RoguesPassageFactory"/>, narrowed to a target creature with
///   power 2 or less (CR 608.2b — resolution-time legality guard).
///
/// Because both abilities cost <c>{T}, Sacrifice this land</c>, paying either
/// taps and sacrifices the land, so in practice only one is activated per
/// copy — but each is modelled as its own <see cref="ActivatedAbility"/> with
/// the same <see cref="AdditionalCost.Tap"/> + self-sacrifice posture.
///
/// ## Implemented (v1)
/// - Land identity (no supertype, no subtypes; CR 305.6).
/// - <b>{T}, Sacrifice this land:</b> tutor a basic land card (CR 205.4a —
///   Basic supertype + Land card type) onto the battlefield tapped, then
///   shuffle (CR 701.20a). Self-sacrifice inlined in the resolve closure
///   (same trick as Terramorphic Expanse / Evolving Wilds) because
///   <see cref="AdditionalCost.Sacrifice"/>.Pay() is a no-op stub. The
///   sacrifice happens before the search so the land is no longer on the
///   battlefield during the tutor. Library → Battlefield routed through
///   <see cref="ZoneServiceRegistry"/> so ETB-tapped replacements + movement
///   subscribers fire; the printed "tapped" rider is applied after the move.
/// - <b>{T}, Sacrifice this land:</b> target creature with power 2 or less
///   can't be blocked this turn (CR 509.1c restriction, CR 514.2 EOT expiry).
///   On resolution the factory reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and, when the choice is a
///   battlefield <see cref="Creature"/> whose <see cref="Creature.Power"/> is
///   2 or less, registers a single-target
///   <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBeBlocked"/> against the supplied
///   <see cref="ContinuousEffectsService"/>. Untargeted, non-creature,
///   off-battlefield, or power-&gt;2 choices resolve as a no-op (CR 608.2b).
///   Self-sacrifice is inlined here too.
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter targets to "creature with power 2 or less" — the resolution-time
///   guard handles illegal targets (CR 608.2b), same posture as Rogue's
///   Passage / Liquimetal Coating.
/// - <b>No live continuous-effects service</b>: when <paramref name="effects"/>
///   is null the unblockable-grant resolution no-ops (the tap + sacrifice are
///   still part of the cost surface). Matches the Rogue's Passage shape-only
///   path.
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield
///   without publishing a reveal event. Same gap as every tutor factory.
/// </summary>
[CardName("Escape Tunnel")]
public static class EscapeTunnelFactory
{
    public const string CardName = "Escape Tunnel";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("escape-tunnel");

    /// <summary>
    /// Construct Escape Tunnel with no live continuous-effects service. The
    /// fetch ability is fully functional; the unblockable ability is attached
    /// for shape observability but its "can't be blocked" grant no-ops on
    /// resolution. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Escape Tunnel. When <paramref name="effects"/> is supplied,
    /// activating the second ability and resolving against a battlefield
    /// <see cref="Creature"/> with power 2 or less registers a single-target
    /// CR 509.1c "can't be blocked" restriction on that creature until end of
    /// turn (CR 514.2).
    /// </summary>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        AddFetchAbility(land, owner);
        AddUnblockableAbility(land, owner, effects);

        return land;
    }

    // ----------------------------------------------------------------------
    // {T}, Sacrifice this land: tutor a basic land -> battlefield tapped,
    // then shuffle.
    // ----------------------------------------------------------------------
    private static void AddFetchAbility(Land land, Player owner)
    {
        ActivatedAbility? fetchAbility = null;
        var fetchEffect = new Effect(
            $"{CardName}: sac self + tutor basic land -> battlefield tapped, shuffle",
            async ctx =>
            {
                if (fetchAbility == null) return;

                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;

                // Self-sacrifice — move this land from battlefield to its
                // owner's graveyard (CR 701.16). Must happen before the
                // library search so the land is no longer in the library.
                SacrificeToOwnersGraveyard(land);

                await TutorBasicLandToBattlefieldTappedAsync(controller, ctx)
                    .ConfigureAwait(false);
            });

        fetchAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { fetchEffect });

        land.AddAbility(fetchAbility);
    }

    // ----------------------------------------------------------------------
    // {T}, Sacrifice this land: Target creature with power 2 or less can't be
    // blocked this turn.
    // CR 602.1 — ordinary activated ability.
    // CR 509.1c — "can't be blocked" combat restriction.
    // CR 514.2 — "this turn" wears off at the cleanup step.
    // CR 608.2b — illegal target (non-creature / power > 2 / gone) → no-op.
    // ----------------------------------------------------------------------
    private static void AddUnblockableAbility(
        Land land, Player owner, ContinuousEffectsService? effects)
    {
        ActivatedAbility? unblockableAbility = null;
        var unblockableEffect = new Effect(
            $"{CardName}: target creature (power <= 2) can't be blocked this turn",
            () =>
            {
                if (unblockableAbility == null) return;

                // Self-sacrifice — the {T},Sacrifice cost; inlined because the
                // generic sacrifice payment is a no-op stub.
                SacrificeToOwnersGraveyard(land);

                if (effects == null) return; // shape-only path

                if (unblockableAbility.ChosenTargets.Count == 0) return;
                if (unblockableAbility.ChosenTargets[0].Count == 0) return;

                if (unblockableAbility.ChosenTargets[0][0] is not Creature target)
                    return; // CR 608.2b — illegal / non-creature target → no-op
                if (target.Zone != ZoneType.Battlefield)
                    return; // target left the battlefield in response
                if (target.Power > 2)
                    return; // CR 608.2b — "power 2 or less" restriction failed

                // expiresAtEndOfTurn defaults to true → "this turn".
                effects.Register(new CombatRestrictionEffect(
                    CombatRestriction.CannotBeBlocked,
                    target: target));
            });

        unblockableAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { unblockableEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature with power 2 or less",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(unblockableAbility);
    }

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
    /// Search <paramref name="player"/>'s library for a basic land card
    /// (CR 205.4a — Basic supertype + Land card type), consult the agent to
    /// pick among candidates (falls back to the first deterministic match),
    /// move the chosen card to the battlefield, tap it (printed rider; CR 305 /
    /// 614), then shuffle (CR 701.20a — shuffle whether or not a card was
    /// found).
    /// </summary>
    private static async ValueTask TutorBasicLandToBattlefieldTappedAsync(Player player, ResolutionContext ctx)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic))
            .ToList();

        // CR 701.19a — prompt the agent even on zero candidates so the human
        // searcher sees the failed search rather than a silent no-op.
        var pick = await LibrarySearch.PromptOnlyAsync(
            ctx, player, candidates, "basic land card").ConfigureAwait(false);

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, player);
                if (pick is Permanent permTapped && !permTapped.IsTapped)
                {
                    permTapped.Tap();
                }
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(player);
                if (pick is Permanent perm)
                {
                    perm.Tap();
                }
            }
        }

        // CR 701.20a — shuffle whether or not a card was found.
        LibraryShuffle.ShuffleLibrary(player, "escape-tunnel");
    }
}
