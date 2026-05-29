using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wood Elves (Tempest / many reprints, {2}{G}).
///
/// Creature — Elf Scout 1/1. Oracle text (verified against Scryfall):
///   "When this creature enters, search your library for a Forest card,
///    put that card onto the battlefield, then shuffle."
///
/// The base shape (name, Creature, Elf/Scout subtypes, {2}{G}, 1/1) is
/// materialised from the embedded JSON definition (<c>wood-elves.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The ETB Forest tutor is
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express library-search-onto-battlefield effects, so it lives in the
/// factory (same posture as <see cref="BladeSplicerFactory"/> and the
/// other JSON-backed cards whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - 1/1 Creature — Elf Scout at {2}{G}.
/// - <b>ETB triggered ability (CR 603.6a)</b> wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with
///   ActiveZones = Battlefield. On resolution it searches the controller's
///   library for a <b>Forest card</b> — matched by the Forest land
///   <i>subtype</i> (CR 305.6), so basic Forest, Snow-Covered Forest, and
///   any nonbasic with the Forest land type (e.g. Stomping Ground / Bayou)
///   are all legal, but Island / Plains / a non-Forest land are not. Same
///   predicate (<c>c =&gt; c.HasSubtype(CardSubtype.Forest)</c>) the
///   <see cref="GenerousEntFactory"/> Forestcycling tutor uses.
/// - The picked Forest is put onto the battlefield <b>untapped</b> — Wood
///   Elves' oracle text carries no "tapped" qualifier (contrast
///   <see cref="SakuraTribeElderFactory"/>, which prints "tapped"). The
///   move routes through <see cref="ZoneServiceRegistry"/> so ETB triggers
///   / enters-tapped replacements (snow basics) fire on the tutored land;
///   raw-zone fallback when no live service is wired (shape-test path).
/// - <b>Shuffle (CR 701.20a)</b> via
///   <see cref="LibraryShuffle.ShuffleLibrary"/> after the search,
///   whether or not a card was found.
/// - The "may"-free search still calls the agent so a human searcher SEES
///   the search (CR 701.19a) via <see cref="LibrarySearch.PromptOnly"/>;
///   deterministic first-match fallback when no agent is registered.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the tutored Forest moves Library → Battlefield
///   without publishing a reveal event (Wood Elves does not reveal anyway,
///   so this is a non-issue here). Same posture as every tutor factory.
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + ETB trigger attached (not
///   registered with any <see cref="TriggerManager"/>; raw-zone tutor
///   fallback). The overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?)"/> — also registers the
///   ETB trigger so a qualifying <see cref="Majik.Core.Events.CardMovedEvent"/>
///   lands the ability on the stack automatically (CR 603.2).
/// </summary>
[CardName("Wood Elves")]
public static class WoodElvesFactory
{
    public const string CardName = "Wood Elves";
    public const string Slug = "wood-elves";

    /// <summary>
    /// Shape overload — attaches the ETB trigger without registering it with
    /// a <see cref="TriggerManager"/>. The overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Wood Elves with its ETB Forest tutor attached and optionally
    /// registered against the supplied <paramref name="triggers"/> manager.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers so a
    /// qualifying <see cref="Majik.Core.Events.CardMovedEvent"/> automatically
    /// queues the ability on the stack (CR 603.2).</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elf/Scout subtypes, {2}{G}, 1/1). The JSON carries no abilities —
        // the ETB Forest tutor is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, search your library for a Forest
        //    card, put that card onto the battlefield, then shuffle."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: search library for a Forest card, put onto battlefield, then shuffle",
            () =>
            {
                var controller = card.Controller ?? owner;
                TutorForestToBattlefield(controller);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for a Forest card (CR 305.6
    /// — matched by the Forest land subtype, so basics, snow basics and
    /// Forest-typed nonbasics are all legal), consult the agent to pick among
    /// candidates (deterministic first-match fallback), move the chosen card
    /// onto the battlefield UNTAPPED, then shuffle (CR 701.20a).
    /// </summary>
    private static void TutorForestToBattlefield(Player player)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && c.HasSubtype(CardSubtype.Forest))
            .ToList();

        // CR 701.19a — prompt the agent even on zero candidates so the human
        // searcher sees the (failed) search.
        var pick = LibrarySearch.PromptOnly(player, candidates, "Forest card");

        if (pick != null)
        {
            // CR 603.6a / CR 614 — route through ZoneService so ETB triggers
            // and enters-tapped replacements (snow basics) fire on the tutored
            // Forest. No "tapped" rider — Wood Elves' Forest enters untapped.
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
                if (pick is Permanent perm)
                    perm.MarkEnteredBattlefield();
            }
        }

        // CR 701.20a — shuffle whether or not a card was found.
        LibraryShuffle.ShuffleLibrary(player, "wood-elves");
    }
}
