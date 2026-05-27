using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sword of Forge and Frontier (Modern Horizons 3,
/// {3}). Completes the nine-card "Sword of X and Y" enemy-colour cycle.
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +2/+2 and has protection from red and from
///    green."
///   "Whenever equipped creature deals combat damage to a player, draw a
///    card, then you may discard a card. If you don't, you may play an
///    additional land this turn."
///   "Equip {2}."
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +2/+2"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (CR 613 Layer 7c). The
///   effect reads <see cref="Permanent.AttachedTo"/> dynamically, so
///   re-equipping transfers the boost without re-registration. Mirrors
///   <see cref="SwordOfFireAndIceFactory"/>.
/// - <b>"Protection from red and from green"</b> — CR 702.16. With a
///   <see cref="ContinuousEffectsService"/> wired, two
///   <see cref="GrantAbilityEffect"/> instances (CR 613.1f, Layer 6)
///   re-project <see cref="ProtectionAbility"/>("red") /
///   <see cref="ProtectionAbility"/>("green") onto the live equipped
///   creature. Same grant shape as Sword of Fire and Ice — selectors read
///   <see cref="Permanent.AttachedTo"/> at sync time, so re-equipping
///   transfers the protection automatically. The shape-only constructor
///   (no service) leaves the two markers on the equipment card so
///   factory-shape / dispatch tests still get a deterministic answer.
/// - <b>Combat-damage-to-a-player trigger (CR 510, CR 603.1)</b> — wired
///   over <see cref="CombatDamageDealtEvent"/> filtered to the equipped
///   creature (<see cref="Permanent.AttachedTo"/> at trigger-evaluation
///   time) and a non-null <see cref="CombatDamageDealtEvent.TargetPlayer"/>.
///   Same shape as Sword of Fire and Ice, but the on-resolve effect is a
///   draw-then-may-discard / else-extra-land branch (no damage half).
///
///   On resolution:
///     1. Controller draws one card (CR 121.1). Empty-library stamps the
///        loss flag via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>
///        (CR 704.5b / 120.3).
///     2. v1 deterministic "may" policy: <b>decline to discard</b>. This
///        favours the ramp branch — Sword of Forge and Frontier is
///        printed and played as a ramp / value enabler (the extra land
///        drop is the strictly-better mode in nearly every board state),
///        so the v1 deterministic choice mirrors the bot's likely intent.
///        When a real agent prompt ships, this gates on an
///        <see cref="Players.Agents.IPlayerAgent.ChooseYesNoAsync"/>
///        gesture against <see cref="Cards.BotIntent.Discard"/> /
///        <see cref="Cards.BotIntent.Ramp"/>.
///     3. Because we declined to discard, the second "you may" fires —
///        controller may play an additional land this turn. v1 again
///        auto-accepts (Ramp). The cap bump uses the same
///        <see cref="LandDropTracker.SetMaxLandDropsThisTurn"/> additive
///        pattern as <see cref="ExploreFactory"/>:
///        <c>current + 1</c>. <see cref="LandDropTracker.ResetTurn"/>
///        clears the bump on turn change so multi-turn carry-over isn't
///        possible.
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via the
///   <see cref="EquipActivatedAbility"/> primitive, same shape as the
///   other shipped Swords.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests. The combat-damage trigger is attached
/// for shape but not registered with a <see cref="TriggerManager"/>; the
/// static +2/+2 boost is not registered against any
/// <see cref="ContinuousEffectsService"/>; the land-drop bump on
/// resolution is skipped when no <see cref="LandDropTracker"/> is
/// supplied. Use the four-arg overload to wire runtime services.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically.
/// - <b>Agent-driven "may discard" prompt</b> — v1 always declines
///   (ramp-preferred). When the prompt queue ships, the gesture wires to
///   <see cref="Players.Agents.IPlayerAgent.ChooseYesNoAsync"/>.
/// - <b>Agent-driven "may play extra land" prompt</b> — v1 auto-accepts.
///   Same "Ramp is always good" simplification carried by
///   <see cref="ExploreFactory"/>.
/// </summary>
[CardName("Sword of Forge and Frontier")]
public static class SwordOfForgeAndFrontierFactory
{
    public const string CardName = "Sword of Forge and Frontier";
    public const string Cost = "{3}";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs Sword of Forge and Frontier with no live runtime wiring
    /// (the shape / dispatcher path). The +2/+2 boost is not registered
    /// against any service; the combat-damage trigger is attached to the
    /// card but not registered with a <see cref="TriggerManager"/>;
    /// land-drop bump on resolution is skipped. Protection markers are
    /// present.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null, landDropTracker: null);

    /// <summary>
    /// Constructs Sword of Forge and Frontier. When
    /// <paramref name="continuousEffects"/> is supplied the +2/+2 boost
    /// (Layer 7c) is registered against it. When <paramref name="triggers"/>
    /// is supplied the combat-damage-to-a-player trigger is registered so
    /// a <see cref="CombatDamageDealtEvent"/> from the equipped creature
    /// (targeting a player) automatically queues the ability. When
    /// <paramref name="landDropTracker"/> is supplied, declining the
    /// "may discard" gesture bumps the controller's per-turn land-drop
    /// cap by one (CR 305.2); a null tracker is the shape-only path and
    /// skips the bump.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        LandDropTracker? landDropTracker)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Static continuous effect — "Equipped creature gets +2/+2."
        // CR 613 Layer 7c. The effect gates on the source being on the
        // battlefield AND attached (see AttachedBoostEffect.IsActive).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 2, toughness: 2));
        }

        // --------------------------------------------------------------
        // Protection grants — "Equipped creature has protection from red
        // and from green." (CR 702.16, CR 613.1f). See class docstring.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility("red")));
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility("green")));
        }
        else
        {
            card.AddAbility(new ProtectionAbility("red"));
            card.AddAbility(new ProtectionAbility("green"));
        }

        // --------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510 / CR 603.1.
        //   "Whenever equipped creature deals combat damage to a player,
        //    draw a card, then you may discard a card. If you don't,
        //    you may play an additional land this turn."
        // --------------------------------------------------------------
        var combatEffect = new Effect(
            $"{CardName}: draw 1; v1 decline discard → bump land cap",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;

                var controller = card.Controller ?? owner;

                // 1) Draw a card. Empty-library stamps the loss flag
                //    (CR 704.5b / 120.3).
                DrawOne(controller);

                // 2) v1 deterministic policy: decline the optional discard
                //    (ramp branch is the load-bearing mode for this card).
                //    Real agent prompt deferred — see class docstring.
                bool discarded = false;

                // 3) Because we declined, fire the "may play an additional
                //    land this turn" branch. CR 305.2 — bump the per-turn
                //    cap by one. Null tracker = shape-only path; skip.
                if (!discarded && landDropTracker != null)
                {
                    var current = landDropTracker.MaxLandDropsThisTurn(controller);
                    landDropTracker.SetMaxLandDropsThisTurn(controller, current + 1);
                }
            });

        var combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (e.TargetPlayer == null) return false;
                var equipped = card.AttachedTo;
                if (equipped == null) return false;
                return ReferenceEquals(e.Source, equipped);
            }),
            effects: new IEffect[] { combatEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(combatTrigger);
        triggers?.RegisterTriggeredAbility(combatTrigger);

        // --------------------------------------------------------------
        // Equip {2} — activated ability (CR 702.6) via the
        // EquipActivatedAbility primitive.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }

    /// <summary>
    /// Draw a single card for <paramref name="player"/> via raw library →
    /// hand zone moves. Empty-library stamps the CR 704.5b loss flag.
    /// Mirrors <see cref="SwordOfFireAndIceFactory"/>'s shape-only draw.
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
