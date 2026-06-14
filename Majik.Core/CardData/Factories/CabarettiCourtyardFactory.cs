using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Cabaretti Courtyard — Streets of New Capenna "slow fetch" land cycle.
///
/// Oracle (verified against Scryfall 2026-06-14):
///   <c>When this land enters, sacrifice it. When you do, search your library
///   for a basic Mountain, Forest, or Plains card, put it onto the battlefield
///   tapped, then shuffle and you gain 1 life.</c>
///
/// ## Shape source
/// Card identity (a nonbasic Land with no supertype / subtype, producing no
/// mana on its own; CR 305.6) is loaded from
/// <c>Majik.Core/CardData/Cards/cabaretti-courtyard.json</c> via
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="FabledPassageFactory"/>. The ETB-sacrifice fetch ability is then
/// attached in code because the declarative JSON
/// <see cref="AbilityDefinition"/> schema does not model the
/// "sacrifice self → search → battlefield-tapped + shuffle + gain life"
/// triggered shape (there is no <c>sacrifice_self</c> EFFECT verb — only the
/// activated <c>sacrifice_self</c> COST — and <c>search_library</c> carries no
/// life-gain rider).
///
/// ## Implemented (v1)
/// - Land identity (no supertype, no subtypes — produces no mana on its own;
///   CR 305.6).
/// - <b>ETB trigger (CR 603.6a / 603.6e)</b>: "When this land enters,
///   sacrifice it." The reflexive "When you do, …" clause (CR 603.6b — a
///   triggered ability that triggers off the sacrifice) is folded into the same
///   resolution: the land sacrifices itself, then searches the controller's
///   library for a basic Mountain / Forest / Plains card (CR 205.4a basic
///   supertype + CR 205.3 land subtype), puts it onto the battlefield tapped,
///   shuffles (CR 701.20a), and the controller gains 1 life (CR 119.3). This
///   single-resolution fold matches the FabledPassage / Esper Panorama tutor
///   posture (the reflexive trigger has no independent timing window that any
///   other game action can interleave into in v1).
/// - Self-sacrifice inlined in the resolve closure (same trick as
///   <see cref="FabledPassageFactory"/>): the land moves Battlefield →
///   owner's Graveyard before the search so it is no longer in the library
///   during the tutor.
/// - Library → Battlefield routed through <see cref="ZoneServiceRegistry"/> so
///   ETB-tapped replacements and CardMovedEvent subscribers fire on the tutored
///   basic; the printed "tapped" rider is then applied (CR 305 / 614).
///
/// ## Deferred (v1 gaps)
/// - <b>Reflexive-trigger split</b>: the "sacrifice it. When you do, …" is
///   resolved as one effect rather than two stacked triggered abilities. The
///   observable game state is identical in v1 (nothing interleaves between the
///   sacrifice and the reflexive search), so this is a fidelity simplification,
///   not a behaviour gap.
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield without
///   publishing a reveal event. Same gap as every tutor factory.
/// </summary>
[CardName("Cabaretti Courtyard")]
public static class CabarettiCourtyardFactory
{
    public const string CardName = "Cabaretti Courtyard";
    public const string Slug = "cabaretti-courtyard";

    /// <summary>The land amount of life gained by the reflexive clause
    /// (CR 119.3). Cabaretti Courtyard = 1.</summary>
    private const int LifeGain = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>The legal basic-land subtypes Cabaretti Courtyard can fetch
    /// (CR 205.3) — Mountain, Forest, or Plains.</summary>
    private static readonly CardSubtype[] FetchableSubtypes =
        { CardSubtype.Mountain, CardSubtype.Forest, CardSubtype.Plains };

    /// <summary>Construct Cabaretti Courtyard with the ETB-sacrifice fetch
    /// trigger attached but NOT registered with a
    /// <see cref="TriggerManager"/> — suitable for shape / dispatcher tests
    /// and direct effect execution.</summary>
    public static Land Create(Player owner) => Create(owner, triggers: null);

    /// <summary>Construct Cabaretti Courtyard with optional
    /// <see cref="TriggerManager"/> wiring. When <paramref name="triggers"/> is
    /// supplied the ETB trigger is registered so the entering
    /// <c>CardMovedEvent</c> places it on the stack automatically
    /// (CR 603.3).</summary>
    public static Land Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // "When this land enters, sacrifice it. When you do, search your
        //  library for a basic Mountain, Forest, or Plains card, put it onto
        //  the battlefield tapped, then shuffle and you gain 1 life."
        //  CR 603.6a (ETB trigger) + CR 603.6b (reflexive "when you do").
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: sacrifice self -> tutor basic Mountain/Forest/Plains "
                + "-> battlefield tapped, shuffle, gain 1 life",
            async ctx =>
            {
                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;

                // Self-sacrifice — Battlefield → owner's graveyard (CR 701.16).
                // Must precede the search so the land is no longer on the
                // battlefield / in any zone the tutor inspects.
                SacrificeToOwnersGraveyard(land);

                await TutorBasicThenGainLifeAsync(controller, ctx).ConfigureAwait(false);
            });

        var etbTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return land;
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
    /// Search <paramref name="player"/>'s library for a basic Mountain, Forest,
    /// or Plains card (CR 205.4a Basic supertype + CR 205.3 land subtype),
    /// consult the agent to pick (deterministic first-match fallback), move the
    /// chosen card to the battlefield, tap it (printed rider; CR 305 / 614),
    /// then shuffle (CR 701.20a). Finally, the controller gains 1 life (CR
    /// 119.3) whether or not a card was found — the "you gain 1 life" clause is
    /// unconditional once the reflexive trigger resolves.
    /// </summary>
    private static async ValueTask TutorBasicThenGainLifeAsync(Player player, ResolutionContext ctx)
    {
        bool IsFetchable(ICard c) =>
            c.HasType(CardType.Land)
            && c.HasSupertype(CardSupertype.Basic)
            && FetchableSubtypes.Any(c.HasSubtype);

        var candidates = player.Zones.Library.GetCards().Where(IsFetchable).ToList();

        // CR 701.19a — prompt even on zero candidates so a human searcher sees
        // the failed search rather than a silent no-op.
        var pick = await LibrarySearch.PromptOnlyAsync(
            ctx, player, candidates, "basic Mountain, Forest, or Plains card")
            .ConfigureAwait(false);

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
        LibraryShuffle.ShuffleLibrary(player, Slug);

        // CR 119.3 — "and you gain 1 life." Unconditional once the reflexive
        // clause resolves (independent of whether a basic was found).
        player.GainLife(LifeGain);
    }
}
