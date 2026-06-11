using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Priest of Forgotten Gods (War of the Spark, {1}{B}).
///
/// Creature — Human Cleric 1/2.
/// Oracle text (Scryfall, verified):
///   "{T}, Sacrifice two other creatures: Any number of target players each
///    lose 2 life and sacrifice a creature of their choice. You add {B}{B}
///    and draw a card."
///
/// ## Implemented (v1)
/// - 1/2 Human Cleric, mana cost {1}{B}.
/// - One activated ability (CR 602.1) whose cost is:
///     - {T} (CR 602.5e, <see cref="AdditionalCost.Tap"/>); and
///     - Sacrifice two other creatures (CR 118.4) — two
///       <see cref="SacrificeAnotherCreatureCost"/> instances, each requiring
///       a creature other than the Priest itself.
/// - Resolution, in oracle order:
///     1. Each affected player loses 2 life (CR 119.3).
///     2. Each affected player sacrifices a creature of their choice
///        (CR 701.16 — sacrifice bypasses Indestructible / regeneration). The
///        affected player's agent drives the "of their choice" pick
///        (<see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>, intent
///        <see cref="BotIntent.Removal"/>), mirroring
///        <see cref="DiabolicEdictFactory"/>; deterministic fallback (no agent,
///        or an illegal pick) = first creature in battlefield order. A player
///        controlling no creature sacrifices nothing (no-op).
///     3. You add {B}{B} (CR 106.1) — <see cref="Player.AddManaToPool"/>.
///     4. You draw a card (CR 120.1).
///
/// ## Reads opponents from the live resolution context (the fix)
/// The "any number of target players" rider reads the affected players from the
/// LIVE game at RESOLUTION via <see cref="ResolutionContext.Game"/>
/// (<c>ctx.Game.AllPlayers</c>, filtered to non-controller, not-lost players) —
/// the same context-driven idiom as <see cref="AmaliaBenavidesAguirreFactory"/>
/// / <see cref="StormbreathDragonFactory"/>. Previously it captured a
/// <c>Func&lt;IReadOnlyList&lt;Player&gt;&gt; opponentsResolver</c> at factory
/// time; the production routed build
/// (<c>GameFacade.BuildDeckCard → NamedCardFactory.Create(name, owner, effects)</c>)
/// dispatched the single-arg shape build, which left that resolver null, so the
/// each-opponent half (life loss + sacrifice) was INERT in real games while the
/// factory-direct tests passed (they injected the resolver). Reading the live
/// context means the rider is correct on BOTH the shape build and the routed
/// prod build, with no captured resolver.
///
/// The affected player's "of their choice" sacrifice pick reads THAT player's
/// agent from <see cref="AgentRegistry"/> at resolution (the live engine
/// registers a per-seat agent there — see <c>GameFacade</c>), with an optional
/// <paramref name="sacrificeAgent"/> override for tests. Deterministic
/// first-creature fallback when neither is available or the pick is illegal.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — canonical build. Wires the activated
///   ability whose rider reads opponents from the live resolution context.
/// - <see cref="Create(Player, Effects.ContinuousEffectsService?)"/> — the
///   effects-aware overload the source generator recognises and the production
///   <c>GameFacade</c> routed build dispatches to (via
///   <see cref="NamedCardFactory.Create(string, Player, Effects.ContinuousEffectsService?)"/>).
///   The Priest registers no continuous effect, so it forwards straight to the
///   canonical overload — its sole purpose is to make the generator emit the
///   effects-aware dispatch arm so the routed prod build wires the ability with
///   the context-reading rider (mirrors the Stormbreath Dragon /
///   Festival Crasher / Kiln Fiend fix).
///
/// ## Deferred (v1 gaps)
/// - <b>"Any number of target players" targeting</b>: the engine's
///   targeting / target-count prompt for players is not wired for this card
///   shape. v1 affects every opponent. For the common two-player game this is
///   identical to choosing "the one opponent"; the optional multi-player
///   downside of being forced to hit every opponent is the same deferral
///   posture as Yawgmoth's historical each-other-player iteration.
/// - <b>Sacrifice-cost target prompt</b>: <see cref="SacrificeAnotherCreatureCost.Target"/>
///   must be set by the agent before payment; v1 falls back to the first
///   eligible creature on the battlefield (deterministic).
/// - <b>Forced sacrifice prompt UI</b>: the affected player's agent receives
///   the full creature list; surfacing the choice to the portal decision
///   panel is deferred (same queue as Diabolic Edict).
/// </summary>
[CardName("Priest of Forgotten Gods")]
public static class PriestOfForgottenGodsFactory
{
    public const string CardName = "Priest of Forgotten Gods";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>
    /// Canonical build. The per-player lose-life / sacrifice rider reads the
    /// affected players from the live resolution context at resolution time.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, sacrificeAgent: null, eventBus: null);

    /// <summary>
    /// Effects-aware overload the source generator recognises and the
    /// <b>production</b> <c>GameFacade</c> routed build dispatches to (via
    /// <see cref="NamedCardFactory.Create(string, Player, Effects.ContinuousEffectsService?)"/>).
    /// The Priest registers no continuous effect, so this forwards straight to
    /// the canonical <see cref="Create(Player)"/> — its sole purpose is to make
    /// the generator emit the effects-aware dispatch arm so the routed prod
    /// build wires the ability (without it the routed build fell through to
    /// single-arg dispatch with a null opponents resolver → the each-opponent
    /// rider was inert; same fix as Stormbreath Dragon / Festival Crasher).
    /// The <paramref name="effects"/> service is intentionally unused; the rider
    /// reads opponents from the live resolution context, not a registered
    /// continuous effect.
    /// </summary>
    public static Creature Create(Player owner, Effects.ContinuousEffectsService? effects) =>
        Create(owner, sacrificeAgent: null, eventBus: null);

    /// <summary>
    /// Construct Priest of Forgotten Gods.
    /// </summary>
    /// <param name="owner">Owner and initial controller of the card.</param>
    /// <param name="sacrificeAgent">Optional agent used to drive each affected
    /// player's "sacrifice a creature of their choice" pick. When null the live
    /// per-player agent is read from <see cref="AgentRegistry"/> at resolution;
    /// when neither is available (or the pick is illegal) the pick falls back
    /// deterministically to the first creature in battlefield order (mirrors
    /// <see cref="DiabolicEdictFactory"/>).</param>
    /// <param name="eventBus">Optional event bus. When supplied, each affected
    /// player's forced "sacrifice a creature of their choice" publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) so aristocrat
    /// payoffs fire on the Priest activation path.</param>
    public static Creature Create(
        Player owner,
        IPlayerAgent? sacrificeAgent,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 1,
            toughness: 2,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Human, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------------
        // Activated ability (CR 602.1):
        //   Cost: {T}, Sacrifice two other creatures
        //   Effect: Any number of target players each lose 2 life and
        //           sacrifice a creature of their choice. You add {B}{B} and
        //           draw a card.
        // --------------------------------------------------------------------

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                // {T} (CR 602.5e).
                AdditionalCost.Tap(card),
                // "Sacrifice two other creatures" (CR 118.4) — two distinct
                // sacrifice-another-creature costs. Each excludes the Priest
                // itself; CostPayment pays them in sequence, so the second
                // cannot re-pick the creature the first already sacrificed.
                // CR 701.16a — thread the bus so each of the two sacrifices
                // fires PermanentSacrificedEvent for "whenever you sacrifice …".
                new SacrificeAnotherCreatureCost(card, eventBus),
                new SacrificeAnotherCreatureCost(card, eventBus),
            },
            effects: new IEffect[]
            {
                // Effect 1+2: each affected player loses 2 life (CR 119.3),
                // then sacrifices a creature of their choice (CR 701.16).
                //
                // The affected players are read from the LIVE game at
                // RESOLUTION (ctx.Game.AllPlayers) — no captured opponents
                // resolver — so this is correct on BOTH the shape build and the
                // production routed build. With no game context (shape-only
                // Resolve) there are no opponents to hit, so the rider is a safe
                // no-op (mirrors Amalia / Stormbreath).
                new Effect(
                    $"{CardName}: each affected player loses 2 life and sacrifices a creature",
                    ctx =>
                    {
                        var controller = card.Controller ?? owner;

                        var players = ctx.Game?.AllPlayers;
                        if (players == null) return ValueTask.CompletedTask;

                        foreach (var p in players)
                        {
                            // "Any number of target players" — v1 affects every
                            // opponent (see class xmldoc deferral). CR 102.1 —
                            // the controller is never a legal pick here.
                            if (ReferenceEquals(p, controller)) continue;
                            if (p.HasLost) continue;

                            // CR 119.3 — life loss happens regardless of whether
                            // the player controls a creature to sacrifice.
                            p.LoseLife(2);

                            // CR 701.16 — "sacrifice a creature of their choice".
                            var creatures = p.Zones.Battlefield.GetCards()
                                .OfType<Creature>()
                                .Cast<ICard>()
                                .ToList();
                            if (creatures.Count == 0) continue;

                            // The affected player's own agent makes the choice:
                            // the explicit test override first, otherwise the
                            // live per-seat agent registered in AgentRegistry.
                            var agent = sacrificeAgent ?? AgentRegistry.Get(p);

                            ICard pick;
                            if (agent != null)
                            {
                                var chosen = agent
                                    .ChooseFromBattlefieldAsync(p, creatures, BotIntent.Removal)
                                    .GetAwaiter().GetResult();

                                // Validate: a creature still on this player's
                                // battlefield. Invalid → deterministic fallback.
                                pick = (chosen != null
                                        && chosen.Zone == ZoneType.Battlefield
                                        && chosen.HasType(CardType.Creature)
                                        && ReferenceEquals(chosen.Controller, p))
                                    ? chosen
                                    : creatures[0];
                            }
                            else
                            {
                                pick = creatures[0];
                            }

                            // CR 701.16 — sacrifice. With a bus, publish a
                            // PermanentSacrificedEvent crediting the affected
                            // player (CR 701.16a) for aristocrat payoffs.
                            if (eventBus != null) Fx.Sacrifice(pick, p, eventBus);
                            else Fx.Sacrifice(pick);
                        }

                        return ValueTask.CompletedTask;
                    }),

                // Effect 3: you add {B}{B} (CR 106.1).
                new Effect(
                    $"{CardName}: you add {{B}}{{B}}",
                    () => owner.AddManaToPool(ManaCost.Parse("{B}{B}"))),

                // Effect 4: you draw a card (CR 120.1).
                new Effect(
                    $"{CardName}: you draw a card",
                    () =>
                    {
                        var top = owner.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null)
                        {
                            // CR 120.3 — drawing from an empty library is noted;
                            // the SBA handles loss at the next opportunity.
                            owner.MarkTriedToDrawFromEmptyLibrary();
                            return;
                        }
                        owner.Zones.Library.RemoveCard(top);
                        owner.Zones.Hand.AddCard(top);
                        top.SetZone(ZoneType.Hand);
                    }),
            });

        card.AddAbility(ability);
        return card;
    }
}
