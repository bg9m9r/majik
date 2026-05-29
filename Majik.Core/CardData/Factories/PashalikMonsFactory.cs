using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pashalik Mons (Mercadian Masques, {3}{R}{R}).
///
/// Legendary Creature — Goblin Warrior 3/3. Oracle text (Scryfall, verified):
///   "Whenever Pashalik Mons or another Goblin you control dies, Pashalik
///    Mons deals 1 damage to any target.
///    {3}{R}, Sacrifice a Goblin: Create two 1/1 red Goblin creature
///    tokens."
///
/// ## Implemented (v1)
/// - 3/3 Legendary Creature — Goblin Warrior at {3}{R}{R}; owner/controller
///   wired. <see cref="CardSupertype.Legendary"/> + Goblin / Warrior
///   subtypes so the Legend Rule (CR 704.5j) and Goblin tribal lord scopes
///   (Goblin Chieftain / Warchief) see Mons correctly.
/// - <b>Dies-trigger (CR 603.1 + CR 700.4)</b>: a <see cref="TriggeredAbility"/>
///   fires on a Goblin the controller controls moving Battlefield →
///   Graveyard. The printed "Pashalik Mons or another Goblin you control"
///   wording collapses to "a Goblin you control" because Mons is itself a
///   Goblin (mirrors Falkenrath Noble's self-name collapse). The trigger
///   carries a single any-target <see cref="TargetRequest"/>; on resolution
///   the source deals 1 damage to the chosen target via
///   <see cref="Fx.DealDamageAny"/> (CR 306.7 — Planeswalker targets convert
///   to loyalty removal). Same ping shape as Mayhem Devil.
/// - <b>Activated ability (CR 602)</b>: <c>{3}{R}, Sacrifice a Goblin:
///   Create two 1/1 red Goblin creature tokens.</c> Cost = a
///   <see cref="ManaCostCost"/> for the <c>{3}{R}</c> mana plus a
///   <see cref="SacrificeAGoblinCost"/>. At resolution the effect mints
///   exactly two 1/1 red Goblin tokens via
///   <see cref="TokenFactory.CreateOnBattlefield"/> (CR 111 / 111.4) —
///   identical token shape to Dragon Fodder / Krenko's Command.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — single-arg dispatcher path. The
///   dies-trigger is attached for shape observability; token creation falls
///   back to raw zone moves (no <see cref="ZoneService"/>) — token-ETB
///   triggers won't auto-fire. Suitable for shape / NamedCardFactory tests.
/// - <see cref="Create(Player, ZoneService?)"/> — token creation funnels
///   through <see cref="ZoneService.MoveCard"/> so
///   <see cref="CardMovedEvent"/> fires per token (Impact Tremors / Soul
///   Warden pickup correctly).
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully
///   wired; registers the dies-trigger with the
///   <see cref="TriggerManager"/> so the bus drives it automatically.
///
/// ## Deferred (v1 gaps)
/// - <b>Last-known-information for the dying Goblin's controller</b>:
///   CR 603.10 — controller should be read from LKI at the moment of death.
///   v1 reads <see cref="Card.Controller"/> off the moved card directly.
///   Same posture as Falkenrath Noble / Blood Artist.
/// - <b>Target prompting</b>: activated-ability + trigger flow doesn't
///   prompt for targets / sacrifice via the v1 dispatcher — callers set
///   <see cref="TriggeredAbility.ChosenTargets"/> and the sacrifice cost's
///   Target before resolution (mirrors Mayhem Devil). A future agent-prompt
///   MVP closes this.
/// </summary>
[CardName("Pashalik Mons")]
public static class PashalikMonsFactory
{
    public const string CardName = "Pashalik Mons";
    public const string PrintedManaCost = "{3}{R}{R}";
    public const int Power = 3;
    public const int Toughness = 3;
    public const int PingDamage = 1;

    /// <summary>The mana portion of the activated ability's cost.</summary>
    public const string ActivatedManaCost = "{3}{R}";

    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Pashalik Mons with no live runtime services. The
    /// dies-trigger is attached to the card shape but not registered with a
    /// <see cref="TriggerManager"/>; token creation uses raw zone moves.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Pashalik Mons with optional <see cref="ZoneService"/>
    /// wiring (token-ETB events) but no trigger registration.
    /// </summary>
    public static Creature Create(Player owner, ZoneService? zoneService) =>
        Create(owner, zoneService, triggers: null);

    /// <summary>
    /// Construct Pashalik Mons with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Optional zone service so each spawned
    /// Goblin token publishes <see cref="CardMovedEvent"/> on ETB.</param>
    /// <param name="triggers">Optional trigger manager so the dies-trigger
    /// is bus-driven automatically.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Dies-trigger — CR 603.1 + CR 700.4.
        //   "Whenever Pashalik Mons or another Goblin you control dies,
        //    Pashalik Mons deals 1 damage to any target."
        // Collapses to "a Goblin you control dies" — Mons is a Goblin, so
        // its own death qualifies (CR 603.6c — self-naming dies trigger
        // reads LKI just before leaving the battlefield).
        // ----------------------------------------------------------------
        TriggeredAbility? diesTrigger = null;
        var diesCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            if (!e.Card.HasSubtype(CardSubtype.Goblin)) return false;
            // CR 109.5 — "you control": only Goblins the controller controls.
            // LKI controller is read off the moved card (v1 — see gap note).
            return ReferenceEquals(e.Card.Controller, card.Controller ?? owner);
        });

        var pingEffect = new Effect(
            $"{CardName}: deal {PingDamage} damage to any target",
            () =>
            {
                if (diesTrigger == null) return;
                if (diesTrigger.ChosenTargets.Count == 0) return;
                if (diesTrigger.ChosenTargets[0].Count == 0) return;
                var target = diesTrigger.ChosenTargets[0][0];
                Fx.DealDamageAny(target, PingDamage);
            });

        diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: diesCondition,
            effects: new IEffect[] { pingEffect },
            // CR 603.6c — keep active in the graveyard so Mons' OWN death
            // still resolves the ping.
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        // ----------------------------------------------------------------
        // {3}{R}, Sacrifice a Goblin: Create two 1/1 red Goblin creature
        // tokens (CR 602 — activated ability; CR 701.16 — sacrifice cost).
        // Mana portion ({3}{R}) is a ManaCostCost; the sacrifice is a
        // SacrificeAGoblinCost (Mons itself is eligible — no "another").
        // ----------------------------------------------------------------
        var tokenEffect = new Effect(
            $"{CardName}: create two 1/1 red Goblin creature tokens",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateGoblinToken(controller, zoneService);
                CreateGoblinToken(controller, zoneService);
            });

        var tokenAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivatedManaCost),
                new SacrificeAGoblinCost(card, owner),
            },
            effects: new IEffect[] { tokenEffect });

        card.AddAbility(tokenAbility);

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/1 red Goblin creature token under
    /// <paramref name="controller"/>'s control. Mirrors Krenko / Dragon
    /// Fodder token minting so "1/1 red Goblin token" stays uniform.
    /// </summary>
    public static Creature CreateGoblinToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Goblin",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Goblin },
            Keywords: null,
            // CR 105 / CR 111.4 — printed "1/1 red Goblin creature token".
            Colors: new[] { ManaColor.Red });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
