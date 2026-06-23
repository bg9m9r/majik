using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Promising Vein (The Lost Caverns of Ixalan,
/// Land — Cave).
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {1}, {T}, Sacrifice this land: Search your library for a basic land
///    card, put it onto the battlefield tapped, then shuffle."
///
/// A Cave-typed colourless source whose late-game mode sacrifices itself to
/// ramp a basic land in tapped. The base shape (name, Land — Cave, the
/// <c>{T}: Add {C}</c> mana ability) is materialised from the embedded JSON
/// definition (<c>promising-vein.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="RamunapRuinsFactory"/> / <see cref="CaveOfTemptationFactory"/>
/// (the <c>{C}</c> half + a subtype come straight from JSON). The
/// search/sacrifice/enters-tapped tutor ability is layered on in code because
/// the JSON <see cref="AbilityDefinition"/> schema models only mana abilities,
/// not tutor abilities — same split as <see cref="TerramorphicExpanseFactory"/>.
///
/// ## Implemented (v1)
/// - <b>Land — Cave</b> + <b>{T}: Add {C}</b> (from JSON; CR 605.1 — a mana
///   ability, no stack). {C} is tracked in the generic bucket, the same
///   modelling as every other <c>produces: "C"</c> land
///   (<see cref="RamunapRuinsFactory"/> / <see cref="MirrodinsCoreFactory"/>).
/// - <b>{1}, {T}, Sacrifice this land:</b> search the controller's library for
///   a basic land card (CR 205.4a — Basic supertype + Land card type), put it
///   onto the battlefield <b>tapped</b> (printed rider; CR 305 / 614), then
///   shuffle (CR 701.20a). Differs from Terramorphic Expanse / Evolving Wilds
///   only by the added <b>{1}</b> mana component in the activation cost
///   (CR 117.5).
///     - <see cref="ManaCostCost"/> ({1}) + <see cref="AdditionalCost.Tap"/> +
///       <see cref="AdditionalCost.Sacrifice"/> are all declared ICosts so the
///       ability's CanPay gate reads correctly. CostPayment runs {1} + {T}
///       before the ability hits the stack; the self-sacrifice and the tutor
///       are performed inside the resolve closure (the generic
///       <see cref="AdditionalCost.Sacrifice"/> payment is a no-op stub, same
///       posture as Terramorphic Expanse / Prismatic Vista).
///
/// ## Deferred (v1 gaps — shared with the sac-fetch land family)
/// - <b>Sacrifice payment side effects</b>: the generic Sacrifice cost is a
///   no-op stub; the closure performs the zone move directly so behaviour is
///   observable — same posture as Terramorphic Expanse / Prismatic Vista.
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield
///   without publishing a reveal event. Same gap as every tutor factory.
/// </summary>
[CardName("Promising Vein")]
public static class PromisingVeinFactory
{
    public const string CardName = "Promising Vein";
    public const string Slug = "promising-vein";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Promising Vein owned and controlled by
    /// <paramref name="owner"/>. Attaches the JSON {C} ability plus the
    /// {1}, {T}, Sacrifice: tutor-a-basic-tapped ability.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition: Land — Cave + the
        // {T}: Add {C} mana ability (CR 605.1 — a mana ability, no stack).
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {1}, {T}, Sacrifice this land:
        //   Search your library for a basic land card, put it onto the
        //   battlefield tapped, then shuffle.
        // CR 602 — activated ability (non-mana). Mana cost {1} + {T} +
        // sacrifice. CostPayment runs {1} + {T} before the ability hits the
        // stack; the self-sacrifice + tutor run in the resolve closure (the
        // generic Sacrifice cost is a no-op stub — same posture as
        // Terramorphic Expanse / Prismatic Vista).
        // ----------------------------------------------------------------
        ActivatedAbility? fetchAbility = null;
        var fetchEffect = new Effect(
            $"{CardName}: sac self + tutor basic land -> battlefield tapped, shuffle",
            async ctx =>
            {
                if (fetchAbility == null) return;

                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;

                // Self-sacrifice — battlefield → owner's graveyard (CR 701.16).
                // Must happen before the library search so the land is not in
                // the library when we search.
                SacrificeToOwnersGraveyard(land);

                await TutorBasicLandToBattlefieldTappedAsync(controller, ctx)
                    .ConfigureAwait(false);
            });

        fetchAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land),
            },
            effects: new IEffect[] { fetchEffect });

        land.AddAbility(fetchAbility);
        return land;
    }

    /// <summary>
    /// Move <paramref name="self"/> from the battlefield to its owner's
    /// graveyard. Idempotent — no-op if already off the battlefield. Mirrors
    /// the closure used by Terramorphic Expanse / Cave of Temptation.
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
    /// Search <paramref name="player"/>'s library for a basic land card
    /// (CR 205.4a — Basic supertype + Land card type), consult the agent to
    /// pick among candidates (falls back to the first deterministic match),
    /// move the chosen card to the battlefield, tap it (printed rider; CR
    /// 305 / 614), then shuffle (CR 701.20a). Mirrors
    /// <see cref="TerramorphicExpanseFactory"/>.
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
        LibraryShuffle.ShuffleLibrary(player, Slug);
    }
}
