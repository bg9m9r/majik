using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Emrakul's Hatcher (Rise of the Eldrazi, {4}{R}).
///
/// Creature — Eldrazi Drone 3/3. Oracle text (Scryfall, verified):
///   "When this creature enters, create three 0/1 colorless Eldrazi Spawn
///    creature tokens. They have \"Sacrifice this token: Add {C}.\""
///
/// ## Implemented (v1)
/// - 3/3 Creature — Eldrazi Drone at {4}{R}.
/// - <b>ETB triggered ability (CR 603.6a)</b> over a
///   <see cref="CardMovedEvent"/> filtered to (this card, ToZone =
///   Battlefield). Resolution creates THREE 0/1 colourless Eldrazi Spawn
///   creature tokens under the controller via
///   <see cref="TokenFactory.CreateEldraziSpawn"/> — the same helper used by
///   <see cref="EldraziSkyspawnerFactory"/>'s Scion sibling, here invoked
///   with count = 3 (the Spawn token's printed
///   "Sacrifice this token: Add {C}." mana ability ships with it).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached
///   for shape observability; not registered with any
///   <see cref="TriggerManager"/>, no <see cref="ZoneService"/> wiring.
///   Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully
///   wired. The trigger registers with <paramref name="triggers"/>; each
///   Spawn token's ETB routes through <paramref name="zones"/> so its own
///   <see cref="CardMovedEvent"/> publishes.
///
/// Each Spawn carries its "Sacrifice this creature: Add {C}." mana ability
/// (sacrifice-cost, no-tap) via the shared
/// <see cref="TokenFactory.CreateEldraziSpawn"/> helper.
/// </summary>
[CardName("Emrakul's Hatcher")]
public static class EmrakulsHatcherFactory
{
    public const string CardName = "Emrakul's Hatcher";
    public const string PrintedManaCost = "{4}{R}";
    public const int Power = 3;
    public const int Toughness = 3;
    public const int SpawnCount = 3;

    /// <summary>
    /// Construct Emrakul's Hatcher with no live wiring. The ETB trigger
    /// is attached for shape observability; not registered with any
    /// <see cref="TriggerManager"/>, no <see cref="ZoneService"/>
    /// wiring. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Emrakul's Hatcher with optional runtime services.
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

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Eldrazi, CardSubtype.Drone });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 111 (Token).
        //   "When this creature enters, create three 0/1 colorless Eldrazi
        //    Spawn creature tokens. They have \"Sacrifice this token: Add
        //    {C}.\""
        // No targets — pure token-creation.
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: create three 0/1 colourless Eldrazi Spawn creature tokens with \"Sacrifice this token: Add {{C}}.\"",
            () =>
            {
                var controller = card.Controller ?? owner;
                for (var i = 0; i < SpawnCount; i++)
                {
                    // CR 111.10 — Eldrazi Spawn: 0/1 colourless creature token
                    // with "Sacrifice this creature: Add {C}." (sacrifice-cost
                    // mana ability — see TokenFactory.CreateEldraziSpawn).
                    TokenFactory.CreateEldraziSpawn(controller, zones);
                }
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }
}
