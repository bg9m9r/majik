using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Civic Wayfinder (Ravnica: City of Guilds / many
/// reprints, {2}{G}).
///
/// Creature — Elf Druid Warrior 2/2. Oracle text (verified against
/// Scryfall):
///   "When this creature enters, you may search your library for a basic
///    land card, reveal it, put it into your hand, then shuffle."
///
/// The base shape (name, Creature, Elf/Druid/Warrior subtypes, {2}{G}, 2/2)
/// is materialised from the embedded JSON definition
/// (<c>civic-wayfinder.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The ETB basic-land tutor is
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express library-search-to-hand effects, so it lives in the factory
/// (same posture as <see cref="WoodElvesFactory"/>).
///
/// ## Implemented (v1)
/// - 2/2 Creature — Elf Druid Warrior at {2}{G}.
/// - <b>ETB triggered ability (CR 603.6a)</b> wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with
///   ActiveZones = Battlefield. On resolution it searches the controller's
///   library for a <b>basic land card</b> — matched by the Basic supertype
///   (CR 205.4a) + Land card type (CR 305.6), so basic Forest / Island /
///   etc. and snow basics are legal, but nonbasic lands (even Forest-typed
///   duals) are not.
/// - The picked basic is put into the controller's <b>hand</b> (contrast
///   <see cref="WoodElvesFactory"/>, which puts the land onto the
///   battlefield) via the shared
///   <see cref="TypedCyclingFactory.TutorTypedCard"/> primitive — the same
///   "basic land card → hand → shuffle" closure
///   <see cref="KrosanTuskerFactory"/> cribs for its on-cycle rider. The
///   primitive consults the controller's agent (CR 701.19a — deterministic
///   first-match fallback when no agent is registered) and shuffles once
///   afterwards (CR 701.20a).
/// - The printed "you may" optional rider (CR 603.6 / 701.19a — search is
///   an action a player may decline): an agent returning null = decline;
///   the shape-only path with no agent defaults to find-and-keep to
///   preserve the deck-fix tempo line — the same posture as every other
///   tutor primitive in the engine (Wood Elves, Krosan Tusker, Stoneforge
///   Mystic, Sylvan Scrying).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the tutored basic moves Library → Hand without
///   publishing a reveal event. Same gap as every tutor factory; the
///   destination zone (hand) is otherwise correct.
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + ETB trigger attached (not
///   registered with any <see cref="TriggerManager"/>). The overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?)"/> — also registers the
///   ETB trigger so a qualifying
///   <see cref="Majik.Core.Events.CardMovedEvent"/> lands the ability on the
///   stack automatically (CR 603.2).
/// </summary>
[CardName("Civic Wayfinder")]
public static class CivicWayfinderFactory
{
    public const string CardName = "Civic Wayfinder";
    public const string Slug = "civic-wayfinder";

    /// <summary>
    /// Shape overload — attaches the ETB trigger without registering it with
    /// a <see cref="TriggerManager"/>. The overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Civic Wayfinder with its ETB basic-land tutor attached and
    /// optionally registered against the supplied <paramref name="triggers"/>
    /// manager.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers so a
    /// qualifying <see cref="Majik.Core.Events.CardMovedEvent"/> automatically
    /// queues the ability on the stack (CR 603.2).</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elf/Druid/Warrior subtypes, {2}{G}, 2/2). The JSON carries no
        // abilities — the ETB basic-land tutor is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, you may search your library for a
        //    basic land card, reveal it, put it into your hand, then
        //    shuffle."
        //
        // Predicate: Basic supertype (CR 205.4a) + Land card type
        // (CR 305.6) — a "basic land card". Routed through the shared
        // TutorTypedCard primitive (basic land card → hand → shuffle),
        // the identical closure Krosan Tusker uses for its on-cycle rider.
        // CR 701.19a agent prompt + deterministic fallback + CR 701.20a
        // single shuffle.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: search library for a basic land card, put it into hand, then shuffle",
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                await TypedCyclingFactory.TutorTypedCardAsync(
                    ctx: ctx,
                    owner: controller,
                    predicate: c =>
                        c.HasType(CardType.Land)
                        && c.HasSupertype(CardSupertype.Basic),
                    kindLabel: "basic land card",
                    shuffleReason: "civic-wayfinder").ConfigureAwait(false);
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
}
