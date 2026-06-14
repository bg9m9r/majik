using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Obscura Storefront — Streets of New Capenna common "storefront" land cycle
/// (the Obscura / Esper member).
///
/// Oracle (verified against Scryfall 2026-06-14):
///   <c>When this land enters, sacrifice it. When you do, search your library
///      for a basic Plains, Island, or Swamp card, put it onto the battlefield
///      tapped, then shuffle and you gain 1 life.</c>
///
/// ## Shape
/// Card identity (a nonbasic Land with no supertype/subtype, producing no mana
/// on its own; CR 305.6) is loaded from
/// <c>Majik.Core/CardData/Cards/obscura-storefront.json</c> via
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="FabledPassageFactory"/>.
///
/// The behaviour is a single ETB triggered ability (CR 603.6a — "When this land
/// enters") attached in code, because the declarative
/// <see cref="EffectDefinition"/> union does not model "sacrifice this
/// permanent" as an EFFECT (it only models <c>sacrifice_self</c> as an
/// activation COST). The printed text is two linked abilities — the ETB
/// "sacrifice it" and the reflexive "When you do, search …" (CR 603.6e) — but
/// because the sacrifice is mandatory and unconditional, the reflexive trigger
/// always follows, so the engine collapses them into one resolve closure: the
/// land sacrifices itself, then the tutor + lifegain follow in sequence (the
/// same observable result; the intermediate "When you do" stack object carries
/// no independent decision point). This mirrors the
/// <see cref="FabledPassageFactory"/> idiom (sacrifice self inside the resolve
/// closure, then tutor a basic land onto the battlefield tapped, then shuffle).
///
/// ## Implemented (v1)
/// - Land identity (no supertype, no subtypes — produces no mana on its own;
///   CR 305.6).
/// - ETB triggered ability (CR 603.6a): sacrifice this land (CR 701.16), then
///   search the controller's library for a basic Plains, Island, or Swamp card
///   (CR 205.4a — Basic supertype + the P/I/S land subtypes), put it onto the
///   battlefield tapped (CR 614), then shuffle (CR 701.20a) and gain 1 life
///   (CR 119.3).
/// - Self-sacrifice inlined in the resolve closure (same trick as
///   <see cref="FabledPassageFactory"/> / Terramorphic Expanse) because
///   <see cref="Primitives.Fx"/>'s sacrifice helpers move the land to its
///   owner's graveyard directly. The sacrifice happens before the search so the
///   land is no longer in the library/battlefield during the tutor.
/// - Library → Battlefield routed through <see cref="ZoneServiceRegistry"/> so
///   ETB-tapped replacements (snow basics) and CardMovedEvent subscribers
///   (Amulet of Vigor untap, bounce-land ETB triggers) fire on the tutored
///   basic; the printed "tapped" rider is then applied per CR 305 / 614.
///
/// ## Deferred (v1 gaps — shared by every tutor factory)
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield without
///   publishing a reveal event. Same gap as every tutor factory.
/// - <b>Two-stack-object linkage</b>: the printed "When you do" reflexive
///   trigger (CR 603.6e) is collapsed into the ETB resolve rather than placed on
///   the stack as a separate object. Because both halves are mandatory and carry
///   no separate decision, the observable result is identical.
/// </summary>
[CardName("Obscura Storefront")]
public static class ObscuraStorefrontFactory
{
    public const string CardName = "Obscura Storefront";
    public const string Slug = "obscura-storefront";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>The P/I/S land subtypes the tutor may fetch (CR 205.3).</summary>
    private static readonly CardSubtype[] FetchableSubtypes =
    {
        CardSubtype.Plains, CardSubtype.Island, CardSubtype.Swamp,
    };

    /// <summary>Life gained after the fetch (CR 119.3).</summary>
    private const int LifeGain = 1;

    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        var etbEffect = new Effect(
            "Obscura Storefront: sac self -> tutor basic Plains/Island/Swamp to battlefield tapped, shuffle, gain 1 life",
            async ctx =>
            {
                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;

                // "Sacrifice it" (CR 701.16) — move this land to its owner's
                // graveyard before the search so it is no longer in the
                // library/battlefield during the tutor.
                SacrificeToOwnersGraveyard(land);

                await TutorBasicThenGainLifeAsync(controller, ctx).ConfigureAwait(false);
            });

        var etb = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { etbEffect });

        land.AddAbility(etb);
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
    /// Search <paramref name="player"/>'s library for a basic Plains, Island, or
    /// Swamp card (CR 205.4a — Basic supertype + one of the P/I/S land
    /// subtypes), consult the agent to pick among candidates (falls back to the
    /// first deterministic match), move the chosen card to the battlefield, tap
    /// it (printed rider; CR 614), then shuffle (CR 701.20a) and gain 1 life
    /// (CR 119.3). The shuffle + lifegain happen whether or not a card was found
    /// (CR 701.20a).
    /// </summary>
    private static async ValueTask TutorBasicThenGainLifeAsync(Player player, ResolutionContext ctx)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land)
                && c.HasSupertype(CardSupertype.Basic)
                && FetchableSubtypes.Any(c.HasSubtype))
            .ToList();

        var pick = await LibrarySearch.PromptOnlyAsync(
            ctx, player, candidates, "basic Plains, Island, or Swamp card")
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

        // CR 119.3 — "and you gain 1 life", part of the same reflexive
        // resolution; happens regardless of whether the search found a card.
        Fx.GainLife(player, LifeGain);
    }
}
