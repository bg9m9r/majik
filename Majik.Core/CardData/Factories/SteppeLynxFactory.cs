using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Steppe Lynx (Zendikar, {W}).
///
/// Creature — Cat 0/1. Oracle text:
///   "Landfall — Whenever a land you control enters, this creature gets
///    +2/+2 until end of turn."
///
/// The Zendikar landfall aggro one-drop: base 0/1 that swings as a 2/3
/// every turn you make a land drop. Same landfall trigger predicate as
/// <see cref="HedronCrabFactory"/> / <see cref="LotusCobraFactory"/>
/// (<see cref="Triggers.OnLandEntersUnderControl"/>, CR 603.6a); the
/// resolve body registers a self-targeted
/// <see cref="PumpUntilEndOfTurnEffect"/>(+2, +2) — the same pump
/// primitive used by Giant Growth / Become Immense / Berserk
/// (Layer 7c, CR 613.1g; expiry CR 514.2).
///
/// ## Implemented (v1)
/// - 0/1 Creature — Cat, mana cost {W}, owner / controller wired.
/// - <b>Landfall triggered ability</b> (CR 603.1 / 603.6a / CR 702.142)
///   — fires on a <see cref="Majik.Core.Events.CardMovedEvent"/> filtered
///   to "a land entering the battlefield under the controller's control"
///   via the shared <see cref="Triggers.OnLandEntersUnderControl"/>
///   predicate. No <see cref="TargetRequest"/>: the pump always affects
///   the Lynx itself, so there is nothing to target (CR 603.6a — the
///   effect names "this creature").
/// - <b>Resolve — +2/+2 until end of turn</b>: registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+2, +2) on the Lynx's own
///   <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires in the
///   cleanup step). When <see cref="Creature.ActiveEffects"/> is null
///   (shape-only tests with no live <see cref="ContinuousEffectsService"/>)
///   the registration is a no-op — mirrors
///   <see cref="GiantGrowthFactory"/>'s resolve.
///
/// ## Deferred (v1 gaps)
/// - <b>Trigger registration</b>: the shape-only <see cref="Create(Player)"/>
///   path attaches the trigger to the card for inspection but does not
///   register it with a bus. Use the
///   <see cref="Create(Player, TriggerManager)"/> overload for live firing.
/// </summary>
[CardName("Steppe Lynx")]
public static class SteppeLynxFactory
{
    public const string CardName = "Steppe Lynx";
    public const string PrintedManaCost = "{W}";
    public const int Power = 0;
    public const int Toughness = 1;

    /// <summary>Layer 7c +P/+T magnitude granted on each landfall
    /// (CR 613.1g).</summary>
    public const int PumpAmount = 2;

    /// <summary>
    /// Construct Steppe Lynx with no live <see cref="TriggerManager"/>
    /// wiring. The landfall trigger is attached for shape inspection but
    /// not registered with a bus. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Steppe Lynx. When <paramref name="triggers"/> is supplied
    /// the landfall trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering
    /// under the controller's control automatically queues the ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Cat });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Landfall — CR 603.1 / 603.6a / CR 702.142.
        //   "Whenever a land you control enters, this creature gets +2/+2
        //    until end of turn."
        // Predicate is shared with Hedron Crab / Lotus Cobra / Tireless
        // Provisioner. No target: the pump always affects the Lynx itself.
        // On resolve, register a self-targeted +2/+2 PumpUntilEndOfTurnEffect
        // (Layer 7c CR 613.1g; expiry CR 514.2) on the Lynx's own
        // ActiveEffects — the same pump primitive used by Giant Growth /
        // Become Immense / Berserk.
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: landfall — this creature gets +{PumpAmount}/+{PumpAmount} until end of turn",
            () =>
            {
                // ActiveEffects is null in shape-only tests (no live
                // ContinuousEffectsService) — no-op, mirroring Giant Growth.
                card.ActiveEffects?.Register(
                    new PumpUntilEndOfTurnEffect(card, PumpAmount, PumpAmount));
            });

        var landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { pumpEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);

        return card;
    }
}
