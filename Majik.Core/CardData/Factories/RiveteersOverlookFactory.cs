using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Riveteers Overlook — Streets of New Capenna land (the Jund member of the
/// common "tapped triome fetch" land cycle — the gain-1-life siblings of
/// Brokers Hideout / Cabaretti Courtyard / Maestros Theater / Obscura
/// Storefront).
///
/// Oracle (verified against Scryfall 2026-06-14):
///   <c>When this land enters, sacrifice it. When you do, search your library
///      for a basic Swamp, Mountain, or Forest card, put it onto the
///      battlefield tapped, then shuffle and you gain 1 life.</c>
///
/// Card identity (a colorless nonbasic Land with no supertype/subtype, producing
/// no mana on its own; CR 305.6) is loaded from
/// <c>Majik.Core/CardData/Cards/riveteers-overlook.json</c> via
/// <see cref="CardDefinitionFactory"/> — same minimal-identity posture as
/// <see cref="FabledPassageFactory"/>. The ETB-triggered fetch ability is then
/// attached in code because the JSON <see cref="EffectDefinition"/> schema does
/// not model the reflexive "sacrifice self → search + gain life" tutor shape
/// (it covers an activated <c>sacrifice_self</c> cost + <c>search_library</c>,
/// but neither a sacrifice-self EFFECT, a reflexive "when you do" sub-trigger,
/// nor a gain-life rider on the search). The behaviour mirrors
/// <see cref="BountifulLandscapeFactory"/>'s tutor closure (basic
/// Swamp/Mountain/Forest → battlefield tapped, shuffle) plus the printed
/// "you gain 1 life" rider, hung off an ETB triggered ability instead of an
/// activated one.
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — no supertype, no subtypes; produces no mana on its
///   own (CR 305.6).
/// - <b>ETB trigger</b>: "When this land enters, sacrifice it" (CR 603.6a /
///   CR 701.16). The reflexive "When you do, search …" sub-trigger (CR 603.2g)
///   is folded into the same ETB triggered ability — for this non-targeting,
///   non-optional payoff the sacrifice and the search+lifegain resolve as one
///   practical unit.
/// - <b>Search</b>: a basic Swamp / Mountain / Forest card (CR 205.4a — Basic
///   supertype + one of those land subtypes), put onto the battlefield tapped
///   (CR 701.19a), then shuffle (CR 701.20a — shuffle whether or not a card was
///   found).
/// - <b>"and you gain 1 life"</b> (CR 119.3) — applied after the search/shuffle.
/// - Self-sacrifice inlined in the resolve closure (same trick as
///   <see cref="BountifulLandscapeFactory"/> / <see cref="FabledPassageFactory"/>);
///   it happens before the search so this land is no longer on the battlefield
///   or in the library during the tutor.
/// - Library → Battlefield routed through <see cref="ZoneServiceRegistry"/> when
///   a live service is registered so ETB-tapped replacements (e.g. snow basics)
///   and CardMovedEvent subscribers (Amulet of Vigor untap, bounce-land ETB
///   triggers) fire on the tutored basic; the printed "tapped" rider is then
///   applied (CR 305 / 614).
///
/// ## Deferred (matches every tutor factory)
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield without
///   publishing a reveal event.
/// </summary>
[CardName("Riveteers Overlook")]
public static class RiveteersOverlookFactory
{
    public const string CardName = "Riveteers Overlook";
    public const string Slug = "riveteers-overlook";

    /// <summary>Life gained after the fetch (CR 119.3).</summary>
    private const int LifeGain = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Riveteers Overlook owned and controlled by
    /// <paramref name="owner"/> from its embedded JSON identity definition plus
    /// the imperatively-attached ETB fetch ability.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // "When this land enters, sacrifice it. When you do, search your library
        // for a basic Swamp, Mountain, or Forest card, put it onto the
        // battlefield tapped, then shuffle and you gain 1 life."
        var etbEffect = new Effect(
            $"{CardName}: ETB sacrifice self + tutor basic Swamp/Mountain/Forest -> battlefield tapped, shuffle, gain 1 life",
            async ctx =>
            {
                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;

                // Self-sacrifice (CR 701.16) before the search so this land is
                // no longer on the battlefield / in the library during the tutor.
                SacrificeToOwnersGraveyard(land);

                await TutorBasicSwampMountainForestToBattlefieldTappedAsync(controller, ctx)
                    .ConfigureAwait(false);

                // "and you gain 1 life" (CR 119.3).
                controller.GainLife(LifeGain);
            });

        var etbTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { etbEffect });
        land.AddAbility(etbTrigger);

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
    /// Search <paramref name="player"/>'s library for a basic land card whose
    /// subtype is Swamp, Mountain, or Forest (CR 205.4a — Basic supertype + one
    /// of those land subtypes), consult the agent to pick among candidates
    /// (falls back to the first deterministic match), move the chosen card to
    /// the battlefield, apply the printed "tapped" rider, then shuffle
    /// (CR 701.20a — shuffle whether or not a card was found).
    /// </summary>
    private static async ValueTask TutorBasicSwampMountainForestToBattlefieldTappedAsync(Player player, ResolutionContext ctx)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land)
                        && c.HasSupertype(CardSupertype.Basic)
                        && (c.HasSubtype(CardSubtype.Swamp)
                            || c.HasSubtype(CardSubtype.Mountain)
                            || c.HasSubtype(CardSubtype.Forest)))
            .ToList();

        // CR 701.19a — prompt the agent even on zero candidates so the human
        // searcher sees the failed search rather than a silent no-op.
        var pick = await LibrarySearch.PromptOnlyAsync(
            ctx, player, candidates, "basic Swamp, Mountain, or Forest card")
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
    }
}
