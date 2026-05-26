using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eldrazi Skyspawner (Battle for Zendikar, {2}{U}).
///
/// Creature — Eldrazi Drone 2/2. Oracle text (Scryfall, verified):
///   "Flying
///    When this creature enters, create a 1/1 colorless Eldrazi Scion
///    creature token with \"Sacrifice this creature: Add {C}.\""
///
/// ## Implemented (v1)
/// - 2/2 Creature — Eldrazi Drone at {2}{U}.
/// - Flying (CR 702.9) attached as a <see cref="KeywordAbility"/> marker
///   so combat / colour-matters surfaces observe it (same posture as
///   Pinnacle Emissary's Drone token, Slickshot Show-Off's Flying).
/// - <b>ETB triggered ability (CR 603.6a)</b> over a
///   <see cref="CardMovedEvent"/> filtered to (this card, ToZone =
///   Battlefield). Resolution creates one 1/1 colourless Eldrazi Scion
///   creature token under Skyspawner's controller via
///   <see cref="CreateEldraziScionToken"/>. The Scion ships with a
///   <see cref="ManaAbility"/> producing one {C} — same v1 posture as
///   <see cref="TokenFactory.CreateEldraziSpawn"/> where the sacrifice
///   cost rider is documented but deferred pending a sac-cost
///   <see cref="ManaAbility"/> extension (parallels Treasure / Food's
///   tap+sac rider gap).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached
///   for shape observability; not registered with any
///   <see cref="TriggerManager"/>, no <see cref="ZoneService"/> wiring.
///   Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully
///   wired. The trigger registers with <paramref name="triggers"/>; the
///   Scion token's ETB routes through <paramref name="zones"/> so its own
///   <see cref="CardMovedEvent"/> publishes (downstream ETB subscribers
///   like Soul Warden see the Scion's arrival).
///
/// ## Deferred (v1 gaps)
/// - <b>"Sacrifice this creature: Add {C}." cost</b> on the Scion token: the
///   <see cref="ManaAbility"/> primitive doesn't currently carry a sac cost
///   (same gap as Eldrazi Spawn / Treasure / Food). The Scion produces {C}
///   without enforcing the sac. When the cost extension lands the helper
///   picks up the rider for free.
/// </summary>
[CardName("Eldrazi Skyspawner")]
public static class EldraziSkyspawnerFactory
{
    public const string CardName = "Eldrazi Skyspawner";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Eldrazi Skyspawner with no live wiring. The ETB trigger
    /// is attached for shape observability; not registered with any
    /// <see cref="TriggerManager"/>, no <see cref="ZoneService"/>
    /// wiring. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Eldrazi Skyspawner with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the Scion token's ETB routes
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

        // CR 702.9 — Flying. Keyword marker only; the combat-block
        // validator reads the keyword off the card's abilities (same
        // shape used by Pinnacle Emissary's Drone token, Slickshot
        // Show-Off, etc.).
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 111 (Token).
        //   "When this creature enters, create a 1/1 colorless Eldrazi
        //    Scion creature token with \"Sacrifice this creature: Add
        //    {C}.\""
        // No targets — pure token-creation.
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: create a 1/1 colourless Eldrazi Scion creature token with \"Sacrifice this creature: Add {{C}}.\"",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateEldraziScionToken(controller, zones);
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

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/1 colourless Eldrazi Scion
    /// creature token under <paramref name="controller"/>'s control with
    /// "Sacrifice this creature: Add {C}." bound as a
    /// <see cref="ManaAbility"/> producing one colourless.
    ///
    /// <para>v1 gap: the sacrifice cost on the mana ability is documented
    /// but unenforced — same posture as
    /// <see cref="TokenFactory.CreateEldraziSpawn"/>, Treasure, Food. The
    /// Scion still produces {C} when its mana ability is activated; the
    /// sac cost is wired separately once the
    /// <see cref="ManaAbility"/> cost extension lands.</para>
    /// </summary>
    public static Creature CreateEldraziScionToken(
        Player controller,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Eldrazi Scion",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Eldrazi, CardSubtype.Scion },
            // CR 105 / CR 111.4 — printed "colourless" token. Explicit
            // empty colour list stamps the colourless override on the
            // resulting Card.TokenColorsOverride.
            Colors: Array.Empty<ManaColor>());

        var scion = TokenFactory.CreateOnBattlefield(spec, controller, zones);

        // "Sacrifice this creature: Add {C}."
        // v1: wired as a plain ManaAbility producing one colourless.
        // Sacrifice-cost enforcement is deferred until ManaAbility
        // supports additional costs (same gap as
        // TokenFactory.CreateEldraziSpawn / Treasure / Food's sac riders).
        scion.AddAbility(new ManaAbility(scion, controller, ManaCost.Parse("C")));

        return scion;
    }
}
