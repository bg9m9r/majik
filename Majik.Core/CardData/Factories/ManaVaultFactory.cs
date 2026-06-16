using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mana Vault (Limited Edition Alpha, {1}).
///
/// Artifact. Oracle text:
///   "Mana Vault doesn't untap during your untap step."
///   "At the beginning of your upkeep, if Mana Vault is tapped, you may
///    pay {4}. If you don't, Mana Vault deals 1 damage to you."
///   "{T}: Add {C}{C}{C}."
///
/// ## Implemented (v1)
/// - <b>Tap mana ability (CR 605)</b>: <see cref="ManaAbility"/> with the
///   static-amount overload, taps Mana Vault and adds three colourless
///   (<see cref="ManaCost.Parse"/> routes {C} through the generic bucket
///   per CR 107.4c — <c>Parse("CCC")</c> yields <c>Generic == 3</c>).
/// - <b>Upkeep pay-or-damage trigger (CR 603.1 / CR 500.4 / CR 603.4)</b>:
///   a <see cref="TriggeredAbility"/> over <see cref="StepStartedEvent"/>
///   filtered to (Upkeep, controller). At resolution the effect re-checks
///   the printed "intervening if" — Mana Vault must still be on the
///   battlefield and tapped — then attempts <see cref="Player.PayMana"/>
///   with {4} against the controller's mana pool. If the payment fails,
///   the controller loses 1 life (Mana Vault dealing damage to its
///   controller — same v1 simplification as Manabarbs / Dark Confidant
///   where ability damage routes through <see cref="Player.LoseLife"/>
///   rather than a full <see cref="DamageDealtEvent"/>).
/// - The "you may" is a real agent decision: at resolution the controller's
///   agent is prompted "Pay {4}?" via the shared
///   <see cref="Majik.Core.Primitives.UpkeepPayUnlessConsequence"/>
///   primitive. On "yes" + affordable the {4} is drained and no damage is
///   dealt; on "no" / can't-afford the damage path fires. Same wiring the
///   pact cycle / Stasis / Kataki now share.
///
/// - <b>"Doesn't untap during your untap step" static (CR 502.1)</b>:
///   wired via <see cref="DoesNotUntapStaticEffect"/>. On enter-the-
///   battlefield the lifecycle registers a per-permanent skip with
///   <see cref="UntapStepRestrictions"/>; TurnDriver's UntapStep
///   consults the registry and skips this permanent. On LTB the
///   registration is removed. Pass an <see cref="IEventBus"/> to
///   <see cref="Create(Player, TriggerManager?, IEventBus?)"/> to
///   activate the lifecycle (it sync-attaches via
///   <see cref="CardMovedEvent"/>); the no-arg overloads still build
///   shape without auto-attaching so existing structural tests keep
///   working unchanged.
///
/// ## Deferred (v1 gaps)
/// - <b>No in-trigger tap-lands step</b>: the {4} is paid from whatever is
///   already in the controller's pool when the trigger resolves — the
///   decision to pay now flows through the agent prompt, but there is still
///   no resolution-time "tap a land for {4}" sub-prompt.
/// - <b>Full <see cref="DamageDealtEvent"/> route</b>: the 1 damage goes
///   through <see cref="Player.LoseLife"/>; subscribers that care about
///   damage prevention won't see Mana Vault's ping. Same scope decision
///   as Dark Confidant / Manabarbs.
/// </summary>
[CardName("Mana Vault")]
public static class ManaVaultFactory
{
    public const string CardName = "Mana Vault";
    public const string PrintedManaCost = "{1}";
    public const string UpkeepCost = "{4}";

    /// <summary>
    /// Construct Mana Vault with no live trigger-manager wiring. The
    /// upkeep ability is attached to the card's
    /// <see cref="Card.Abilities"/> collection so structural shape tests
    /// can observe it; for end-to-end firing pass a live
    /// <see cref="TriggerManager"/> via the overload.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Mana Vault with optional trigger-manager wiring. When
    /// <paramref name="triggers"/> is supplied, the upkeep triggered
    /// ability is registered so an Upkeep <see cref="StepStartedEvent"/>
    /// for the controller surfaces it as pending.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers) =>
        Create(owner, triggers, eventBus: null);

    /// <summary>
    /// Construct Mana Vault with optional trigger-manager + event-bus
    /// wiring. When <paramref name="eventBus"/> is supplied, the
    /// <see cref="DoesNotUntapStaticEffect"/> lifecycle is attached so
    /// the printed "Mana Vault doesn't untap during your untap step"
    /// clause activates on ETB and lifts on LTB (CR 502.1).
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Upkeep trigger — CR 603.1, CR 500.4. The intervening "if" clause
        // (CR 603.4) is re-checked at resolution: Mana Vault must still be
        // on the battlefield and tapped. Pay {4} from the controller's
        // mana pool (auto-pay-if-able v1); on failure, deal 1 damage to
        // the controller via Player.LoseLife.
        // ----------------------------------------------------------------
        // At resolution the controller's agent is prompted "Pay {4}?" via the
        // shared Majik.Core.Primitives.UpkeepPayUnlessConsequence primitive
        // (CR 117.1). On "yes" + affordable {4} is drained and no damage is
        // dealt; on "no" / can't-afford the controller loses 1 life (Mana
        // Vault's ping, routed through Player.LoseLife per the Dark Confidant /
        // Manabarbs scope decision). The printed "if tapped" intervening-if
        // (CR 603.4) is the guard; the legacy / shape-only sync path keeps the
        // deterministic "pay if able" posture.
        var upkeepEffect = Majik.Core.Primitives.UpkeepPayUnlessConsequence.Build(
            "Mana Vault: at upkeep if tapped, pay {4} or take 1 damage",
            owner,
            ManaCost.Parse("4"),
            consequence: () => (card.Controller ?? owner).LoseLife(1),
            promptText: "Pay {4} to keep Mana Vault tapped without taking 1 damage?",
            guard: () => card.Zone == ZoneType.Battlefield && card.IsTapped);

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        // ----------------------------------------------------------------
        // {T}: Add {C}{C}{C}.  ManaCost.Parse("CCC") buckets three {C} into
        // Generic = 3 (CR 107.4c — engine collapses colourless to generic).
        // ----------------------------------------------------------------
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("CCC")));

        // ----------------------------------------------------------------
        // CR 502.1 — "Mana Vault doesn't untap during your untap step."
        // Wired via the lifecycle binder; only attaches when an event bus
        // is supplied so the shape-only constructors stay zero-side-
        // effect for structural tests that don't drive zone moves.
        // ----------------------------------------------------------------
        if (eventBus != null)
        {
            new DoesNotUntapStaticEffect(card, eventBus).Attach();
        }

        return card;
    }
}
