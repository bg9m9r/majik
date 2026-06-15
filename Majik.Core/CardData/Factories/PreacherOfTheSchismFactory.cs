using System;
using System.Collections.Generic;
using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Preacher of the Schism (The Lost Caverns of Ixalan,
/// {2}{B}).
///
/// Creature — Vampire Cleric 2/4. Oracle text (verified against Scryfall):
///   "Deathtouch
///    Whenever this creature attacks the player with the most life or tied for
///    most life, create a 1/1 white Vampire creature token with lifelink.
///    Whenever this creature attacks while you have the most life or are tied
///    for most life, you draw a card and you lose 1 life."
///
/// ## Shape source
/// Card identity (name, {2}{B}, 2/4, Creature — Vampire Cleric) is loaded from
/// <c>Majik.Core/CardData/Cards/preacher-of-the-schism.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="AmbushViperFactory"/>. The Deathtouch keyword marker and the two
/// conditional attack triggers are attached in code below.
///
/// ## Implemented (v1)
/// - 2/4 Creature — Vampire Cleric at {2}{B}. Mono-black (the {B} pip,
///   CR 202.2c). Mana value 3 (CR 202.3).
/// - <b>Deathtouch</b> (CR 702.2): <see cref="KeywordAbility"/> marker read by
///   <c>Majik.Core.Combat.CombatAbilities.HasDeathtouch</c> — same wire-up as
///   <see cref="AmbushViperFactory"/>.
/// - <b>Attack trigger #1</b> (CR 508.1f): "Whenever this creature attacks the
///   player with the most life or tied for most life, create a 1/1 white
///   Vampire creature token with lifelink." Keyed on
///   <see cref="Triggers.OnAttackSelf"/>; the defending player is captured off
///   the live <see cref="CreatureAttacksEvent"/> (the closure idiom from
///   <see cref="RestlessFortressFactory"/>) and the most-life comparison runs
///   at resolution against <c>ctx.Game.AllPlayers</c> (the live-snapshot idiom
///   from <see cref="AmaliaBenavidesAguirreFactory"/>). Defending player having
///   the most life (or tied) mints the Vampire token via
///   <see cref="TokenFactory"/> (CR 111).
/// - <b>Attack trigger #2</b> (CR 508.1f): "Whenever this creature attacks
///   while you have the most life or are tied for most life, you draw a card
///   and you lose 1 life." Keyed on <see cref="Triggers.OnAttackSelf"/>; the
///   controller's most-life check runs at resolution against
///   <c>ctx.Game.AllPlayers</c>. CR 120.2 — draw one card; CR 119.3 — lose 1
///   life (independent events). Draw uses the manual library-top move idiom
///   (<see cref="GlintSleeveSiphonerFactory"/>) so an empty library flags the
///   draw-from-empty SBA (CR 704.5b).
/// </summary>
[CardName("Preacher of the Schism")]
public static class PreacherOfTheSchismFactory
{
    public const string CardName = "Preacher of the Schism";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("preacher-of-the-schism");

    /// <summary>Construct Preacher of the Schism with no live trigger wiring —
    /// the Deathtouch marker and both attack triggers are attached structurally
    /// for shape tests; without a trigger manager the triggers are not
    /// registered.</summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, triggers: null);

    /// <summary>Construct Preacher of the Schism with optional runtime
    /// services.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Optional zone service — routes the Vampire
    /// token ETB through <see cref="ZoneService"/> so <see cref="CardMovedEvent"/>
    /// publishes.</param>
    /// <param name="triggers">Optional trigger manager — registers both attack
    /// triggers.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.2 — Deathtouch. CombatAbilities.HasDeathtouch consumes this
        // marker for lethal-damage determination.
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        AttachMostLifeDefenderTrigger(card, owner, zoneService, triggers);
        AttachControllerMostLifeTrigger(card, owner, triggers);

        return card;
    }

    /// <summary>
    /// CR 508.1f — "Whenever this creature attacks the player with the most life
    /// or tied for most life, create a 1/1 white Vampire creature token with
    /// lifelink." The defending player is captured at attack declaration; the
    /// most-life comparison runs at resolution against the live player snapshot.
    /// </summary>
    private static void AttachMostLifeDefenderTrigger(
        Creature card,
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        // CR 506.2 / 508.4d — capture the defending player off the live attack
        // event (closure idiom from Restless Fortress).
        Player? capturedDefender = null;

        var makeVampire = new Effect(
            $"{CardName}: create a 1/1 white Vampire token with lifelink if the defending player has the most life",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                var defender = capturedDefender;

                // CR 508.1f wording is checked here against the live snapshot —
                // "the player with the most life or tied for most life".
                if (defender != null && HasMostLife(defender, ctx))
                {
                    // CR 111 — 1/1 white Vampire creature token with lifelink.
                    TokenFactory.CreateOnBattlefield(
                        new TokenFactory.TokenSpec(
                            Name: "Vampire",
                            Power: 1,
                            Toughness: 1,
                            Subtypes: new[] { CardSubtype.Vampire },
                            Keywords: new[] { "Lifelink" },
                            Colors: new[] { ManaColor.White }),
                        controller,
                        zoneService);
                }

                return default;
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CreatureAttacksEvent>(
                (e, _) =>
                {
                    // CR 506.2 / 508.4d — capture the defending player (attacked
                    // player OR the controller of the attacked planeswalker).
                    capturedDefender = e.DefendingPlayer;
                    // CR 508.1f — fires when THIS creature is the attacker.
                    return ReferenceEquals(e.Attacker, card);
                }),
            effects: new IEffect[] { makeVampire },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }

    /// <summary>
    /// CR 508.1f — "Whenever this creature attacks while you have the most life
    /// or are tied for most life, you draw a card and you lose 1 life." The
    /// controller's most-life check runs at resolution against the live player
    /// snapshot.
    /// </summary>
    private static void AttachControllerMostLifeTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers)
    {
        var drawAndLose = new Effect(
            $"{CardName}: draw a card and lose 1 life if you have the most life",
            ctx =>
            {
                var controller = card.Controller ?? owner;

                // CR 508.1f wording — "while you have the most life or are tied
                // for most life" — checked against the live snapshot.
                if (!HasMostLife(controller, ctx)) return default;

                // CR 120.2 — draw one card (library-top move). Empty library
                // flags the draw-from-empty SBA (CR 704.5b).
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    controller.MarkTriedToDrawFromEmptyLibrary();
                }
                else
                {
                    controller.Zones.Library.RemoveCard(top);
                    controller.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }

                // CR 119.3 — you lose 1 life (independent from the draw).
                controller.LoseLife(1);

                return default;
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { drawAndLose },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }

    /// <summary>
    /// True iff <paramref name="player"/> has the most life among all players,
    /// or is tied for the most (CR 508.1f wording). Reads the live player
    /// snapshot off <paramref name="ctx"/>; falls back to a trivially-true
    /// single-player view when no game context is supplied (shape tests).
    /// </summary>
    private static bool HasMostLife(Player player, ResolutionContext ctx)
    {
        IReadOnlyList<Player> players = ctx.Game?.AllPlayers
            ?? (IReadOnlyList<Player>)new[] { player };
        int highest = players.Max(p => p.LifeTotal);
        return player.LifeTotal >= highest;
    }
}
