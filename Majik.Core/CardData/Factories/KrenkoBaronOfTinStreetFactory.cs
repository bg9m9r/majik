using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Krenko, Baron of Tin Street (Modern Horizons 3,
/// {2}{R}).
///
/// Legendary Creature — Goblin, 3/3. Oracle text (verified against Scryfall):
///   "Haste
///    {T}, Sacrifice an artifact: Put a +1/+1 counter on each Goblin you
///    control.
///    Whenever an artifact is put into a graveyard from the battlefield, you
///    may pay {R}. If you do, create a 1/1 red Goblin creature token. It gains
///    haste until end of turn."
///
/// The base shape (name, Creature, Legendary supertype, Goblin subtype,
/// {2}{R}, 3/3, Haste) is materialised from the embedded JSON definition
/// (<c>krenko-baron-of-tin-street.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (Haste is a JSON keyword →
/// <see cref="KeywordAbility"/>). The two non-trivial abilities are layered
/// on here because the JSON <c>AbilityDefinition</c> schema doesn't yet
/// express a "{T}, Sacrifice an artifact → counter-each-Goblin" activated
/// ability or an "artifact-into-graveyard, may-pay-mana → token" trigger.
///
/// ## Implemented (v1)
/// - 3/3 red Legendary Creature — Goblin at {2}{R} with Haste. Legendary
///   supertype + Goblin subtype wired (via the JSON def) so the Legend Rule
///   (CR 704.5j) and Goblin tribal lord scopes see Krenko correctly. Red is
///   derived from the {R} pip (CR 105 / 202.2).
/// - <b>Activated ability (CR 602)</b>:
///   <c>{T}, Sacrifice an artifact: Put a +1/+1 counter on each Goblin you
///   control.</c> Cost = <see cref="AdditionalCost.Tap"/> +
///   <see cref="SacrificeAnArtifactCost"/> (CR 117 / 701.16). At resolution
///   it puts one +1/+1 counter (CR 122) on every Goblin permanent on the
///   controller's battlefield — INCLUDING Krenko itself (the oracle reads
///   "each Goblin you control" with no "other" qualifier; CR 109.5 — "you
///   control" = controller, not opponents). The set of Goblins is snapshotted
///   at resolution (CR 608.2).
/// - <b>Triggered ability (CR 603.1 / 603.6a)</b>:
///   <c>Whenever an artifact is put into a graveyard from the battlefield,
///   you may pay {R}. If you do, create a 1/1 red Goblin creature token. It
///   gains haste until end of turn.</c> Subscribes to
///   <see cref="CardMovedEvent"/> gated on (a) FromZone = battlefield,
///   (b) ToZone = graveyard, (c) the moved card is an Artifact. This fires
///   regardless of WHY the artifact left (sacrifice, destroy, combat death)
///   — the oracle says "is put into a graveyard from the battlefield", not
///   "is sacrificed" — so the same inline-predicate shape as
///   <see cref="Triggers.OnDies"/> is used but with an Artifact type check
///   instead of source identity. The optional "you may pay {R}" rider
///   consults the controller's <see cref="IPlayerAgent"/> (Mentor of the
///   Meek posture, CR 117.5); agent-less callers auto-pay if able. When paid
///   it creates one 1/1 red Goblin token (CR 111 / 111.4) granted the Haste
///   keyword (the printed "gains haste until end of turn" — for a token
///   minted during the controller's turn, the permanent Haste keyword is
///   functionally equivalent to the EOT grant; see Goblin Rabblemaster's
///   hasty-token posture).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — single-arg dispatcher path. Both
///   abilities are attached for shape observability; the trigger is not
///   registered with any <see cref="TriggerManager"/> and tokens land via
///   raw zone moves (no <see cref="ZoneService"/>). Suitable for shape /
///   dispatch tests.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?, IEventBus?)"/>
///   — fully-wired overload. The artifact-death trigger registers with the
///   <see cref="TriggerManager"/>; the <see cref="ZoneService"/> funnels the
///   spawned Goblin token through <see cref="ZoneService.MoveCard"/> so
///   <see cref="Events.CardMovedEvent"/> fires on ETB; the
///   <see cref="IEventBus"/> lets the sacrifice cost publish a
///   <see cref="PermanentSacrificedEvent"/>.
/// </summary>
[CardName("Krenko, Baron of Tin Street")]
public static class KrenkoBaronOfTinStreetFactory
{
    public const string CardName = "Krenko, Baron of Tin Street";
    public const string Slug = "krenko-baron-of-tin-street";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Krenko, Baron of Tin Street with no live wiring. Both
    /// abilities are attached for shape observability; the trigger is not
    /// registered with any <see cref="TriggerManager"/> and tokens land via
    /// raw zone moves. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null, eventBus: null);

    /// <summary>
    /// Construct Krenko, Baron of Tin Street with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the artifact-death trigger is
    /// registered so a matching <see cref="CardMovedEvent"/> (artifact →
    /// graveyard from the battlefield) automatically queues the
    /// may-pay-{R}-then-create-token effect.</param>
    /// <param name="zoneService">Optional zone service so the spawned Goblin
    /// token publishes <see cref="Events.CardMovedEvent"/> on ETB. When null,
    /// the token is placed on the battlefield via raw zone moves.</param>
    /// <param name="eventBus">Optional event bus so the
    /// <see cref="SacrificeAnArtifactCost"/> publishes a
    /// <see cref="PermanentSacrificedEvent"/> when paid.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Legendary supertype, Goblin subtype, {2}{R}, 3/3, Haste keyword).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {T}, Sacrifice an artifact: Put a +1/+1 counter on each Goblin you
        // control (CR 602 — activated ability).
        //
        // Counter semantics:
        //   - Snapshotted at resolution (CR 608.2).
        //   - INCLUDES Krenko itself — oracle reads "each Goblin you control"
        //     with no "other" qualifier; Krenko is a Goblin he controls.
        //   - Goblin permanents on the controller's battlefield only
        //     (CR 109.5 — "you control" = controller, not opponents).
        // ----------------------------------------------------------------
        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on each Goblin you control",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 608.2 — snapshot the Goblins to a list before mutating,
                // so the iteration set is fixed at resolution.
                var goblins = controller.Zones.Battlefield.GetCards()
                    .OfType<Permanent>()
                    .Where(c => c.HasSubtype(CardSubtype.Goblin))
                    .ToList();

                foreach (var goblin in goblins)
                {
                    goblin.Counters.Add(CounterType.PlusOnePlusOne); // CR 122
                }
            });

        var counterAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(card),
                new SacrificeAnArtifactCost(eventBus: eventBus),
            },
            effects: new IEffect[] { counterEffect });

        card.AddAbility(counterAbility);

        // ----------------------------------------------------------------
        // Whenever an artifact is put into a graveyard from the battlefield,
        // you may pay {R}. If you do, create a 1/1 red Goblin creature token.
        // It gains haste until end of turn (CR 603.1 / 603.6a).
        //
        // The trigger reads the event, NOT source identity — it fires for
        // ANY artifact (including Krenko-sac'd artifacts and combat/destroy
        // deaths), matching the printed "is put into a graveyard from the
        // battlefield". Same predicate shape as Triggers.OnDies but with an
        // Artifact type check rather than ReferenceEquals(source).
        // ----------------------------------------------------------------
        var artifactDiesCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) =>
                e.FromZone == ZoneType.Battlefield
                && e.ToZone == ZoneType.Graveyard
                && e.Card.HasType(CardType.Artifact));

        var tokenEffect = new Effect(
            $"{CardName}: may pay {{R}} → create a 1/1 red Goblin token with haste",
            async ctx =>
            {
                // CR 603.6c — defence-in-depth; activeZones already gates the
                // event match to the battlefield.
                if (card.Zone != ZoneType.Battlefield) return;

                var controller = card.Controller ?? owner;

                // "You may pay {R}" — consult the controller's agent.
                // Agent-less fallback: auto-pay if able (Mentor of the Meek /
                // Lightning Rift posture; CR 117.5).
                var redCost = ManaCost.Parse("{R}");
                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                bool pay;
                if (agent != null)
                {
                    pay = await agent.ChooseYesNoAsync(
                        "Pay {R} to create a 1/1 red Goblin token with haste?",
                        BotIntent.Token).ConfigureAwait(false);
                }
                else
                {
                    pay = true;
                }

                if (!pay) return;

                // CR 117.5 — optional may-pay; the trigger fizzles when the
                // mana isn't available.
                if (!controller.PayMana(redCost)) return;

                CreateHastyGoblinToken(controller, zoneService);
            });

        var artifactDiesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: artifactDiesCondition,
            effects: new IEffect[] { tokenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(artifactDiesTrigger);
        triggers?.RegisterTriggeredAbility(artifactDiesTrigger);

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/1 red Goblin creature token with the
    /// Haste keyword under <paramref name="controller"/>'s control. The
    /// printed "gains haste until end of turn" is modelled as the permanent
    /// Haste keyword on the token: for a token minted during the controller's
    /// turn it is functionally equivalent (the token can attack this turn;
    /// after this turn haste no longer matters). Same hasty-token posture as
    /// <see cref="GoblinRabblemasterFactory"/>.
    /// </summary>
    public static Creature CreateHastyGoblinToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Goblin",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Goblin },
            // CR 702.10 — "It gains haste until end of turn." Stamped as the
            // permanent Haste keyword (functionally equivalent for a
            // this-turn token; Goblin Rabblemaster posture).
            Keywords: new[] { "Haste" },
            // CR 105 / CR 111.4 — printed "1/1 red Goblin creature token".
            Colors: new[] { ManaColor.Red });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
