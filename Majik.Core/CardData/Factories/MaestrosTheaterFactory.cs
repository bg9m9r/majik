using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Maestros Theater — Streets of New Capenna "Pathway"-adjacent tri-fetch land
/// (the Grixis member of the New Capenna fetch-tri-land cycle).
///
/// Oracle (verified against Scryfall 2026-06-14):
///   "When this land enters, sacrifice it. When you do, search your library for
///    a basic Island, Swamp, or Mountain card, put it onto the battlefield
///    tapped, then shuffle and you gain 1 life."
///
/// ## Shape source
/// Card identity (a colourless nonbasic Land with no supertype/subtype, producing
/// no mana on its own; CR 305.6) is loaded from
/// <c>Majik.Core/CardData/Cards/maestros-theater.json</c> via
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="FabledPassageFactory"/> / <see cref="TerramorphicExpanseFactory"/>.
/// The behaviour is then attached in code because the JSON ability schema does
/// not model a single "ETB → sacrifice self → subtype-restricted fetch tapped →
/// gain life" shape; the suggested analogue <see cref="EsperPanoramaFactory"/>
/// expresses the tri-basic fetch as an ACTIVATED ({1},{T},Sac) ability, whereas
/// Maestros Theater performs it from an enters-the-battlefield TRIGGER.
///
/// ## Implemented (v1)
/// - Land identity (no supertype, no subtypes — produces no mana on its own;
///   CR 305.6).
/// - ETB triggered ability (CR 603.6a): on entering, sacrifice this land
///   (CR 701.16), then search the controller's library for a basic Island,
///   Swamp, or Mountain card (CR 205.4a — Basic supertype + the Island/Swamp/
///   Mountain land subtype, CR 205.3i), put it onto the battlefield tapped
///   (CR 305 / 614), then shuffle (CR 701.20a) and gain 1 life (CR 119.3).
/// - Self-sacrifice inlined in the resolve closure (same trick as
///   <see cref="TerramorphicExpanseFactory"/> / <see cref="FabledPassageFactory"/>)
///   because the generic <c>AdditionalCost.Sacrifice</c> payment is a no-op
///   stub. The sacrifice happens before the search so the land is no longer on
///   the battlefield during the tutor (and does not appear in the library).
/// - Library → Battlefield routed through <see cref="ZoneServiceRegistry"/> so
///   ETB-tapped replacements + CardMovedEvent subscribers fire on the tutored
///   basic; the printed "tapped" rider is then applied per CR 305 / 614.
///
/// ## Reflexive-trigger simplification
/// The printed text is two LINKED reflexive abilities (CR 603.6b — "When this
/// land enters, sacrifice it. <i>When you do</i>, search …"): the ETB trigger
/// sacrifices, and the actual sacrifice event spawns the reflexive
/// search-and-gain trigger. The observable game state is identical whether the
/// fetch resolves as a second triggered ability or inline within the ETB
/// resolution (there is no intervening priority window the search depends on,
/// and Maestros Theater itself is already in the graveyard either way), so the
/// fetch + lifegain are inlined into the ETB resolve closure — the same
/// single-resolution posture the activated-ability sibling Esper Panorama uses.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the closure performs the zone move
///   directly (no <c>PermanentSacrificedEvent</c> published) — same posture as
///   Terramorphic Expanse / Fabled Passage.
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield without
///   publishing a reveal event. Same gap as every tutor factory.
/// </summary>
[CardName("Maestros Theater")]
public static class MaestrosTheaterFactory
{
    public const string CardName = "Maestros Theater";
    public const string Slug = "maestros-theater";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>The basic land subtypes Maestros Theater can fetch (CR 205.3i).</summary>
    private static readonly CardSubtype[] FetchableSubtypes =
        { CardSubtype.Island, CardSubtype.Swamp, CardSubtype.Mountain };

    /// <summary>Life gained after the fetch resolves (CR 119.3).</summary>
    private const int LifeGain = 1;

    /// <summary>
    /// Construct Maestros Theater with its ETB trigger attached but NOT
    /// registered with a <see cref="TriggerManager"/> — suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Maestros Theater with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so the relevant <c>CardMovedEvent</c> places it on the stack
    /// automatically (CR 603.3).
    /// </summary>
    public static Land Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);
        land.SetOwner(owner);
        land.SetController(owner);

        var etbEffect = new Effect(
            $"{CardName}: sacrifice self -> tutor basic Island/Swamp/Mountain to battlefield tapped, shuffle, gain {LifeGain} life",
            async ctx =>
            {
                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;

                // CR 701.16 — sacrifice this land. Must happen before the
                // search so the land is no longer on the battlefield (and is
                // never a search candidate from the library).
                SacrificeToOwnersGraveyard(land);

                await TutorTriBasicTappedThenGainLifeAsync(controller, ctx)
                    .ConfigureAwait(false);
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
    /// Search <paramref name="player"/>'s library for a basic Island, Swamp, or
    /// Mountain card (CR 205.4a — Basic supertype + the relevant land subtype,
    /// CR 205.3i), consult the agent to pick among candidates (prompting even on
    /// zero candidates so a failed search is visible), move the chosen card to
    /// the battlefield tapped (CR 305 / 614), shuffle (CR 701.20a), then gain
    /// <see cref="LifeGain"/> life (CR 119.3 — the lifegain happens whether or
    /// not a card was found, as it is part of the same resolution clause).
    /// </summary>
    private static async ValueTask TutorTriBasicTappedThenGainLifeAsync(Player player, ResolutionContext ctx)
    {
        bool IsFetchable(ICard c) =>
            c.HasType(CardType.Land)
            && c.HasSupertype(CardSupertype.Basic)
            && FetchableSubtypes.Any(c.HasSubtype);

        var candidates = player.Zones.Library.GetCards().Where(IsFetchable).ToList();

        // CR 701.18a — prompt the agent even on zero candidates so the human
        // searcher sees the failed search rather than a silent no-op.
        var pick = await LibrarySearch.PromptOnlyAsync(
            ctx, player, candidates, "basic Island, Swamp, or Mountain card")
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

        // CR 119.3 — "then shuffle and you gain 1 life." The lifegain is part of
        // the same resolution clause as the (possibly failed) search.
        player.GainLife(LifeGain);
    }
}
