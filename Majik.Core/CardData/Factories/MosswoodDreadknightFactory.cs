using Majik.Core.Abilities;
using Majik.Core.CardData.Adventures;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mosswood Dreadknight // Dread Whispers (Wilds
/// of Eldraine, {B/G}{B/G}).
///
/// ## Card text
/// - Mosswood Dreadknight — Creature — Human Knight {B/G}{B/G}, 3/2.
///     "Trample
///      When this creature dies, return it to its owner's hand."
/// - Dread Whispers (Adventure) — Sorcery — Adventure {B/G}.
///     "Look at the top two cards of your library. Put one of them into
///      your hand and the other into your graveyard."
///
/// ## Implemented (v1)
///
/// - 3/2 Human Knight at {B/G}{B/G}. Hybrid pip format follows the same
///   shape <see cref="FulminatorMageFactory"/> uses ({B/R}{B/R}) — the
///   engine's <see cref="ManaCost"/> parser derives colour identity
///   (black + green) from the pip set.
/// - <b>Trample</b> keyword marker via <see cref="KeywordAbility"/>
///   (CR 702.19) — same wiring as
///   <see cref="AmpedRaptorFactory"/>.
/// - <b>Dies trigger (CR 603.6c / CR 700.4)</b> — Battlefield → Graveyard
///   <see cref="CardMovedEvent"/> filtered to this card via
///   <see cref="Abilities.Triggers.OnDies"/>. Active zones =
///   {Battlefield, Graveyard} so the trigger still matches once the card
///   has been moved to the graveyard by <see cref="ZoneService"/> prior
///   to the <see cref="CardMovedEvent"/> publish (Wurmcoil Engine /
///   Matter Reshaper posture; CR 603.6d "looks back").
/// - On resolution: route the dying card from its current graveyard back
///   to its <see cref="Card.Owner"/>'s hand via
///   <see cref="ZoneService.MoveCard"/> when supplied (so a downstream
///   <see cref="CardMovedEvent"/> fires for any "whenever a card enters
///   your hand" subscribers), or raw zone manipulation on the shape-only
///   path. The "return to owner's hand" target (CR 400.7 — "owner") is
///   <see cref="ICard.Owner"/>, not the dying controller's hand, which
///   matches the printed text for cards stolen via Threaten / Control
///   Magic before dying.
///
/// - <b>Adventure cast pipeline (CR 715)</b>: the Dread Whispers half is
///   attached as an <see cref="AdventureSpec"/> on the card. The cast
///   flow (<see cref="Costs.AdventureAlternativeCost"/> +
///   <see cref="Game.SpellCastFlow"/>) routes Dread Whispers through the
///   standard Rule 601 sequence with the Adventure mana cost, exiles
///   the card on resolve (CR 715.3d), and grants the owner a runtime
///   "may cast from exile" permission for the printed Mosswood
///   Dreadknight cost via
///   <see cref="Card.GrantRuntimeExileCast"/>. Mirrors
///   <see cref="BonecrusherGiantFactory"/>'s wiring.
/// - <b>Dread Whispers resolution</b>: <see cref="BuildAdventureSpell"/>
///   returns a no-target <see cref="Game.SpellDefinition"/> whose effect
///   factory peeks the top two cards of the caster's library; v1
///   deterministic policy puts the <b>top</b> card into the caster's
///   <see cref="Player.Zones"/>.Hand and the <b>second</b> card into the
///   caster's graveyard. Both moves are raw zone manipulation (same
///   posture as <see cref="ExploreFactory"/> / <see cref="PreordainFactory"/>'s
///   shape-only path); a future "agent picks which goes where" prompt
///   gates on a Brainstorm-shape pick-one-of-two gesture.
///
/// ## Lifecycle
///
/// - <see cref="Create(Player)"/> — shape only. Trigger attached for
///   shape observability; not registered with any
///   <see cref="TriggerManager"/>; the dies-resolution uses raw zone
///   manipulation. Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully
///   wired. Trigger registers with <paramref name="triggers"/>; the
///   return-to-hand move routes through <paramref name="zones"/>.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Agent prompt for pick-one-of-two</b> (Dread Whispers): v1
///   deterministically routes the top library card to hand and the
///   second to graveyard. Same deferral as Preordain's deterministic
///   sequencing.
/// - <b>"Return to its owner's hand" replacement-effect interplay</b>:
///   the dies trigger is a triggered ability, not a replacement effect,
///   so cards like Rest in Peace / Leyline of the Void that replace the
///   graveyard move with exile preempt the trigger entirely (CR 614.6 —
///   replacement effects apply before triggered abilities trigger; if
///   the creature never reaches the graveyard, the dies trigger never
///   fires). v1 inherits the engine's existing replacement-effect
///   ordering with no new wiring.
/// </summary>
[CardName("Mosswood Dreadknight")]
public static class MosswoodDreadknightFactory
{
    public const string CardName = "Mosswood Dreadknight";
    public const string PrintedManaCost = "{B/G}{B/G}";
    public const int Power = 3;
    public const int Toughness = 2;

    public const string AdventureName = "Dread Whispers";
    public const string AdventureManaCost = "{B/G}";

    /// <summary>
    /// Construct Mosswood Dreadknight with no live wiring. The dies
    /// trigger is attached for shape observability; not registered with
    /// any <see cref="TriggerManager"/>; the return-to-hand move uses
    /// raw zone manipulation. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Mosswood Dreadknight with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the dies-resolution
    /// return-to-hand move routes through <see cref="ZoneService.MoveCard"/>
    /// so a downstream <see cref="CardMovedEvent"/> fires for "whenever
    /// a card enters your hand" subscribers.</param>
    /// <param name="triggers">When supplied, the dies trigger registers
    /// so a qualifying <see cref="CardMovedEvent"/> automatically queues
    /// it on the stack (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.19 — Trample. KeywordAbility marker consumed by
        // Majik.Core.Combat.CombatAbilities.HasTrample.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // ----------------------------------------------------------------
        // Dies trigger — CR 603.6c / CR 700.4.
        //   "When this creature dies, return it to its owner's hand."
        //
        // ActiveZones = {Battlefield, Graveyard} — the trigger's
        // zone-guard must still match after ZoneService has stamped the
        // card's Zone = Graveyard before publishing the CardMovedEvent
        // (Wurmcoil / Matter Reshaper posture).
        //
        // CR 400.7 — "owner". The return target is card.Owner's hand, not
        // the dying controller's hand, so a Threaten-stolen Dreadknight
        // still returns to its true owner.
        // ----------------------------------------------------------------
        var capturedZones = zones;
        var diesEffect = new Effect(
            $"{CardName}: return to its owner's hand",
            () =>
            {
                // Trigger fires on B → G move; the card now lives in the
                // owner's graveyard. Move it from graveyard → hand.
                var dest = card.Owner ?? owner;

                if (capturedZones != null)
                {
                    capturedZones.MoveCard(
                        card,
                        ZoneType.Graveyard,
                        ZoneType.Hand,
                        controller: null);
                }
                else
                {
                    // Raw zone manipulation — shape-only path.
                    dest.Zones.Graveyard.RemoveCard(card);
                    dest.Zones.Hand.AddCard(card);
                    card.SetZone(ZoneType.Hand);
                }
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        // ----------------------------------------------------------------
        // CR 715 — attach the Dread Whispers Adventure half. The
        // AdventureSpec only carries the alternative characteristics +
        // an effects-factory closure; the cast path is driven by
        // AdventureAlternativeCost + SpellCastFlow.
        // ----------------------------------------------------------------
        card.AdventureSpec = new AdventureSpec(
            Name: AdventureName,
            ManaCost: ManaCost.Parse(AdventureManaCost),
            AdventureType: CardType.Sorcery,
            BuildDefinition: BuildAdventureSpell);

        return card;
    }

    /// <summary>
    /// Build the standalone Dread Whispers <see cref="Game.SpellDefinition"/>
    /// — no target requests; on resolve the caster's top library card is
    /// moved to hand and the second is moved to graveyard. v1
    /// deterministic policy (top → hand, second → graveyard); the
    /// printed text grants the caster a "pick one of two" choice that
    /// will gate on a Brainstorm-shape agent prompt when wired.
    /// </summary>
    /// <param name="caster">The controller of Dread Whispers.</param>
    /// <param name="targetResolver">Unused (no targets), kept for API
    /// symmetry with other Adventure factories (Stomp / Swift End).</param>
    public static SpellDefinition BuildAdventureSpell(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect(
                    "Dread Whispers: top of library → hand; second → graveyard",
                    () =>
                    {
                        var libCards = caster.Zones.Library.GetCards().ToList();
                        // CR 701.16 — "look at" is private; we don't emit
                        // a public reveal. The deterministic v1 policy
                        // routes top → hand, second → graveyard.
                        if (libCards.Count >= 1)
                        {
                            var top = libCards[0];
                            caster.Zones.Library.RemoveCard(top);
                            caster.Zones.Hand.AddCard(top);
                            top.SetZone(ZoneType.Hand);
                        }
                        if (libCards.Count >= 2)
                        {
                            var second = libCards[1];
                            caster.Zones.Library.RemoveCard(second);
                            caster.Zones.Graveyard.AddCard(second);
                            second.SetZone(ZoneType.Graveyard);
                        }
                        // Library < 2 cards: do as much as possible
                        // (CR 608.2b). Empty library does NOT stamp the
                        // loss flag — Dread Whispers "looks", it does
                        // not draw (CR 121.1 applies only to draws).
                    }),
            });
    }
}
