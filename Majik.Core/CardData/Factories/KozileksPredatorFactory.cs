using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kozilek's Predator (Rise of the Eldrazi / Battle
/// for Zendikar Commander, {3}{G}).
///
/// Creature — Eldrazi Drone 3/3 (green). Oracle text (verified against
/// Scryfall):
///   "When this creature enters, create two 0/1 colorless Eldrazi Spawn
///    creature tokens. They have \"Sacrifice this token: Add {C}.\""
///
/// The card's base shape (name, Creature, Eldrazi + Drone subtypes,
/// {3}{G}, 3/3) is materialised from the embedded JSON definition
/// (<c>kozileks-predator.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed ETB trigger is
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// yet express ETB triggers, so it lives in the factory (same posture as
/// <see cref="GlaringFleshrakerFactory"/> and the other JSON-backed Eldrazi
/// Spawn makers).
///
/// ## Implemented (v1)
///
/// - <b>ETB triggered ability (CR 603.6a)</b> over a
///   <see cref="CardMovedEvent"/> filtered to (this card, ToZone =
///   Battlefield). On resolution the effect mints <b>two</b> 0/1 colorless
///   Eldrazi Spawn creature tokens (CR 111.10) under Kozilek's Predator's
///   controller via <see cref="TokenFactory.CreateEldraziSpawn"/> — the
///   shared Eldrazi Spawn primitive, which ships each token with the
///   "Sacrifice this token: Add {C}." mana ability. This mirrors
///   <see cref="EldraziSkyspawnerFactory"/>'s single-Spawn ETB, count = 2.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached
///   for shape observability; not registered with any
///   <see cref="TriggerManager"/>, no <see cref="ZoneService"/> wiring. This
///   is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully
///   wired. The trigger registers with <paramref name="triggers"/>; each
///   Spawn token's ETB routes through <paramref name="zones"/> so its own
///   <see cref="CardMovedEvent"/> publishes (downstream ETB subscribers see
///   the Spawn arrivals).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Eldrazi Spawn sacrifice cost</b> — each minted Spawn's
///   "Sacrifice this token: Add {C}." is wired as a plain
///   <see cref="ManaAbility"/> producing {C} without enforcing the
///   sacrifice cost (documented deferral inherited from
///   <see cref="TokenFactory.CreateEldraziSpawn"/>, same gap as Treasure /
///   Food sac costs).
/// </summary>
[CardName("Kozilek's Predator")]
public static class KozileksPredatorFactory
{
    public const string CardName = "Kozilek's Predator";
    public const string Slug = "kozileks-predator";
    public const int SpawnCount = 2;

    /// <summary>
    /// Construct Kozilek's Predator with no live wiring. The ETB trigger is
    /// attached structurally (correct card shape for factory-shape /
    /// dispatch tests) but NOT registered with a
    /// <see cref="TriggerManager"/>; the Spawn tokens mint with a raw zone
    /// move (null <see cref="ZoneService"/>). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Kozilek's Predator with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, each Spawn token's ETB routes
    /// through <see cref="ZoneService.MoveCardTo"/> so
    /// <see cref="CardMovedEvent"/> publishes for any zone-change
    /// subscribers.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers with
    /// the bus so the corresponding <see cref="CardMovedEvent"/> lands the
    /// ability on the stack automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Eldrazi + Drone subtypes, {3}{G}, 3/3). The JSON carries no
        // abilities — the ETB trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 111 (Token).
        //   "When this creature enters, create two 0/1 colorless Eldrazi
        //    Spawn creature tokens. They have "Sacrifice this token:
        //    Add {C}.""
        // No targets — pure token-creation. Mints SpawnCount (= 2) Eldrazi
        // Spawn tokens via the shared TokenFactory primitive (CR 111.10).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create two 0/1 colorless Eldrazi Spawn creature tokens with \"Sacrifice this token: Add {{C}}.\"",
            () =>
            {
                var controller = card.Controller ?? owner;
                for (var i = 0; i < SpawnCount; i++)
                {
                    TokenFactory.CreateEldraziSpawn(controller, zones);
                }
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>(
                (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }
}
