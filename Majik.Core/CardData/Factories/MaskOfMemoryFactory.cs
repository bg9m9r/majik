using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mask of Memory (Legions, {2}).
///
/// Artifact — Equipment. Oracle text:
///   "Whenever equipped creature deals combat damage to a player, you may
///    draw two cards. If you do, discard a card."
///   "Equip {1}"
///
/// ## Shape source
/// Card identity (name, {2}, Artifact — Equipment, colorless) is loaded from
/// <c>Majik.Core/CardData/Cards/mask-of-memory.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The combat-damage trigger and the
/// Equip ability are attached in code below — the JSON ability schema expresses
/// neither a combat-damage-to-a-player loot trigger nor the equip ability, so
/// they are hand-rolled here (same posture as
/// <see cref="SwordOfLightAndShadowFactory"/>, the closest equipment analogue,
/// and <see cref="CuriosityFactory"/> for the optional-draw shape).
///
/// ## Implemented (v1)
/// - <b>Combat-damage-to-a-player trigger (CR 510 / CR 603.1)</b>: fires on a
///   <see cref="CombatDamageDealtEvent"/> whose
///   <see cref="CombatDamageDealtEvent.Source"/> is the currently-equipped
///   creature (<see cref="Permanent.AttachedTo"/>, read dynamically so
///   re-equipping / a control change redirects the trigger) AND whose
///   <see cref="DamageDealtEvent.TargetPlayer"/> is non-null (combat damage to a
///   player, not to a creature / planeswalker).
/// - <b>"You may draw two cards. If you do, discard a card." (loot, CR 121.1 /
///   701.16)</b>: the draw is optional (CR 603.5 "may"). When the controller
///   chooses to draw (the <c>mayDraw</c> closure; defaults to drawing — the
///   engine-wide degrade-to-yes posture for unattended / test runs), TWO cards
///   are drawn, then exactly ONE card is discarded. The discard is mandatory
///   <i>iff</i> at least one card was actually drawn ("If you do" — CR 603.5);
///   when the controller declines, neither the draw nor the discard happens. The
///   discard pick uses the <c>discardPick</c> closure, defaulting to the last
///   card in hand (the same deterministic v1 fallback
///   <see cref="CatharticReunionFactory"/> / Faithless Looting use).
/// - <b>Equip {1} (CR 702.6)</b>: activated ability via the
///   <see cref="EquipActivatedAbility"/> primitive. v1 picker is deterministic:
///   the first creature on the controller's battlefield.
///
/// ## "If you do" semantics (CR 603.5)
/// The printed reflexive clause ties the discard to the draw: the discard
/// happens only if cards were drawn. v1 honours this — declining the optional
/// draw skips the discard, and an empty library (zero cards actually drawn)
/// likewise skips the discard. This avoids the classic "must-discard-but-drew-
/// nothing" pitfall.
///
/// ## Lifecycle
/// The single-arg <see cref="Create(Player)"/> overload omits trigger-manager
/// wiring and produces the correct card shape with the trigger + Equip ability
/// attached but not registered — suitable for factory-shape / dispatch /
/// trigger-gating tests. The three-arg overload registers the combat trigger
/// with a <see cref="TriggerManager"/> so a live
/// <see cref="CombatDamageDealtEvent"/> queues the ability automatically.
///
/// ## Deferred (v1 gaps)
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first controller-side
///   creature deterministically (inherited from
///   <see cref="EquipActivatedAbility"/>).
/// - <b>Agent-driven yes/no + discard prompts</b> — v1 honours caller-supplied
///   closures; absent them it degrades to "draw two, discard last-in-hand".
/// </summary>
[CardName("Mask of Memory")]
public static class MaskOfMemoryFactory
{
    public const string CardName = "Mask of Memory";
    public const string PrintedManaCost = "{2}";
    public const string EquipCost = "{1}";
    public const int DrawCount = 2;
    public const int DiscardCount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("mask-of-memory");

    /// <summary>
    /// Construct Mask of Memory with the combat-damage loot trigger and Equip
    /// {1} attached. The optional draw defaults to drawing (CR 603.5 —
    /// degrade-to-yes for unattended runs); the discard defaults to the last
    /// card in hand. The trigger is NOT registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher /
    /// trigger-gating tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, triggers: null, mayDraw: null, discardPick: null);

    /// <summary>
    /// Construct Mask of Memory. When <paramref name="triggers"/> is supplied the
    /// combat-damage-to-a-player trigger is registered so a
    /// <see cref="CombatDamageDealtEvent"/> from the equipped creature (targeting
    /// a player) automatically queues the loot ability. <paramref name="mayDraw"/>
    /// models the controller's "you may draw two cards" yes/no choice (CR 603.5;
    /// null = draw). <paramref name="discardPick"/> chooses the card to discard
    /// from the post-draw hand (null = last card in hand).
    /// </summary>
    public static Artifact Create(
        Player owner,
        TriggerManager? triggers,
        Func<bool>? mayDraw,
        Func<IReadOnlyList<ICard>, ICard?>? discardPick)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510 / CR 603.1.
        //   "Whenever equipped creature deals combat damage to a player, you
        //    may draw two cards. If you do, discard a card."
        // ----------------------------------------------------------------
        var lootEffect = new Effect(
            $"{CardName}: equipped creature dealt combat damage to a player — you may draw two cards, if you do discard a card",
            () =>
            {
                // CR 603.5 — optional draw. Default to drawing when no choice
                // closure is wired (unattended / test runs).
                if (mayDraw != null && !mayDraw()) return;

                // CR 603.3c — the granted ability's controller is the
                // controller of the equipment. Fall back to the owner if
                // (somehow) the controller is null at resolution.
                var lootFor = card.Controller ?? owner;

                // "Draw two cards." (CR 121.1) — count what is actually drawn so
                // the reflexive discard tracks "If you do" (CR 603.5).
                var drawn = DrawCards(lootFor, DrawCount);

                // "If you do, discard a card." — the discard is mandatory only
                // when at least one card was actually drawn. Declining the draw
                // (above) or drawing nothing from an empty library skips it.
                if (drawn == 0) return;
                DiscardOne(lootFor, discardPick);
            });

        var combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                // Combat damage to a PLAYER (non-null TargetPlayer), not to a
                // creature / planeswalker.
                if (e.TargetPlayer == null) return false;

                // Source must be the currently-equipped creature (read
                // dynamically so re-equipping redirects the trigger).
                var equipped = card.AttachedTo;
                if (equipped == null) return false;
                return ReferenceEquals(e.Source, equipped);
            }),
            effects: new IEffect[] { lootEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(combatTrigger);
        triggers?.RegisterTriggeredAbility(combatTrigger);

        // ----------------------------------------------------------------
        // Equip {1} — activated ability (CR 702.6).
        // ----------------------------------------------------------------
        card.AddAbility(new EquipActivatedAbility(source: card, cost: EquipCost));

        return card;
    }

    /// <summary>
    /// Draw up to <paramref name="count"/> cards for <paramref name="player"/>
    /// via raw library → hand zone moves, returning the number actually drawn.
    /// An empty library mid-draw stamps
    /// <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> (CR 704.5b / 120.3)
    /// and short-circuits the remaining draws. Mirrors
    /// <see cref="CatharticReunionFactory"/>'s draw shape.
    /// </summary>
    private static int DrawCards(Player player, int count)
    {
        var drawn = 0;
        for (var i = 0; i < count; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null)
            {
                player.MarkTriedToDrawFromEmptyLibrary();
                break;
            }
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
            drawn++;
        }
        return drawn;
    }

    /// <summary>
    /// Discard exactly one card (CR 701.16) from <paramref name="player"/>'s
    /// hand. The pick uses <paramref name="discardPick"/> when supplied (a null
    /// or out-of-hand pick falls back to the deterministic default), otherwise
    /// the last card in hand — the same v1 fallback
    /// <see cref="CatharticReunionFactory"/> uses. No-ops on an empty hand.
    /// </summary>
    private static void DiscardOne(
        Player player,
        Func<IReadOnlyList<ICard>, ICard?>? discardPick)
    {
        var hand = player.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return;

        ICard? pick = discardPick?.Invoke(hand);
        if (pick == null || pick.Zone != ZoneType.Hand || !hand.Contains(pick))
            pick = hand[^1];

        player.Zones.Hand.RemoveCard(pick);
        player.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
    }
}
