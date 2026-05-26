using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nettle Sentinel (Eventide, {G}).
///
/// Creature — Elf Warrior 2/2. Oracle text:
///   "Nettle Sentinel doesn't untap during your untap step.
///    Whenever you cast a green spell, you may untap Nettle Sentinel."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Elf Warrior, mana cost {G}, owner/controller stamped.
/// - <b>"Doesn't untap during your untap step" static (CR 502.1)</b>:
///   wired via <see cref="DoesNotUntapStaticEffect"/>. On enter-the-
///   battlefield the lifecycle registers a per-permanent skip with
///   <see cref="UntapStepRestrictions"/>; TurnDriver's UntapStep consults
///   the registry and skips Nettle Sentinel. On LTB the registration is
///   removed. Pass an <see cref="IEventBus"/> to the
///   <see cref="Create(Player, TriggerManager?, IEventBus?)"/> overload
///   to activate the lifecycle (it sync-attaches via
///   <see cref="CardMovedEvent"/>); the no-arg overload still builds the
///   shape without auto-attaching for structural tests.
/// - <b>Untap-on-green-spell trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/>
///   filtered to (spell controller = Nettle Sentinel's controller) AND
///   (spell colour set contains <see cref="ManaColor.Green"/> per CR 105
///   / <see cref="CardColors.GetColors"/>). On resolve the effect calls
///   <see cref="Permanent.Untap"/> on Nettle Sentinel itself (CR 701.20).
///   The "you may" is auto-accepted in v1 (untapping a creature you
///   control is never a downside — same posture as Bloodghast's "may
///   return" and Sun Titan's "may reanimate").
/// - The trigger fires even when Nettle Sentinel is already untapped —
///   <see cref="Permanent.Untap"/> is idempotent so an already-untapped
///   sentinel is a no-op (CR 701.20 — "untap an untapped permanent" is
///   harmless).
///
/// ## Deferred (v1 gaps)
/// - <b>"You may"</b>: auto-accepted (same gap as Bloodghast / Tireless
///   Tracker / Sun Titan).
/// - <b>Mana-ability cast → untap chain</b>: Nettle Sentinel + Heritage
///   Druid is the canonical Elfball loop. Heritage Druid's "tap three
///   untapped Elves" mana ability is separate; this factory only
///   provides the untap-on-green-spell-cast half. The chain works as
///   soon as Heritage Druid's mana ability lands a SpellCastEvent for a
///   green spell on the bus.
/// </summary>
[CardName("Nettle Sentinel")]
public static class NettleSentinelFactory
{
    public const string CardName = "Nettle Sentinel";
    public const string PrintedManaCost = "{G}";

    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Nettle Sentinel with no live TriggerManager / IEventBus
    /// wiring. The untap-on-green-spell trigger is attached for shape but
    /// isn't registered with a bus, and the doesn't-untap lifecycle
    /// binder isn't attached (so structural tests don't get a stray
    /// registration in <see cref="UntapStepRestrictions"/>).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Nettle Sentinel with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the untap-on-green-spell
    /// trigger is registered with the bus so a
    /// <see cref="SpellCastEvent"/> for a green spell cast by the
    /// controller surfaces the ability as pending.</param>
    /// <param name="eventBus">When supplied, the
    /// <see cref="DoesNotUntapStaticEffect"/> lifecycle binder is
    /// attached so the printed "doesn't untap during your untap step"
    /// clause activates on ETB and lifts on LTB (CR 502.1). Without a
    /// bus, the doesn't-untap registration is skipped.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 502.1 — "Nettle Sentinel doesn't untap during your untap
        // step." Wired via the lifecycle binder; only attaches when an
        // event bus is supplied so the shape-only constructor stays
        // zero-side-effect for structural tests that don't drive zone
        // moves. Mirrors ManaVaultFactory's lifecycle wiring.
        // ----------------------------------------------------------------
        if (eventBus != null)
        {
            new DoesNotUntapStaticEffect(card, eventBus).Attach();
        }

        // ----------------------------------------------------------------
        // Untap-on-green-spell-cast trigger — CR 603.1. The condition
        // filters SpellCastEvent to (cast by the controller) AND (spell's
        // colour set contains Green per CR 105). The effect untaps
        // Nettle Sentinel itself; CR 701.20 makes untapping an already-
        // untapped permanent a no-op.
        // ----------------------------------------------------------------
        var untapTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<SpellCastEvent>((e, _) =>
            {
                if (e?.Spell == null) return false;
                var caster = e.Spell.Controller;
                if (caster == null) return false;
                if (!ReferenceEquals(caster, card.Controller ?? owner)) return false;
                var spellCard = e.Spell.Card;
                if (spellCard == null) return false;
                return CardColors.GetColors(spellCard).Contains(ManaColor.Green);
            }),
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: untap self (whenever you cast a green spell)",
                    () =>
                    {
                        // CR 701.20 — "untap an untapped permanent" is a no-op
                        // by intent. Permanent.Untap() throws when not tapped,
                        // so guard with IsTapped before calling.
                        if (card.IsTapped) card.Untap();
                    }),
            },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(untapTrigger);
        triggers?.RegisterTriggeredAbility(untapTrigger);

        return card;
    }
}
