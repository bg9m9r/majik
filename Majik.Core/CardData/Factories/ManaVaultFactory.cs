using Majik.Core.Abilities;
using Majik.Core.Cards;
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
/// - The "you may" defaults to auto-pay when the controller has the mana
///   available; absent enough mana, the damage path fires. This mirrors
///   every other pact-style "pay {X} or else" the engine already ships
///   (Slaughter Pact / Pact of Negation / Pact of the Titan).
///
/// ## Deferred (v1 gaps)
/// - <b>"Doesn't untap during your untap step" static</b>: there is no
///   `SkipNextUntap` / `DoesntUntapDuringUntapStep` engine surface today
///   (grep for "SkipNextUntap"/"DoesntUntap"/"doesn't untap" — nothing in
///   <c>Majik.Core/</c>). Adding one is a real chunk of work (UntapStep
///   filter + per-permanent stash + interaction with effects like Voltaic
///   Key that explicitly untap something else). Per the planning note,
///   we ship the mana ability + upkeep cost first and defer the skip-
///   untap clause. Practical impact: until the static lands, Mana Vault
///   will untap normally on its controller's untap step and the upkeep
///   "if tapped" gate won't fire — playable but not yet Vintage-correct.
/// - <b>Cost-payment prompt</b>: same surface gap as Slaughter Pact /
///   Pact of Negation — there's no agent prompt yet for "do you want to
///   pay this {4}?", so production callers pre-stage the controller's
///   mana pool. The trigger consumes whatever mana is already in the
///   pool; if {4}-worth is sitting there, it's paid, otherwise the damage
///   fires. The "may" is therefore implicit-pay-if-able.
/// - <b>Full <see cref="DamageDealtEvent"/> route</b>: the 1 damage goes
///   through <see cref="Player.LoseLife"/>; subscribers that care about
///   damage prevention won't see Mana Vault's ping. Same scope decision
///   as Dark Confidant / Manabarbs.
/// </summary>
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
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Mana Vault with optional trigger-manager wiring. When
    /// <paramref name="triggers"/> is supplied, the upkeep triggered
    /// ability is registered so an Upkeep <see cref="StepStartedEvent"/>
    /// for the controller surfaces it as pending.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers)
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
        var upkeepEffect = new Effect(
            "Mana Vault: at upkeep if tapped, pay {4} or take 1 damage",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (!card.IsTapped) return;

                var controller = card.Controller ?? owner;
                var cost = ManaCost.Parse("4");

                // Auto-pay if the pool has enough; LoseLife(1) on failure.
                // The v1 "may" collapses to "pay-if-able"; the prompt
                // surface to decline is the same gap shared with the
                // pact-cycle factories.
                if (!controller.PayMana(cost))
                {
                    controller.LoseLife(1);
                }
            });

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, PhaseStateType.Upkeep),
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

        return card;
    }
}
