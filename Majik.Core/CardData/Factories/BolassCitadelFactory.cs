using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bolas's Citadel (War of the Spark, {3}{B}{B}{B}).
///
/// Legendary Artifact. Oracle text (verified against Scryfall):
///   "You may look at the top card of your library any time.
///    You may play lands and cast spells from the top of your library. If you
///    cast a spell this way, pay life equal to its mana value rather than pay
///    its mana cost.
///    {T}, Sacrifice ten nonland permanents: Each opponent loses 10 life."
///
/// ## Implemented
/// - <b>Play lands and cast spells from the top of your library</b>
///   (CR 601.3e / CR 305.6 / CR 715.4): a battlefield-gated continuous
///   permission registered into <see cref="LibraryTopPlayPermissions"/> by a
///   <see cref="LibraryTopPlayStaticEffect"/> while the Citadel is on the
///   battlefield (revoked on leave, CR 603.6e). The grant is
///   <see cref="TopPlayFilter.Any"/> + reveal-top — covering BOTH the land-play
///   half (a land on top is playable as the land for the turn,
///   <see cref="LibraryTopPlayPermissions.MayPlayTopCard"/>) and the nonland
///   cast half (<see cref="LibraryTopPlayPermissions.MayCastTopCard"/>).
/// - <b>"If you cast a spell this way, pay life equal to its mana value rather
///   than pay its mana cost."</b> (CR 118.9 / CR 116.3a): the grant carries a
///   <see cref="PayLifeEqualToManaValueAlternativeCost"/> factory, so a top-cast
///   under THIS grant is REQUIRED to use that alternative cost
///   (<see cref="LibraryTopPlayPermissions.MandatoryTopCastAltCostFor"/>). The
///   <see cref="Majik.Core.Players.Agents.HeuristicBotAgent"/> routes the
///   top-cast through <see cref="Majik.Core.Game.SpellCastFlow"/> with the
///   pay-life alt cost instead of the printed mana cost; the life (equal to the
///   spell's mana value) is paid on resolution (CR 118.8). No mana is spent, so
///   the spell is a "free cast" for CR 118 mana-spent payoffs.
/// - <b>{T}, Sacrifice ten nonland permanents: Each opponent loses 10 life</b>
///   (CR 602.1): an <see cref="ActivatedAbility"/> with
///   <see cref="AdditionalCost.Tap"/> +
///   <see cref="SacrificeNNonlandPermanentsCost"/>(10); on resolution every
///   opponent loses 10 life (CR 119.3, routed through
///   <see cref="Player.LoseLife"/> so life-loss triggers fire).
///
/// ## Production wiring
/// The live cast/play-from-top grant requires the per-game
/// <see cref="ContinuousEffectsService"/>'s event bus, so the grant follows the
/// Citadel in / out of the battlefield. The effects-aware overload
/// <see cref="Create(Player, ContinuousEffectsService)"/> — the overload the
/// production source-generated dispatch invokes — reads
/// <see cref="ContinuousEffectsService.EventBus"/> and attaches the lifecycle.
/// The single-arg <see cref="Create(Player)"/> path attaches description-only
/// markers + the sacrifice ability for shape / dispatch tests.
///
/// ## Deferred (v1 gaps, isolated)
/// - <b>"Look at the top card any time"</b> as an interactive peek surface is
///   modelled as the reveal-top rider on the registry grant (the top is public
///   to the controller); no separate hidden-peek UI affordance.
/// - <b>X-spell mana value</b>: a top-cast X spell's life cost reads its
///   non-X mana value (X resolves to 0 in the printed cost off the stack). See
///   <see cref="PayLifeEqualToManaValueAlternativeCost"/> xmldoc.
/// </summary>
[CardName("Bolas's Citadel")]
public static class BolassCitadelFactory
{
    public const string CardName = "Bolas's Citadel";
    public const string PrintedManaCost = "{3}{B}{B}{B}";

    public const string LookTopDescription =
        "You may look at the top card of your library any time.";

    public const string PlayCastFromTopDescription =
        "You may play lands and cast spells from the top of your library. If you "
        + "cast a spell this way, pay life equal to its mana value rather than pay "
        + "its mana cost.";

    public const string SacrificeAbilityDescription =
        "{T}, Sacrifice ten nonland permanents: Each opponent loses 10 life.";

    /// <summary>CR 701.16 — the activated ability sacrifices ten nonland permanents.</summary>
    public const int SacrificeCount = 10;

    /// <summary>CR 119.3 — each opponent loses this much life on resolution.</summary>
    public const int LifeLossPerOpponent = 10;

    /// <summary>
    /// Shape-only build (no live cast/play-from-top grant). The description
    /// markers and the {T}, Sacrifice ten ability are attached for shape /
    /// dispatch tests. Use <see cref="Create(Player, ContinuousEffectsService)"/>
    /// (the production routing overload) for the live cast-from-top permission.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, continuousEffects: null, opponentResolver: null);

    /// <summary>
    /// Effects-aware build — the overload the production
    /// <c>NamedCardFactory.CreateGeneratedWithEffects</c> dispatch invokes.
    /// When <paramref name="continuousEffects"/> carries an event bus, the
    /// play-lands-and-cast-spells-from-top grant (carrying the mandatory
    /// pay-life-equal-to-mana-value alt cost) is registered (and revoked) as the
    /// Citadel enters / leaves the battlefield. The drain ability's opponent set
    /// is derived from the live <see cref="ContinuousEffectsService"/> when one
    /// is supplied (its <c>AllPlayers</c> surface), else no-ops (CR 109.5).
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? continuousEffects)
        => Create(owner, continuousEffects, opponentResolver: null);

    /// <summary>
    /// Construct Bolas's Citadel.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">When non-null, its event bus drives the
    /// live cast/play-from-top grant lifecycle.</param>
    /// <param name="opponentResolver">Returns the live player list at resolution
    /// time for the "each opponent loses 10 life" drain (CR 109.5 — non-targeted,
    /// global). The controller is filtered out. Null ⇒ the drain no-ops (shape /
    /// dispatch path) — same posture as the sibling each-opponent factories
    /// (Kroxa, Marauding Blight Priest).</param>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var citadel = new Artifact(
            name: CardName,
            manaCost: PrintedManaCost,
            supertypes: new[] { CardSupertype.Legendary });
        citadel.SetOwner(owner);
        citadel.SetController(owner);

        // CR 604.1 — description-only static markers (UI / shape surface). Live
        // behaviour is the LibraryTopPlayPermissions grant wired below.
        citadel.AddAbility(new StaticAbility(
            source: citadel, controller: owner,
            description: LookTopDescription,
            isActiveCheck: () => citadel.Zone == ZoneType.Battlefield));
        citadel.AddAbility(new StaticAbility(
            source: citadel, controller: owner,
            description: PlayCastFromTopDescription,
            isActiveCheck: () => citadel.Zone == ZoneType.Battlefield));

        // CR 602.1 — {T}, Sacrifice ten nonland permanents: Each opponent
        // loses 10 life.
        var drainEffect = new Effect(
            $"{CardName}: each opponent loses {LifeLossPerOpponent} life",
            () =>
            {
                var controller = citadel.Controller ?? owner;
                var players = opponentResolver?.Invoke();
                if (players == null) return;
                foreach (var opponent in players)
                {
                    if (opponent == null || ReferenceEquals(opponent, controller)) continue;
                    if (!opponent.HasLost) opponent.LoseLife(LifeLossPerOpponent);
                }
            });

        citadel.AddAbility(new ActivatedAbility(
            source: citadel,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(citadel),
                new SacrificeNNonlandPermanentsCost(SacrificeCount),
            },
            effects: new IEffect[] { drainEffect }));

        // CR 601.3e / CR 305.6 / CR 715.4 / CR 118.9 — live "may play lands +
        // cast spells from the top, revealed; pay life equal to mana value for a
        // top-cast" grant, battlefield-gated.
        var bus = continuousEffects?.EventBus;
        if (bus != null)
        {
            new LibraryTopPlayStaticEffect(
                source: citadel,
                controller: owner,
                filter: TopPlayFilter.Any,
                eventBus: bus,
                revealsTop: true,
                topCastAltCostFactory: () => new PayLifeEqualToManaValueAlternativeCost())
                .Attach();
        }

        return citadel;
    }

    /// <summary>
    /// Bolas's Citadel's "look at the top card any time" rider as a
    /// controller-side peek (CR 401.4). Returns the top card of
    /// <paramref name="controller"/>'s library, or null when empty. Pure read.
    /// </summary>
    public static ICard? LookAtTopOfLibrary(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Library.GetCards().FirstOrDefault();
    }
}
