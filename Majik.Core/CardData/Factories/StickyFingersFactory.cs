using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sticky Fingers (Streets of New Capenna, {R}).
///
/// Enchantment — Aura. Oracle text (verified against the printed card):
///   "Enchant creature
///    Enchanted creature has menace and \"Whenever this creature deals combat
///    damage to a player, create a Treasure token.\"
///    When enchanted creature dies, draw a card."
///
/// ## Shape source
/// Card identity (name, {R}, Enchantment — Aura, red) is loaded from
/// <c>Majik.Core/CardData/Cards/sticky-fingers.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The granted keyword + abilities are
/// hand-wired below — the JSON ability schema does not express a granted
/// combat-damage→Treasure trigger nor a leaves-the-battlefield draw, so they
/// follow the established Aura-grant patterns of
/// <see cref="EtherealArmorFactory"/> (granted keyword via
/// <see cref="AttachedBoostEffect"/>), <see cref="CuriosityFactory"/> /
/// <see cref="RagavanNimblePilfererFactory"/> (combat-damage→token trigger),
/// and <see cref="FalkenrathNobleFactory"/> (dies trigger via
/// <see cref="CardMovedEvent"/> Battlefield→Graveyard).
///
/// ## Implemented (v1)
/// - Enchantment — Aura at {R}; ETB-attach plumbing via the shared
///   <see cref="AuraSpellDefinitionBuilder"/> path (<see cref="BuildSpellDefinition"/>;
///   "Enchant creature" — CR 702.5b / 303.4c). On resolution the aura enters
///   already attached to the chosen creature (CR 303.4f).
/// - <b>Granted Menace (CR 702.111)</b>: a keyword-only
///   <see cref="AttachedBoostEffect"/> (CR 613 Layer 6) granting "Menace" to
///   the enchanted creature. Reads <see cref="Permanent.AttachedTo"/>
///   dynamically and gates on the aura being on the battlefield AND attached
///   (its <c>IsActive</c> check) — inert while unattached.
/// - <b>Granted combat-damage→Treasure trigger (CR 510 / 603.1)</b>:
///   "Whenever this creature deals combat damage to a player, create a Treasure
///   token." Per CR 603.3c the granted ability is controlled by the enchanted
///   creature's controller; "this creature" = the enchanted creature
///   (<see cref="Permanent.AttachedTo"/>, read dynamically so a control-change
///   redirects the trigger). The condition matches a
///   <see cref="CombatDamageDealtEvent"/> whose <c>Source</c> is the currently-
///   enchanted creature AND whose <c>TargetPlayer</c> is non-null (a player, not
///   a creature/planeswalker). On resolution a Treasure token is created under
///   the enchanted creature's controller via <see cref="TokenFactory.CreateTreasure"/>.
/// - <b>Aura's own dies trigger (CR 603.6e — leaves-the-battlefield)</b>:
///   "When enchanted creature dies, draw a card." Modelled as a
///   <see cref="CardMovedEvent"/> Battlefield→Graveyard trigger where the moved
///   card is the (last-known) enchanted creature. CR 603.6e — the trigger looks
///   back in time at the game state immediately before the creature left, so the
///   bearer is captured into <c>lastBearer</c> whenever the aura is observed
///   attached (and the condition also accepts the live <see cref="Permanent.AttachedTo"/>),
///   making it robust to whether the SBA detach (CR 704.5n) runs before or
///   after the move event. On resolution one card is drawn for the aura's
///   controller (CR 121.3).
///
/// ## Lifecycle
/// - The single-arg <see cref="Create(Player)"/> dispatcher overload builds the
///   correct card shape and attaches both triggered abilities (for shape /
///   condition inspection) but wires no <see cref="ContinuousEffectsService"/>
///   (so Menace is not registered) and no <see cref="TriggerManager"/>. The
///   three-arg overload registers the Menace boost and both triggers against
///   the supplied services.
///
/// ## Deferred (v1 gaps)
/// - None. Every clause is expressible with existing engine mechanics.
/// </summary>
[CardName("Sticky Fingers")]
public static class StickyFingersFactory
{
    public const string CardName = "Sticky Fingers";
    public const string Slug = "sticky-fingers";
    public const string Cost = "{R}";

    /// <summary>Printed oracle text — kept for documentation parity.</summary>
    public static readonly IReadOnlyList<string> OracleText = new[]
    {
        "Enchant creature",
        "Enchanted creature has menace and \"Whenever this creature deals combat "
            + "damage to a player, create a Treasure token.\"",
        "When enchanted creature dies, draw a card.",
    };

    /// <summary>Granted keyword on the enchanted creature: Menace (CR 702.111).</summary>
    public static readonly IReadOnlyList<string> GrantedKeywords = new[] { "Menace" };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Sticky Fingers with both granted triggered abilities attached
    /// (for shape / dispatcher / trigger-condition tests) but no live
    /// <see cref="ContinuousEffectsService"/> (Menace not registered) and no
    /// <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Construct Sticky Fingers. When <paramref name="continuousEffects"/> is
    /// supplied, the granted Menace boost (CR 613 Layer 6) is registered, gated
    /// on the aura being on the battlefield AND attached. When
    /// <paramref name="triggers"/> is supplied, the combat-damage→Treasure
    /// trigger and the enchanted-creature-dies→draw trigger are registered so
    /// the matching events automatically queue the abilities. Both triggered
    /// abilities are always attached to the aura's <see cref="Card.Abilities"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Granted Menace — CR 702.111 / CR 613 Layer 6. A keyword-only
        // AttachedBoostEffect (+0/+0) granting "Menace" to the enchanted
        // creature; inert while unattached (IsActive gates on battlefield +
        // attached).
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: 0,
                toughness: 0,
                grantedKeywords: GrantedKeywords));
        }

        // Last-known enchanted creature — captured for the look-back dies
        // trigger (CR 603.6e). Refreshed whenever the aura is observed attached.
        Creature? lastBearer = card.AttachedTo as Creature;

        Creature? CurrentBearer()
        {
            if (card.AttachedTo is Creature c)
            {
                lastBearer = c;
                return c;
            }
            return null;
        }

        // ----------------------------------------------------------------
        // Granted combat-damage→Treasure trigger — CR 510 / 603.1.
        //   "Whenever this creature deals combat damage to a player, create a
        //    Treasure token."
        // "this creature" = the enchanted creature (AttachedTo, dynamic). The
        // Treasure is created under the enchanted creature's controller
        // (CR 603.3c — the granted ability's controller).
        // ----------------------------------------------------------------
        var treasureEffect = new Effect(
            $"{CardName}: enchanted creature dealt combat damage to a player — create a Treasure token",
            () =>
            {
                var enchanted = lastBearer ?? card.AttachedTo as Creature;
                var controller = enchanted?.Controller ?? card.Controller ?? owner;
                TokenFactory.CreateTreasure(controller);
            });

        var combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (e.TargetPlayer == null) return false;
                var enchanted = CurrentBearer();
                if (enchanted == null) return false;
                return ReferenceEquals(e.Source, enchanted);
            }),
            effects: new IEffect[] { treasureEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(combatTrigger);
        triggers?.RegisterTriggeredAbility(combatTrigger);

        // ----------------------------------------------------------------
        // Aura's own dies trigger — CR 603.6e (leaves-the-battlefield ability).
        //   "When enchanted creature dies, draw a card."
        // Detected as a CardMovedEvent Battlefield→Graveyard whose moved card is
        // the (last-known) enchanted creature. CR 603.6e: the ability looks back
        // in time at the game state immediately before the creature left — so we
        // match against the captured bearer (robust to whether the SBA detach in
        // CR 704.5n runs before or after the move event fires). Draw is for the
        // aura's controller (CR 121.3).
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: enchanted creature died — draw a card",
            () =>
            {
                var drawFor = card.Controller ?? owner;
                DrawOne(drawFor);
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
            {
                if (e.FromZone != ZoneType.Battlefield) return false;
                if (e.ToZone != ZoneType.Graveyard) return false;

                // Refresh the captured bearer first so a same-tick attach is
                // observed, then match the moved card against the bearer.
                var bearer = CurrentBearer() ?? lastBearer;
                return bearer != null && ReferenceEquals(e.Card, bearer);
            }),
            // Active in Battlefield (aura still on the battlefield at the moment
            // the bearer's move event fires) and Graveyard (self-death edge —
            // the aura may already be heading to the graveyard with its bearer).
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Sticky Fingers. The
    /// printed "Enchant creature" line (CR 702.5b / 303.4c) makes any creature a
    /// legal target. Filters the supplied battlefield to creatures; on resolve
    /// the aura enters already attached to the chosen target (CR 303.4f).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target creature",
            battlefield: battlefield,
            predicate: p => p != null && p.HasType(CardType.Creature));
    }

    /// <summary>
    /// Draw a single card for <paramref name="player"/> via raw library → hand
    /// zone moves. Empty-library halts the draw and stamps
    /// <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> so the SBA loop notes
    /// the loss condition (CR 704.5c / 120.3). Mirrors Curiosity's simple-draw shape.
    /// </summary>
    private static void DrawOne(Player player)
    {
        var top = player.Zones.Library.GetCards().FirstOrDefault();
        if (top == null)
        {
            player.MarkTriedToDrawFromEmptyLibrary();
            return;
        }
        player.Zones.Library.RemoveCard(top);
        player.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }
}
