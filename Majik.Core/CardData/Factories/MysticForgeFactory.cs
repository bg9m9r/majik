using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mystic Forge (Modern Horizons, {4}).
///
/// Artifact. Oracle text:
///   "You may look at the top card of your library any time.
///    You may cast artifact spells and colorless spells from the top of your
///    library.
///    {T}, Pay 1 life: Exile the top card of your library."
///
/// ## Implemented
/// - <b>Cast artifact / colorless spells from the top of your library</b>
///   (CR 601.3e): a battlefield-gated continuous permission registered into
///   <see cref="LibraryTopPlayPermissions"/> by two
///   <see cref="LibraryTopPlayStaticEffect"/> lifecycles while Mystic Forge is
///   on the battlefield (revoked on leave, CR 603.6e) — one
///   <see cref="TopPlayFilter.Artifacts"/> grant (also reveals the top, the
///   "look at the top card any time" clause, CR 715.4) and one
///   <see cref="TopPlayFilter.Colorless"/> grant. When the controller's top
///   library card is an artifact OR a colorless card they may cast it from the
///   library: the card goes onto the stack via
///   <see cref="Majik.Core.Game.SpellCastFlow"/> (which already moves a card
///   from whatever zone it occupies onto the stack and stamps the
///   "cast from library" sentinel, CR 113.5), paid with its printed cost. The
///   <see cref="Majik.Core.Players.Agents.HeuristicBotAgent"/> enumerates the
///   castable top card as a printed-cost bid via
///   <see cref="LibraryTopPlayPermissions.CastableSpellFromTop"/>.
/// - <b>{T}, Pay 1 life: Exile the top card of your library</b> (CR 602.1):
///   an <see cref="ActivatedAbility"/> with <see cref="AdditionalCost.Tap"/> +
///   <see cref="PayLifeCost"/>(1); on resolution the top library card is moved
///   to exile through <see cref="ZoneService"/> when one is supplied (raw zone
///   move otherwise).
///
/// ## Production wiring
/// The live cast-from-top grant requires the per-game
/// <see cref="ContinuousEffectsService"/>'s event bus (so the grant follows
/// Mystic Forge in/out of the battlefield). The effects-aware overload
/// <see cref="Create(Player, ContinuousEffectsService)"/> — the overload the
/// production source-generated dispatch invokes — reads
/// <see cref="ContinuousEffectsService.EventBus"/> and attaches the lifecycle,
/// so the permission is genuinely live in a real match (it does NOT share the
/// "needs a separately-threaded bus the dispatch doesn't supply" residual that
/// Courser's bus-only overload has). The single-arg <see cref="Create(Player)"/>
/// shape path attaches description-only markers + the activated ability for
/// dispatch / shape tests.
///
/// ## Deferred (v1 gaps, isolated)
/// - <b>"Look at the top card any time"</b> as an interactive peek surface is
///   modelled as the reveal-top rider on the registry grant (the top is public
///   to the controller); there is no separate hidden-peek UI affordance.
/// - <b>Sorcery-speed for sorceries cast from the top</b>: a sorcery-typed
///   artifact/colorless card cast from the top still obeys the normal
///   sorcery-speed timing gate (the bot only proposes it during a sorcery
///   window) — Mystic Forge does not grant flash.
/// </summary>
[CardName("Mystic Forge")]
public static class MysticForgeFactory
{
    public const string CardName = "Mystic Forge";
    public const string PrintedManaCost = "{4}";

    public const string LookTopDescription =
        "You may look at the top card of your library any time.";

    public const string CastFromTopDescription =
        "You may cast artifact spells and colorless spells from the top of your library.";

    public const string ExileTopDescription =
        "{T}, Pay 1 life: Exile the top card of your library.";

    /// <summary>Life paid to activate the exile ability (CR 118.8).</summary>
    public const int ExileLifeCost = 1;

    /// <summary>
    /// Shape-only build (no live cast-from-top grant). The description markers
    /// and the {T}, Pay 1 life exile ability are attached for shape / dispatch
    /// tests. Use <see cref="Create(Player, ContinuousEffectsService)"/> (the
    /// production routing overload) for the live cast-from-top permission.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, continuousEffects: null);

    /// <summary>
    /// Effects-aware build — the overload the production
    /// <c>NamedCardFactory.CreateGeneratedWithEffects</c> dispatch invokes.
    /// When <paramref name="continuousEffects"/> carries an event bus, the
    /// cast-artifact/colorless-from-top grant is registered (and revoked) as
    /// Mystic Forge enters / leaves the battlefield.
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var forge = new Artifact(CardName, PrintedManaCost);
        forge.SetOwner(owner);
        forge.SetController(owner);

        // ----------------------------------------------------------------
        // CR 604.1 — description-only static markers (UI / shape surface).
        // Live behaviour is the LibraryTopPlayPermissions grant wired below.
        // ----------------------------------------------------------------
        forge.AddAbility(new StaticAbility(
            source: forge,
            controller: owner,
            description: LookTopDescription,
            isActiveCheck: () => forge.Zone == ZoneType.Battlefield));

        forge.AddAbility(new StaticAbility(
            source: forge,
            controller: owner,
            description: CastFromTopDescription,
            isActiveCheck: () => forge.Zone == ZoneType.Battlefield));

        // ----------------------------------------------------------------
        // CR 602.1 — {T}, Pay 1 life: Exile the top card of your library.
        // ----------------------------------------------------------------
        var exileEffect = new Effect(
            $"{CardName}: exile the top card of your library",
            () => ExileTopCard(forge));

        var exileAbility = new ActivatedAbility(
            source: forge,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(forge),
                new PayLifeCost(ExileLifeCost),
            },
            effects: new IEffect[] { exileEffect });

        forge.AddAbility(exileAbility);

        // ----------------------------------------------------------------
        // CR 601.3e — live "cast artifact / colorless spells from the top"
        // grant, battlefield-gated, registered now if Mystic Forge is already
        // on the battlefield and re-synced on every move of the source. Needs
        // the event bus from the per-game ContinuousEffectsService.
        // ----------------------------------------------------------------
        var bus = continuousEffects?.EventBus;
        if (bus != null)
        {
            // Artifacts grant — also carries the reveal-top rider ("look at the
            // top card any time", CR 715.4).
            new LibraryTopPlayStaticEffect(
                source: forge,
                controller: owner,
                filter: TopPlayFilter.Artifacts,
                eventBus: bus,
                revealsTop: true).Attach();

            // Colorless grant — covers colorless nonartifact spells (Eldrazi).
            new LibraryTopPlayStaticEffect(
                source: forge,
                controller: owner,
                filter: TopPlayFilter.Colorless,
                eventBus: bus,
                revealsTop: false).Attach();
        }

        return forge;
    }

    /// <summary>
    /// Move the top card of <paramref name="forge"/>'s controller's library to
    /// exile (CR 406). Pure zone move — no replacement / trigger routing beyond
    /// the normal zone-change event.
    /// </summary>
    private static void ExileTopCard(Artifact forge)
    {
        var controller = forge.Controller ?? forge.Owner;
        if (controller == null) return;

        var top = controller.Zones.Library.GetCards().FirstOrDefault();
        if (top == null) return;

        controller.Zones.Library.RemoveCard(top);
        controller.Zones.Exile.AddCard(top);
        top.SetZone(ZoneType.Exile);
    }
}
