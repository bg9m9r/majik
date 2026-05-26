using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bitterblossom (Morningtide, {1}{B}).
///
/// Tribal Enchantment — Faerie. Oracle text:
///   "At the beginning of your upkeep, you lose 1 life and create a 1/1
///    black Faerie Rogue creature token with flying."
///
/// ## Implemented (v1)
/// - Enchantment shape with mana cost {1}{B}. <see cref="CardType.Tribal"/>
///   is added post-construction (the <see cref="Enchantment"/> ctor only
///   stamps <see cref="CardType.Enchantment"/>; Morningtide's "Tribal
///   Enchantment" line gets the second card type via
///   <see cref="Card.AddCardType"/>). <see cref="CardSubtype.Faerie"/>
///   subtype is wired so tribal-Faerie lords (Bitterblossom itself is
///   famously a Faerie permanent thanks to the Tribal type — Spellstutter
///   Sprite, Scion of Oona, etc. all see it).
/// - Upkeep triggered ability (CR 603.1, CR 500.4): "At the beginning of
///   your upkeep, you lose 1 life and create a 1/1 black Faerie Rogue
///   creature token with flying." Built via <see cref="Triggers.OnStepBegin"/>
///   filtered to the controller's own Upkeep step (same posture as
///   <see cref="DarkConfidantFactory"/>'s upkeep trigger).
///   On resolution:
///     1. Controller loses 1 life (<see cref="Player.LoseLife"/>).
///     2. A 1/1 black Faerie Rogue token with Flying enters the
///        battlefield under the controller via
///        <see cref="TokenFactory.CreateOnBattlefield"/>. Colour stamped
///        explicitly via <see cref="TokenFactory.TokenSpec.Colors"/> as
///        Black. Keywords list carries "Flying" so the token receives a
///        <see cref="KeywordAbility"/> on creation.
/// - Both halves of the trigger execute as one effect body (CR 608.2 —
///   single resolution; life loss happens before the token spawn so any
///   ETB triggers on the token see the controller at the lower life
///   total).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape-only path. Trigger is attached
///   but not registered with a <see cref="TriggerManager"/>; tests fire it
///   manually via <see cref="TriggeredAbility.IsTriggered"/> or by running
///   the effect directly. Token creation falls back to raw zone moves
///   (no <see cref="ZoneService"/>) — token-ETB triggers won't auto-fire.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ZoneService?)"/>
///   — fully wired overload. Registers the upkeep trigger with the bus and
///   threads <see cref="ZoneService"/> into token creation so
///   <see cref="CardMovedEvent"/> fires when each Faerie token enters.
///
/// ## Deferred (v1 gaps)
/// - <b>"You lose 1 life" cannot be a may</b>: Bitterblossom's upkeep
///   trigger is mandatory — no "may" rider. The life loss always happens,
///   even when it would lose the game (CR 119.6 — losing life is not
///   prevented by being at 1 life; the resulting 0-life total is then
///   resolved by SBAs). v1 honours this: the life loss always fires and
///   SBAs handle the 0/negative-life loss check on the next pass.
/// - <b>Token Faerie Rogue tribal interactions</b>: the spawned token
///   carries Faerie + Rogue subtypes so tribal lords (Bitterblossom itself
///   via Tribal type, Notorious Throng, Oona's Blackguard) see it
///   correctly. The token is NOT printed-colour black-via-mana-cost —
///   colour is stamped via <see cref="TokenFactory.TokenSpec.Colors"/>
///   as black per CR 105 / CR 111.4.
/// </summary>
[CardName("Bitterblossom")]
public static class BitterblossomFactory
{
    public const string CardName = "Bitterblossom";
    public const string PrintedManaCost = "{1}{B}";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;
    public const int LifeLossPerUpkeep = 1;

    /// <summary>
    /// Construct Bitterblossom with no live runtime wiring. The upkeep
    /// triggered ability is attached to the card for shape observability
    /// but is not registered with a <see cref="TriggerManager"/>; tests
    /// fire it manually. Token creation uses raw zone moves so token-ETB
    /// triggers (Soul Warden / Impact Tremors) won't auto-fire — fine for
    /// shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Bitterblossom with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Optional event bus — not used directly by the
    /// trigger body (life-loss + token creation publish their own events),
    /// kept on the signature for parity with other token-producer
    /// factories.</param>
    /// <param name="triggers">Optional trigger manager; when supplied the
    /// upkeep trigger is registered so the bus surfaces it as pending on
    /// every controller upkeep.</param>
    /// <param name="zoneService">Optional zone service so each spawned
    /// Faerie token publishes <see cref="CardMovedEvent"/> on ETB.</param>
    public static Enchantment Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Morningtide's "Tribal Enchantment — Faerie" line: Faerie subtype
        // is wired via the Enchantment ctor; Tribal type is added after
        // construction (the Enchantment ctor only registers Enchantment).
        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: new[] { CardSubtype.Faerie });
        card.AddCardType(CardType.Tribal);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Upkeep trigger — CR 603.1, CR 500.4.
        //   "At the beginning of your upkeep, you lose 1 life and create
        //    a 1/1 black Faerie Rogue creature token with flying."
        // Triggers.OnStepBegin filters StepStartedEvent on
        // (Upkeep, controller) so only the controller's own upkeeps fire.
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            $"{CardName}: lose 1 life, create a 1/1 black Faerie Rogue token with flying",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 119.6 — life loss is mandatory and not prevented by
                // already being at low life; SBA resolves any resulting
                // game loss on the next pass.
                controller.LoseLife(LifeLossPerUpkeep);

                CreateFaerieToken(controller, zoneService);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, PhaseStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/1 black Faerie Rogue creature
    /// token with Flying under <paramref name="controller"/>. Black colour
    /// is stamped via <see cref="TokenFactory.TokenSpec.Colors"/>; Flying
    /// is added as a granted <see cref="KeywordAbility"/> via the spec's
    /// Keywords list (CR 702.9).
    /// </summary>
    public static Creature CreateFaerieToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Faerie Rogue",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Faerie, CardSubtype.Rogue },
            Keywords: new[] { "Flying" },
            Colors: new[] { ManaColor.Black });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
