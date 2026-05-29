using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Instigator (Dominaria, {1}{R}).
///
/// Creature — Goblin Rogue 1/1. Oracle text (Scryfall, verified):
///   "When this creature enters, create a 1/1 red Goblin creature token."
///
/// ## Shape source
/// Card identity (name, {1}{R}, 1/1, Creature — Goblin Rogue) is loaded
/// from <c>Majik.Core/CardData/Cards/goblin-instigator.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The single ETB triggered ability is
/// attached in code below: the JSON ability schema does not yet express a
/// "create a creature token" effect, so it is hand-rolled here — same
/// posture as <see cref="BorderlandRangerFactory"/> (identity from JSON,
/// behaviour in C#) and the token-creation analogue
/// <see cref="MoggWarMarshalFactory"/> (same {1}{R} 1/1 Goblin + ETB
/// "create a 1/1 red Goblin token"). Goblin Instigator is the strict
/// ETB-only subset of Mogg War Marshal — no Echo, no dies trigger.
///
/// ## Implemented (v1)
/// - 1/1 Creature — Goblin Rogue (CR 205.3m) at {1}{R}. Colour red is
///   derived from the {R} pip (CR 202.2). Both <see cref="Majik.Core.Cards.Types.CardSubtype.Goblin"/>
///   and <see cref="Majik.Core.Cards.Types.CardSubtype.Rogue"/> are stamped
///   so Goblin tribal scopes (Goblin Chieftain / Krenko / Goblin Warchief)
///   and Rogue anchors pick it up correctly.
/// - <b>ETB triggered ability (CR 603.6a)</b>: "When this creature enters,
///   create a 1/1 red Goblin creature token." Wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> active in
///   <see cref="ZoneType.Battlefield"/>. On resolution it creates one 1/1
///   red Goblin creature token (CR 111 / CR 111.4) under the entering
///   card's controller via <see cref="GoblinRabblemasterFactory.CreateGoblinToken"/>
///   — the same <see cref="Majik.Core.Tokens.TokenFactory.TokenSpec"/> used
///   by Krenko / Goblin Rabblemaster / Mogg War Marshal (1/1, red, Goblin
///   subtype, no keywords). Token creation routes through
///   <see cref="ZoneService"/> when supplied so the new token's ETB
///   CardMovedEvent publishes for downstream ETB triggers (Impact Tremors /
///   Soul Warden / Purphoros).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached
///   for shape observability but not registered with any
///   <see cref="TriggerManager"/>; token creation uses raw zone
///   manipulation. Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully
///   wired. The ETB trigger registers with <paramref name="triggers"/>;
///   token creation routes through <paramref name="zoneService"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Token ETB triggers fire only when zoneService supplied</b>: same
///   posture as Krenko / Rabblemaster / Mogg War Marshal — the raw fallback
///   bypasses <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>.
/// </summary>
[CardName("Goblin Instigator")]
public static class GoblinInstigatorFactory
{
    public const string CardName = "Goblin Instigator";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("goblin-instigator");

    /// <summary>
    /// Construct Goblin Instigator with its ETB trigger attached to the
    /// card shape but NOT registered with a <see cref="TriggerManager"/>;
    /// token creation on resolution uses raw zone manipulation. Suitable
    /// for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Goblin Instigator with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger registers
    /// so the matching <c>CardMovedEvent</c> queues it on the stack
    /// (CR 603.3). When <paramref name="zoneService"/> is supplied, token
    /// creation routes through <see cref="ZoneService"/> so the Goblin
    /// token's ETB CardMovedEvent publishes for downstream ETB triggers.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 603.6a — ETB trigger. "When this creature enters, create a
        // 1/1 red Goblin creature token." ActiveZones = Battlefield
        // (standard ETB shape — the source is already on the battlefield
        // when ZoneService publishes the CardMovedEvent).
        var etbEffect = new Effect(
            $"{CardName}: create 1/1 red Goblin token",
            () =>
            {
                // CR 111 / CR 111.4 — token under the entering card's
                // controller (defaults to owner for v1 — same posture as
                // Mogg War Marshal).
                var controller = card.Controller ?? owner;
                GoblinRabblemasterFactory.CreateGoblinToken(controller, zoneService);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
