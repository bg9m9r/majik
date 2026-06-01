using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grave Titan (Magic 2011, {4}{B}{B}).
/// Creature — Giant, 6/6.
///
/// Oracle text (verified against Scryfall 2026-06-01):
///   "Deathtouch
///    Whenever this creature enters or attacks, create two 2/2 black Zombie
///    creature tokens."
///
/// ## Implemented (v1)
///
/// - 6/6 Creature — Giant, mana cost {4}{B}{B}, owner/controller wired. Base
///   shape materialised from the embedded JSON definition
///   (<c>grave-titan.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> (same posture as
///   <see cref="SengirVampireFactory"/>).
/// - <b>Deathtouch (CR 702.2)</b> — attached as a <see cref="KeywordAbility"/>
///   marker; <see cref="Majik.Core.Combat.CombatAbilities.HasDeathtouch"/>
///   consumes it for lethal-damage determination (same shape as
///   <see cref="DeadlyRecluseFactory"/>).
/// - <b>"Whenever this creature enters or attacks, create two 2/2 black
///   Zombie creature tokens."</b> The single printed ability has two trigger
///   conditions joined by "or" (CR 603.1) — modelled here as TWO
///   <see cref="TriggeredAbility"/> instances sharing the same token-minting
///   effect body, because the engine registers one event-typed condition per
///   trigger:
///   - the ETB half on <see cref="CardMovedEvent"/> → Battlefield matching
///     Grave Titan itself (<see cref="Triggers.OnEnterBattlefieldSelf"/>,
///     CR 603.6a — Hangarback / Bitterblossom-style ETB token minting), and
///   - the attack half on <see cref="CreatureAttacksEvent"/> matching Grave
///     Titan itself (<see cref="Triggers.OnAttackSelf"/>, CR 508.1f — same
///     posture as <see cref="LegionWarbossFactory"/>'s begin-combat trigger).
///   Each resolves by creating two 2/2 black Zombie creature tokens
///   (CR 111 / CR 111.4) under Grave Titan's controller via
///   <see cref="TokenFactory.CreateOnBattlefield"/>.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Trigger-on-stack timing</b>: the token creation runs immediately when
///   each trigger effect executes. Real MTG puts the triggered ability on the
///   stack and resolves it in APNAP order; v1 collapses this to
///   trigger-resolves-now (observationally equivalent for token creation
///   here, mirroring <see cref="LegionWarbossFactory"/>).
/// - <b>Per-card "enters or attacks" merge</b>: shipped as two triggers
///   rather than one condition with a disjunction. This is purely a modelling
///   choice and produces identical game state — each event path mints exactly
///   two tokens, never both at once for a single event.
/// </summary>
[CardName("Grave Titan")]
public static class GraveTitanFactory
{
    public const string CardName = "Grave Titan";
    public const string Slug = "grave-titan";
    public const int TokenCount = 2;
    public const int TokenPower = 2;
    public const int TokenToughness = 2;

    /// <summary>
    /// Construct Grave Titan with no live runtime services. Suitable for
    /// card-shape / dispatcher tests — both triggers are attached to the card
    /// shape (so <see cref="ICard.Abilities"/> includes them) but are not
    /// registered with any <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct a fully-wired Grave Titan.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the ETB and attack
    /// triggers against. May be null — both triggers are still attached to the
    /// card shape.</param>
    /// <param name="zoneService">Optional zone service so each spawned token's
    /// ETB <see cref="CardMovedEvent"/> fires (Soul Warden etc.). Pass
    /// <c>null</c> for raw zone moves.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        System.ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature type,
        // Giant subtype, {4}{B}{B}, 6/6). Deathtouch + the token triggers are
        // layered on below — none is expressible in the current JSON
        // AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.2 — Deathtouch marker. CombatAbilities.HasDeathtouch consumes
        // this for lethal-damage determination.
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        // ----------------------------------------------------------------
        // "Whenever this creature enters or attacks, create two 2/2 black
        // Zombie creature tokens." (CR 603.1.) Modelled as two triggers
        // sharing one effect body — the engine keys a trigger on a single
        // event type, and "enters" (CardMovedEvent) and "attacks"
        // (CreatureAttacksEvent) are distinct event paths. Each path mints
        // two tokens; neither fires for the other's event.
        // ----------------------------------------------------------------

        // ETB half — CR 603.6a. Token-minting on self-entering the
        // battlefield (Hangarback / Bitterblossom-style).
        var etbEffect = new Effect(
            $"{CardName}: on enter, create two 2/2 black Zombie creature tokens",
            () => CreateZombieTokens(card.Controller ?? owner, zoneService));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // Attack half — CR 508.1f self-match.
        var attackEffect = new Effect(
            $"{CardName}: on attack, create two 2/2 black Zombie creature tokens",
            () => CreateZombieTokens(card.Controller ?? owner, zoneService));

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create two 2/2 black Zombie creature tokens under
    /// <paramref name="controller"/>'s control. Routes through
    /// <see cref="TokenFactory.CreateOnBattlefield"/> so each token publishes a
    /// <see cref="CardMovedEvent"/> when a live <see cref="ZoneService"/> is
    /// threaded in (downstream ETB listeners — Soul Warden — fire).
    /// </summary>
    public static void CreateZombieTokens(
        Player controller,
        ZoneService? zoneService = null)
    {
        System.ArgumentNullException.ThrowIfNull(controller);

        for (int i = 0; i < TokenCount; i++)
        {
            var spec = new TokenFactory.TokenSpec(
                Name: "Zombie",
                Power: TokenPower,
                Toughness: TokenToughness,
                Subtypes: new[] { CardSubtype.Zombie },
                Keywords: null,
                // CR 105 / CR 111.4 — printed "2/2 black Zombie creature token".
                Colors: new[] { Majik.Core.ValueObjects.ManaColor.Black });

            TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
        }
    }
}
