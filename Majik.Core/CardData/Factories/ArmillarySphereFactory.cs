using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Armillary Sphere (Magic 2013, {2}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "{2}, {T}, Sacrifice this artifact: Search your library for up to two
///    basic land cards, reveal them, put them into your hand, then shuffle."
///
/// The card's base shape (name, single Artifact card type, {2}) is
/// materialised from the embedded JSON definition (<c>armillary-sphere.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="RenegadeMapFactory"/>. The {2}, {T}, Sacrifice search-to-hand
/// ability is layered on here because the JSON schema doesn't express search
/// effects.
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {2}, owner / controller wiring).
/// - <b>{2}, {T}, Sacrifice ~: tutor UP TO TWO basic lands to hand</b> — single
///   <see cref="ActivatedAbility"/> with three costs:
///   <see cref="ManaCostCost"/>("{2}") + <see cref="AdditionalCost.Tap"/> on the
///   sphere + <see cref="AdditionalCost.Sacrifice"/> on the sphere itself
///   (the {2} mana pip distinguishes it from Renegade Map's mana-less cost,
///   and the "up to two" multi-pick distinguishes it from Expedition Map /
///   Renegade Map's single pick). Resolution sacrifices the sphere
///   (battlefield → owner's graveyard, CR 701.16), searches the controller's
///   library for up to two basic land cards (CR 305.6 — Basic supertype + Land
///   card type), consulting the agent per pick via
///   <see cref="LibrarySearch.PromptOnlyAsync"/> (CR 701.19a — declining is
///   legal; deterministic first-basic fallback when no agent registered —
///   same posture as <see cref="CultivateFactory"/>), moves each pick
///   Library → Hand, then shuffles once (CR 701.20a) whether or not a card
///   was found.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal-event emission</b>: the tutored basics move Library → Hand
///   without publishing a reveal event. Same gap as Renegade Map / Expedition
///   Map / every tutor-to-hand factory.
/// </summary>
[CardName("Armillary Sphere")]
public static class ArmillarySphereFactory
{
    public const string CardName = "Armillary Sphere";
    public const string Slug = "armillary-sphere";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Armillary Sphere owned and controlled by
    /// <paramref name="owner"/>. Shape-only — no event bus, so the
    /// self-sacrifice cost publishes nothing (legacy posture; dispatcher /
    /// structural tests).
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload — Renegade Map / Expedition Map pattern). Threads
    /// <c>effects.EventBus</c> into the self-sacrifice cost so paying it
    /// publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a)
    /// crediting the cost-payer — the seam aristocrat payoffs read.
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, effects?.EventBus);

    /// <summary>
    /// Canonical builder. <paramref name="eventBus"/> (when non-null) is
    /// threaded into the self-sacrifice <see cref="AdditionalCost"/> so the
    /// cost-payment path publishes a <see cref="PermanentSacrificedEvent"/>
    /// (CR 701.16a). Null preserves the legacy publish-nothing posture.
    /// </summary>
    public static Artifact Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Artifact, {2}) from the embedded JSON definition.
        var sphere = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        sphere.SetOwner(owner);
        sphere.SetController(owner);

        // ----------------------------------------------------------------
        // {2}, {T}, Sacrifice this artifact: Search your library for up to
        // two basic land cards, reveal them, put them into your hand, then
        // shuffle. CR 602 — activated ability with three costs (mana + tap +
        // sac). CR 701.19a — search consults the agent (null = decline).
        // CR 701.20a — single shuffle after the search.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: tutor up to two basic lands -> hand + sac self",
            async ctx =>
            {
                var controller = sphere.Controller ?? owner;
                SacrificeSelf(sphere, owner, eventBus);
                await TutorUpToTwoBasicsToHandAsync(controller, ctx).ConfigureAwait(false);
            });

        var tutorAbility = new ActivatedAbility(
            source: sphere,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Tap(sphere),
                // CR 701.16a — bus on the SAC COST so the live activation path
                // (CostPayment → cost.Pay) publishes PermanentSacrificedEvent;
                // the closure's SacrificeSelf is the bus-aware fallback for the
                // resolve-only dispatcher/test path.
                AdditionalCost.Sacrifice(sphere, eventBus),
            },
            effects: new IEffect[] { tutorEffect });

        sphere.AddAbility(tutorAbility);

        return sphere;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="sphere"/> from the battlefield to its
    /// owner's graveyard. Idempotent. When <paramref name="eventBus"/> is
    /// supplied (prod effects-aware build) the move routes through
    /// <see cref="Fx.Sacrifice(ICard, Player, IEventBus)"/>, publishing a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a). Null bus = bare
    /// owner-routed move. In the live activation path the cost already moved
    /// the sphere, so this closure no-ops (single publish either way).
    /// </summary>
    private static void SacrificeSelf(Artifact sphere, Player owner, IEventBus? eventBus)
    {
        if (sphere.Zone != ZoneType.Battlefield) return;

        if (eventBus != null)
        {
            Fx.Sacrifice(sphere, sphere.Controller ?? owner, eventBus);
            return;
        }

        var controller = sphere.Controller ?? owner;
        controller.Zones.Battlefield.RemoveCard(sphere);
        owner.Zones.Graveyard.AddCard(sphere);
        sphere.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for UP TO TWO basic land
    /// cards (CR 305.6 — Basic supertype + Land card type), consulting the
    /// agent per pick (each may decline — deterministic first-basic fallback
    /// when no agent), moving each pick Library → Hand (CR 701.19a), then
    /// shuffling once (CR 701.20a) whether or not any card was found. Mirrors
    /// <see cref="CultivateFactory"/>'s two-pick search, but both picks go to
    /// hand (Armillary Sphere doesn't partition battlefield/hand).
    /// </summary>
    private static async ValueTask TutorUpToTwoBasicsToHandAsync(Player player, ResolutionContext ctx)
    {
        bool IsBasicLand(ICard c) =>
            c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic);

        var picks = new List<ICard>(capacity: 2);

        for (int i = 0; i < 2; i++)
        {
            // Re-derive candidates each iteration so the already-picked card
            // (now removed from the library) can't be picked twice.
            var candidates = player.Zones.Library.GetCards()
                .Where(c => IsBasicLand(c) && !picks.Contains(c))
                .ToList();

            // CR 701.19a — prompt even on zero candidates so a human searcher
            // sees the (failed) search; declining is always legal ("up to two").
            var pick = await LibrarySearch.PromptOnlyAsync(
                ctx, player, candidates, "basic land card to put into your hand")
                .ConfigureAwait(false);

            if (pick == null) break;

            player.Zones.Library.RemoveCard(pick);
            player.Zones.Hand.AddCard(pick);
            pick.SetZone(ZoneType.Hand);
            picks.Add(pick);
        }

        // CR 701.20a — shuffle once after the search, even when zero cards
        // were found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, Slug);
    }
}
