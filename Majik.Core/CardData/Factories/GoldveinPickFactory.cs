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

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goldvein Pick (Kaladesh, {1}).
///
/// Artifact — Equipment. Oracle text (verified against Scryfall
/// 2026-06-24):
///   "Equipped creature gets +1/+1."
///   "Whenever equipped creature deals combat damage to a player, create a
///    Treasure token. (It's an artifact with '{T}, Sacrifice this token:
///    Add one mana of any color.')"
///   "Equip {1}"
///
/// ## Implementation
///
/// Goldvein Pick is the <see cref="SwordOfFireAndIceFactory"/> shape with a
/// smaller boost and a different combat payoff:
/// - <b>Static "equipped creature gets +1/+1"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (CR 613 Layer 7c). The
///   effect reads <see cref="Permanent.AttachedTo"/> dynamically, so
///   re-equipping transfers the boost without re-registration. Same shape
///   as Sword of Fire and Ice's +2/+2.
/// - <b>Combat-damage-to-a-player trigger (CR 510, CR 603.1)</b> — wired
///   over <see cref="CombatDamageDealtEvent"/> filtered to the equipped
///   creature (<see cref="Permanent.AttachedTo"/> at trigger-evaluation
///   time) and a non-null <see cref="CombatDamageDealtEvent.TargetPlayer"/>
///   (printed text is "to a player"). On resolution it creates a single
///   Treasure token under the equipment controller via
///   <see cref="TokenFactory.CreateTreasure"/> (CR 111.10 — a colourless
///   artifact token with "{T}, Sacrifice this token: Add one mana of any
///   color."). No "any target" — the trigger has no targets, unlike the
///   Sword.
/// - <b>Equip {1}</b> — activated ability (CR 702.6) via the
///   <see cref="EquipActivatedAbility"/> primitive, with the Puresteel
///   zero-equip cost-provider hook. v1 picker is deterministic: the first
///   creature on the controller's battlefield.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests. The combat trigger is attached for shape
/// but not registered with a <see cref="TriggerManager"/>; the static
/// +1/+1 boost is not registered against any
/// <see cref="ContinuousEffectsService"/>.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically (inherited from
///   <see cref="EquipActivatedAbility"/>).
/// - The shape-only <see cref="Create(Player)"/> path resolves the
///   Treasure via the direct-add fallback in
///   <see cref="TokenFactory.CreateTreasure"/> (no
///   <see cref="ZoneService"/>); the fully-wired overload routes through
///   the supplied <see cref="ZoneService"/> so the enters-the-battlefield
///   <see cref="CardMovedEvent"/> fires.
/// </summary>
[CardName("Goldvein Pick")]
public static class GoldveinPickFactory
{
    public const string CardName = "Goldvein Pick";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "goldvein-pick";

    public const string EquipCost = "{1}";

    /// <summary>+1/+1 the equipped creature gets (CR 613 Layer 7c).</summary>
    public const int PowerBoost = 1;

    /// <summary>+1/+1 the equipped creature gets (CR 613 Layer 7c).</summary>
    public const int ToughnessBoost = 1;

    /// <summary>
    /// Constructs Goldvein Pick with no live runtime wiring (the shape /
    /// dispatcher path). The +1/+1 boost is not registered against any
    /// service; the combat-damage trigger is attached to the card but not
    /// registered with a <see cref="TriggerManager"/>. Treasure creation on
    /// the shape-only path falls through the direct-add token fallback.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null, zones: null);

    /// <summary>
    /// Constructs Goldvein Pick. When
    /// <paramref name="continuousEffects"/> is supplied the +1/+1 boost
    /// (Layer 7c) is registered against it; the effect gates on the Pick
    /// being on the battlefield AND attached to a battlefield permanent.
    /// When <paramref name="triggers"/> is supplied the
    /// combat-damage-to-a-player trigger is registered so a
    /// <see cref="CombatDamageDealtEvent"/> from the equipped creature
    /// (targeting a player) automatically queues the ability. The Treasure
    /// is created under <paramref name="zones"/> when supplied so the
    /// enters-the-battlefield event fires.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Artifact / Equipment / {1}) from the embedded
        // JSON definition.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Artifact card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Artifact but got "
                + $"'{built.GetType().Name}'.");
        }

        // --------------------------------------------------------------
        // Static continuous effect — "Equipped creature gets +1/+1."
        // CR 613 Layer 7c. The effect gates on the source being on the
        // battlefield AND attached (see AttachedBoostEffect.IsActive).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: PowerBoost, toughness: ToughnessBoost));
        }

        // --------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510 / CR 603.1.
        //   "Whenever equipped creature deals combat damage to a player,
        //    create a Treasure token."
        // Matches any CombatDamageDealtEvent whose Source is the
        // currently-equipped creature AND TargetPlayer != null (the printed
        // text is "to a player", not the broader "deals combat damage").
        // --------------------------------------------------------------
        var treasureEffect = new Effect(
            $"{CardName}: create a Treasure token",
            () =>
            {
                // CR 111.10 — Treasure token: colourless artifact with
                // "{T}, Sacrifice this token: Add one mana of any color."
                // TokenFactory.CreateTreasure handles the full spec.
                TokenFactory.CreateTreasure(owner, zones);
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
            effects: new IEffect[] { treasureEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(combatTrigger);
        triggers?.RegisterTriggeredAbility(combatTrigger);

        // --------------------------------------------------------------
        // Equip {1} — activated ability (CR 702.6) via the
        // EquipActivatedAbility primitive.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
