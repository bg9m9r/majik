using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kozilek, Butcher of Truth (Rise of the Eldrazi,
/// {10}). Legendary Creature — Eldrazi 12/12. Oracle text (verified against
/// Scryfall):
///   "When you cast this spell, draw four cards.
///    Annihilator 4 (Whenever this creature attacks, defending player
///    sacrifices four permanents of their choice.)
///    When Kozilek is put into a graveyard from anywhere, its owner
///    shuffles their graveyard into their library."
///
/// The card's base shape (name, Legendary supertype, Eldrazi subtype, {10},
/// 12/12) is materialised from the embedded JSON definition
/// (<c>kozilek-butcher-of-truth.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed behaviours
/// (on-cast draw, Annihilator 4, graveyard-shuffle) are layered on here —
/// the JSON <c>AbilityDefinition</c> schema doesn't express cast triggers,
/// Annihilator, or put-into-graveyard triggers, so they live in the factory
/// (same posture as <see cref="KozilekTheGreatDistortionFactory"/> /
/// <see cref="EmrakulTheAeonsTornFactory"/>).
///
/// ## Implemented (v1)
/// - <b>12/12 Legendary Creature — Eldrazi at {10}</b> (mana value 10,
///   colourless — CR 105.2c, no coloured symbols).
/// - <b>Cast trigger — "draw four cards" (CR 603.6a / CR 603.10)</b>:
///   triggered ability over <see cref="SpellCastEvent"/> filtered to
///   <c>e.Spell.Card == card</c> (same self-cast detection pattern as
///   <see cref="EmrakulTheAeonsTornFactory"/> /
///   <see cref="KozilekTheGreatDistortionFactory"/>), <c>activeZones =
///   Stack</c> because Kozilek is on the stack as a spell when the trigger
///   fires. On resolution the controller draws four (CR 120 — one at a
///   time; empty-library halts the loop and stamps the CR 704.5b / 120.3
///   loss via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>, mirroring
///   Kozilek, the Great Distortion's refill loop).
/// - <b>Annihilator 4 (CR 702.86)</b>: shipped via
///   <see cref="AnnihilatorFactory.Build"/> — the per-attacker trigger fires
///   on <see cref="CreatureAttacksEvent"/> and routes the four sacrifice
///   picks through <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> when
///   an agent selector is supplied; deterministic first-four-permanents
///   fallback otherwise. A discoverability
///   <see cref="KeywordAbility"/>("Annihilator", arg: 4) marker is stamped
///   alongside (mirrors <see cref="UlamogsCrusherFactory"/>'s posture).
/// - <b>"Put into a graveyard from anywhere" trigger (CR 603.6c /
///   CR 603.6d)</b>: triggered ability over <see cref="CardMovedEvent"/>
///   filtered to <c>e.Card == card &amp;&amp; e.ToZone == Graveyard</c>. The
///   "from anywhere" clause means the origin zone is unconstrained — unlike a
///   plain dies trigger (<see cref="Triggers.OnDies"/>, battlefield-only) it
///   matches a graveyard arrival from any zone, so <c>activeZones</c> spans
///   every zone (the card's <see cref="ICard.Zone"/> has already been stamped
///   to Graveyard by <see cref="ZoneService"/> before the event publishes;
///   CR 603.6d — the trigger "looks back in time" at the prior game state).
///   On resolution the owner shuffles their graveyard into their library
///   (CR 701.20): every graveyard card is moved to the library, then
///   <see cref="LibraryShuffle.ShuffleLibrary"/> shuffles + emits the
///   <see cref="LibraryShuffledEvent"/> (same shared shuffle hook the tutor
///   family uses; mirrors <see cref="EnduranceFactory"/>'s graveyard-to-
///   library move). Note Kozilek targets its OWNER's graveyard (no chosen
///   target), so there is no <see cref="TargetRequest"/>.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Cast trigger + Annihilator +
///   the graveyard-shuffle trigger are attached; nothing registers with a
///   trigger bus. Suitable for dispatcher / structural tests. This is the
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, Func{Player, IPlayerAgent?}?)"/>
///   — fully wired. All three triggers register with the bus; the
///   Annihilator trigger consults <paramref name="agentSelector"/> for the
///   defender's sacrifice picks.
/// </summary>
[CardName("Kozilek, Butcher of Truth")]
public static class KozilekButcherOfTruthFactory
{
    public const string CardName = "Kozilek, Butcher of Truth";
    public const string Slug = "kozilek-butcher-of-truth";
    public const int Power = 12;
    public const int Toughness = 12;
    public const int AnnihilatorValue = 4;

    /// <summary>Number of cards drawn by the on-cast trigger.</summary>
    public const int CastDrawCount = 4;

    /// <summary>
    /// Construct Kozilek with no live wiring. Cast trigger + Annihilator +
    /// the graveyard-shuffle trigger are attached for shape; nothing
    /// registers with any <see cref="TriggerManager"/>. Suitable for
    /// dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, agentSelector: null);

    /// <summary>
    /// Construct Kozilek with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the cast trigger, the
    /// Annihilator trigger, and the graveyard-shuffle trigger register with
    /// the bus so the corresponding events automatically place the abilities
    /// on the stack (CR 603.2).</param>
    /// <param name="agentSelector">When supplied, the Annihilator trigger
    /// consults the defender's
    /// <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> for sacrifice
    /// picks; null falls back to deterministic first-N-permanents.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<Player, IPlayerAgent?>? agentSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Creature, Eldrazi subtype, {10}, 12/12). The JSON carries no
        // abilities — the cast trigger / Annihilator / graveyard-shuffle are
        // layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.6a / CR 603.10.
        //   "When you cast this spell, draw four cards."
        // Self-cast detection: filter SpellCastEvent on e.Spell.Card == card
        // (same pattern as Emrakul / Kozilek the Great Distortion), active in
        // the Stack zone because Kozilek is on the stack as a spell at cast
        // time. The controller is captured off the live event.
        // ----------------------------------------------------------------
        Player? capturedController = null;

        var castCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) =>
            {
                if (!ReferenceEquals(e.Spell.Card, card)) return false;
                capturedController = e.Spell.Controller;
                return true;
            });

        var castEffect = new Effect(
            $"{CardName}: draw four cards (cast trigger)",
            () =>
            {
                var controller = capturedController ?? card.Controller ?? owner;

                // CR 120 — draw one card at a time. Empty-library halts the
                // loop and stamps the CR 704.5b / 120.3 loss condition
                // (mirrors Kozilek the Great Distortion's refill loop).
                for (var i = 0; i < CastDrawCount; i++)
                {
                    var top = controller.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        controller.MarkTriedToDrawFromEmptyLibrary();
                        break;
                    }
                    controller.Zones.Library.RemoveCard(top);
                    controller.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { castEffect },
            // Cast trigger fires while the spell is on the stack — same
            // active-zone posture as Emrakul / Kozilek the Great Distortion.
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        // ----------------------------------------------------------------
        // Annihilator 4 — CR 702.86. Marker for discoverability + the wired
        // trigger (AnnihilatorFactory.Build) so attacks fire through the bus.
        // Same posture as Ulamog's Crusher / Emrakul.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility(
            "Annihilator", card, owner, arg: AnnihilatorValue));
        var annihilator = AnnihilatorFactory.Build(
            source: card,
            n: AnnihilatorValue,
            agentSelector: agentSelector);
        card.AddAbility(annihilator);
        triggers?.RegisterTriggeredAbility(annihilator);

        // ----------------------------------------------------------------
        // "Put into a graveyard from anywhere" trigger — CR 603.6c / 603.6d.
        //   "When Kozilek is put into a graveyard from anywhere, its owner
        //    shuffles their graveyard into their library."
        // CardMovedEvent filtered to ToZone == Graveyard for this card; the
        // origin zone is unconstrained ("from anywhere"), so unlike a plain
        // dies trigger (Triggers.OnDies — battlefield→graveyard only) we do
        // NOT gate on FromZone. ActiveZones spans every zone because the
        // card's Zone has already been stamped to Graveyard by ZoneService
        // before the event publishes (CR 603.6d — the trigger looks back at
        // the prior game state). Recycles the OWNER's graveyard (no chosen
        // target) so there is no TargetRequest.
        // ----------------------------------------------------------------
        var shuffleCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.ToZone == ZoneType.Graveyard);

        var shuffleEffect = new Effect(
            $"{CardName}: owner shuffles their graveyard into their library",
            () =>
            {
                // CR 701.20 — move every graveyard card to the library, then
                // shuffle. Snapshot first because mutating the zone while
                // iterating its backing collection invalidates the
                // enumerator (same guard as EnduranceFactory).
                var graveyardCards = owner.Zones.Graveyard.GetCards().ToList();
                foreach (var c in graveyardCards)
                {
                    owner.Zones.Graveyard.RemoveCard(c);
                    owner.Zones.Library.AddCard(c);
                    c.SetZone(ZoneType.Library);
                }

                // Shared shuffle hook — Fisher-Yates with the registered
                // GameRandom + a LibraryShuffledEvent publish (CR 701.20).
                LibraryShuffle.ShuffleLibrary(owner, $"{CardName} graveyard shuffle");
            });

        var shuffleTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: shuffleCondition,
            effects: new IEffect[] { shuffleEffect },
            // "from anywhere" — the card's Zone is Graveyard at fire time,
            // but list every zone so the active-zone guard never suppresses a
            // legitimate graveyard arrival regardless of origin.
            activeZones: new[]
            {
                ZoneType.Library, ZoneType.Hand, ZoneType.Battlefield,
                ZoneType.Graveyard, ZoneType.Exile, ZoneType.Stack,
                ZoneType.Command,
            });

        card.AddAbility(shuffleTrigger);
        triggers?.RegisterTriggeredAbility(shuffleTrigger);

        return card;
    }
}
