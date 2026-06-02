using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Resolute Reinforcements (Murders at Karlov Manor
/// Commander, {1}{W}).
///
/// Creature — Human Soldier 1/1. Oracle text (Scryfall, verified):
///   "Flash (You may cast this spell any time you could cast an instant.)
///    When this creature enters, create a 1/1 white Soldier creature token."
///
/// ## Shape source
/// Card identity (name, {1}{W}, 1/1, Creature — Human Soldier) is loaded from
/// <c>Majik.Core/CardData/Cards/resolute-reinforcements.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="GoblinInstigatorFactory"/> (identity from JSON, behaviour in
/// C#). Resolute Reinforcements is the white-Soldier, Flash-bearing analogue
/// of Goblin Instigator: the Flash keyword marker and the ETB token trigger
/// are attached in code because the JSON ability schema does not yet express
/// a keyword marker or a "create a creature token" effect.
///
/// ## Implemented (v1)
/// - 1/1 Creature — Human Soldier (CR 205.3m) at {1}{W}. Colour white is
///   derived from the {W} pip (CR 202.2). Mana value 2 (CR 202.3). Both
///   <see cref="Majik.Core.Cards.Types.CardSubtype.Human"/> and
///   <see cref="Majik.Core.Cards.Types.CardSubtype.Soldier"/> are stamped from
///   the JSON so tribal scopes pick it up correctly.
/// - <b>Flash</b> (CR 702.8): <see cref="KeywordAbility"/> marker — the
///   spell may be cast any time its controller could cast an instant
///   (CR 601.3e / CR 702.8a). Same wire-up shape as
///   <see cref="SpellstutterSpriteFactory"/>'s Flash marker, read by the
///   cast-timing validator.
/// - <b>ETB triggered ability</b> (CR 603.6a): "When this creature enters,
///   create a 1/1 white Soldier creature token." Wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> active in
///   <see cref="ZoneType.Battlefield"/>. On resolution it creates one 1/1
///   white Soldier creature token (CR 111 / CR 111.4) under the entering
///   card's controller via <see cref="RaiseTheAlarmFactory.CreateSoldierToken"/>
///   — the same <see cref="Majik.Core.Tokens.TokenFactory.TokenSpec"/> used by
///   Raise the Alarm / Captain's Call (1/1, white, Soldier subtype, no
///   keywords). Token creation routes through <see cref="ZoneService"/> when
///   supplied so the new token's ETB CardMovedEvent publishes for downstream
///   ETB triggers (Impact Tremors / Soul Warden).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached
///   for shape observability but not registered with any
///   <see cref="TriggerManager"/>; token creation uses raw zone manipulation.
///   Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully
///   wired. The ETB trigger registers with <paramref name="triggers"/>; token
///   creation routes through <paramref name="zoneService"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Token ETB triggers fire only when zoneService supplied</b>: same
///   posture as Goblin Instigator / Raise the Alarm — the raw fallback
///   bypasses <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>.
/// </summary>
[CardName("Resolute Reinforcements")]
public static class ResoluteReinforcementsFactory
{
    public const string CardName = "Resolute Reinforcements";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("resolute-reinforcements");

    /// <summary>
    /// Construct Resolute Reinforcements with its Flash marker and ETB trigger
    /// attached to the card shape, but the ETB trigger NOT registered with a
    /// <see cref="TriggerManager"/>; token creation on resolution uses raw zone
    /// manipulation. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Resolute Reinforcements with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger registers so
    /// the matching <c>CardMovedEvent</c> queues it on the stack (CR 603.3).
    /// When <paramref name="zoneService"/> is supplied, token creation routes
    /// through <see cref="ZoneService"/> so the Soldier token's ETB
    /// CardMovedEvent publishes for downstream ETB triggers.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Keyword marker letting the spell be cast any time
        // its controller could cast an instant (CR 601.3e). Read by the
        // cast-timing validator — same wire-up as Spellstutter Sprite.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 603.6a — ETB trigger. "When this creature enters, create a 1/1
        // white Soldier creature token." ActiveZones = Battlefield (standard
        // ETB shape — the source is already on the battlefield when
        // ZoneService publishes the CardMovedEvent).
        var etbEffect = new Effect(
            $"{CardName}: create 1/1 white Soldier token",
            () =>
            {
                // CR 111 / CR 111.4 — token under the entering card's
                // controller (defaults to owner for v1 — same posture as
                // Goblin Instigator).
                var controller = card.Controller ?? owner;
                RaiseTheAlarmFactory.CreateSoldierToken(controller, zoneService);
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
