using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Diamond Pick-Axe (Outlaws of Thunder Junction,
/// {R}).
///
/// Artifact — Equipment. Oracle text (verified against the embedded seed,
/// 2026-06-24):
///   "Indestructible (Effects that say "destroy" don't destroy this
///    Equipment.)"
///   "Equipped creature gets +1/+1 and has "Whenever this creature attacks,
///    create a Treasure token." (It's an artifact with "{T}, Sacrifice this
///    token: Add one mana of any color.")"
///   "Equip {2}"
///
/// ## Implementation
///
/// Diamond Pick-Axe is the <see cref="GoldveinPickFactory"/> shape (Equipment
/// granting +1/+1 plus a Treasure payoff) with two differences: the equipment
/// is itself Indestructible (One Ring's marker pattern), and the Treasure
/// trigger is "Whenever <b>this creature attacks</b>" rather than
/// "deals combat damage to a player" — it's a triggered ability granted to the
/// equipped creature, so it fires when the equipped creature is declared as an
/// attacker.
///
/// - <b>Indestructible</b> (CR 702.12) — a <see cref="KeywordAbility"/>
///   ("Indestructible") marker on the equipment card itself, read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasIndestructible"/> /
///   SBA 704.5g. Same marker pattern as
///   <see cref="TheOneRingFactory"/>. This is an intrinsic static keyword on
///   the equipment, not granted to the bearer.
/// - <b>Static "equipped creature gets +1/+1"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (CR 613 Layer 7c). The
///   effect reads <see cref="Permanent.AttachedTo"/> dynamically, so
///   re-equipping transfers the boost without re-registration. Same shape as
///   Goldvein Pick's +1/+1.
/// - <b>Attack trigger (CR 508.1 / CR 603.1)</b> — printed on the equipped
///   creature as "Whenever this creature attacks, create a Treasure token."
///   Modelled as a <see cref="TriggeredAbility"/> over
///   <see cref="AttackersDeclaredEvent"/> sourced from the equipment,
///   filtered to the currently-equipped creature
///   (<see cref="Permanent.AttachedTo"/> at trigger-evaluation time) being
///   among the declared attackers. Because the printed text grants the
///   ability to the equipped creature, the trigger reads the live
///   <c>AttachedTo</c> each time, so re-equipping moves the trigger to the new
///   bearer. On resolution it creates a single Treasure token under the
///   equipment controller via <see cref="TokenFactory.CreateTreasure"/>
///   (CR 111.10 — a colourless artifact token with "{T}, Sacrifice this token:
///   Add one mana of any color.").
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via the
///   <see cref="EquipActivatedAbility"/> primitive, with the Puresteel
///   zero-equip cost-provider hook. v1 picker is deterministic: the first
///   creature on the controller's battlefield.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests. The attack trigger is attached for shape
/// but not registered with a <see cref="TriggerManager"/>; the static +1/+1
/// boost is not registered against any
/// <see cref="ContinuousEffectsService"/>.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically (inherited from
///   <see cref="EquipActivatedAbility"/>).
/// - The shape-only <see cref="Create(Player)"/> path resolves the Treasure
///   via the direct-add fallback in
///   <see cref="TokenFactory.CreateTreasure"/> (no <see cref="ZoneService"/>);
///   the fully-wired overload routes through the supplied
///   <see cref="ZoneService"/> so the enters-the-battlefield
///   <see cref="CardMovedEvent"/> fires.
/// </summary>
[CardName("Diamond Pick-Axe")]
public static class DiamondPickAxeFactory
{
    public const string CardName = "Diamond Pick-Axe";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "diamond-pick-axe";

    public const string EquipCost = "{2}";

    /// <summary>+1/+1 the equipped creature gets (CR 613 Layer 7c).</summary>
    public const int PowerBoost = 1;

    /// <summary>+1/+1 the equipped creature gets (CR 613 Layer 7c).</summary>
    public const int ToughnessBoost = 1;

    /// <summary>
    /// Constructs Diamond Pick-Axe with no live runtime wiring (the shape /
    /// dispatcher path). The +1/+1 boost is not registered against any
    /// service; the attack trigger is attached to the card but not registered
    /// with a <see cref="TriggerManager"/>. Treasure creation on the shape-only
    /// path falls through the direct-add token fallback.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null, zones: null);

    /// <summary>
    /// Constructs Diamond Pick-Axe. When <paramref name="continuousEffects"/>
    /// is supplied the +1/+1 boost (Layer 7c) is registered against it; the
    /// effect gates on the Pick-Axe being on the battlefield AND attached to a
    /// battlefield permanent. When <paramref name="triggers"/> is supplied the
    /// attack trigger is registered so an <see cref="AttackersDeclaredEvent"/>
    /// that includes the equipped creature automatically queues the ability.
    /// The Treasure is created under <paramref name="zones"/> when supplied so
    /// the enters-the-battlefield event fires.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Artifact / Equipment / {R}) from the embedded
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
        // Indestructible (CR 702.12) — marker keyword on the equipment
        // itself. Read by CombatAbilities.HasIndestructible / SBA 704.5g.
        // Same pattern as The One Ring. NOT granted to the bearer.
        // --------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Indestructible", card, owner));

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
        // Attack trigger — CR 508.1 / CR 603.1.
        //   Equipped creature has "Whenever this creature attacks, create a
        //   Treasure token."
        // Fires on AttackersDeclaredEvent when the currently-equipped
        // creature (card.AttachedTo at evaluation time) is among the declared
        // attackers. Reading AttachedTo live means re-equipping moves the
        // granted trigger to the new bearer (CR 613.1f spirit — the ability
        // rides the equipped creature).
        // --------------------------------------------------------------
        var treasureEffect = new Effect(
            $"{CardName}: create a Treasure token",
            () =>
            {
                // CR 111.10 — Treasure token: colourless artifact with
                // "{T}, Sacrifice this token: Add one mana of any color."
                // TokenFactory.CreateTreasure handles the full spec.
                TokenFactory.CreateTreasure(card.Controller ?? owner, zones);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
            {
                var equipped = card.AttachedTo;
                if (equipped == null) return false;
                foreach (var atk in e.Combat.Attackers)
                {
                    if (atk?.Creature != null && ReferenceEquals(atk.Creature, equipped))
                        return true;
                }
                return false;
            }),
            effects: new IEffect[] { treasureEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

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
}
