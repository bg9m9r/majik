using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Burning-Tree Emissary (Dragon's Maze / Modern
/// Masters reprints, {R/G}{R/G}).
///
/// Creature — Human Shaman 2/2. Oracle text:
///   "When this creature enters, add {R}{G}."
///
/// ## Implementation
///
/// - 2/2 Creature — Human Shaman, mana cost {R/G}{R/G} (CR 107.4e hybrid
///   pips — <see cref="ManaCost.Parse"/> decomposes each pip into a
///   <c>HybridPip</c>, same as Kitchen Finks / Boros Reckoner).
///
/// - <b>ETB triggered ability (CR 603.6a)</b>: wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. On resolution the
///   controller's mana pool gains {R}{G} via
///   <see cref="Player.AddManaToPool"/> (CR 106.4 — mana goes into the
///   pool, available for spell casting in the same priority window). This
///   is the mechanic that makes Burning-Tree Emissary self-chain in
///   Bushwhacker / Ponza shells: ETB-mana refunds the {R/G}{R/G} cost
///   exactly, so a string of Emissaries plus a one-drop is cost-neutral.
///
/// The mana is added on <i>resolution</i> of the trigger (not on ETB
/// itself); CR 603.6a — ETB triggers go on the stack like any other
/// triggered ability and resolve before priority returns to the active
/// player. The empty oracle's "if you do" / "if able" gating is absent —
/// the mana add is unconditional.
///
/// Lifecycle wiring: same shape as
/// <see cref="OmnathLocusOfCreationFactory"/>'s ETB draw —
/// <see cref="TriggerManager.RegisterTriggeredAbility"/> when a manager is
/// supplied; the single-arg overload attaches the trigger structurally so
/// dispatcher / shape tests still observe it.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Mana-burn timing</b>: produced mana stays in the pool until the
///   next end-of-step empty (CR 106.4) — same as every other mana
///   producer in the engine.
/// - <b>"You may"-prompted variants</b>: irrelevant here (the trigger has
///   no may clause).
/// </summary>
[CardName("Burning-Tree Emissary")]
public static class BurningTreeEmissaryFactory
{
    public const string CardName = "Burning-Tree Emissary";
    public const string PrintedManaCost = "{R/G}{R/G}";

    /// <summary>Mana produced by the ETB trigger.</summary>
    public const string ManaProduced = "RG";

    /// <summary>
    /// Construct Burning-Tree Emissary with no live <see cref="TriggerManager"/>
    /// wiring. The ETB trigger attaches to the card so structural /
    /// dispatcher tests observe its shape; live event-driven firing
    /// requires the <see cref="Create(Player, TriggerManager?)"/> overload
    /// with a non-null manager.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct Burning-Tree Emissary with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied the ETB
    /// triggered ability is registered so an ETB <see cref="CardMovedEvent"/>
    /// queues the mana add automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB — "When this creature enters, add {R}{G}." CR 603.6a + 106.4.
        // Unconditional mana add to the controller's pool on resolution.
        // Mirrors OmnathLocusOfCreationFactory.Create's ETB shape.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: ETB — add {{R}}{{G}}",
            () =>
            {
                var controller = card.Controller ?? owner;
                // CR 106.4 — pips are deposited individually into the
                // mana pool. ManaCost.Parse("RG") yields one red + one
                // green pip; AddManaToPool drops both in.
                controller.AddManaToPool(ManaCost.Parse(ManaProduced));
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
