using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mana Crypt (Mercadian Masques media-insert /
/// Eternal Masters / multiple reprints).
///
/// Artifact — {0}. Oracle text:
///   "At the beginning of your upkeep, flip a coin. If you lose the flip,
///    Mana Crypt deals 3 damage to you."
///   "{T}: Add {C}{C}."
///
/// ## Implemented (v1)
/// - <b>Tap mana ability (CR 605)</b>: <see cref="ManaAbility"/> with the
///   static-amount overload, taps Mana Crypt and adds two colourless
///   (<see cref="ManaCost.Parse"/> routes {C} through the generic bucket
///   per CR 107.4c — <c>Parse("CC")</c> yields <c>Generic == 2</c>).
///   Sister to <see cref="SolRingFactory"/>'s mana ability — Mana Crypt
///   trades Sol Ring's {1} cost for a coin-flip damage rider.
/// - <b>Upkeep coin-flip damage trigger (CR 603.1 / CR 500.4)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="StepStartedEvent"/>
///   filtered to (Upkeep, controller). At resolution the effect calls the
///   injected coin source — true means "you lose the flip" (asymmetric:
///   the controller calls and either calls correctly or not; the engine
///   just samples a 50/50 bit) — and on a loss the controller takes 3
///   damage via <see cref="Player.LoseLife"/>. Same v1 simplification as
///   <see cref="ManaVaultFactory"/> / Manabarbs / Dark Confidant where
///   ability damage routes through <see cref="Player.LoseLife"/> rather
///   than a full <see cref="DamageDealtEvent"/>.
/// - <b>Coin-flip seam</b>: the optional <c>coinLoses</c> Func lets tests
///   force-loss / force-win deterministically. Default is
///   <see cref="System.Random.Shared"/>-backed (a fresh sample per
///   activation — there's no <c>GameRandom</c> threading through factory
///   constructors yet). The engine's <see cref="Random.GameRandom"/>
///   isn't wired into the factory dispatch path today (no factory takes
///   it), so the factory exposes the seam directly to callers who care
///   about determinism (tests + future replay).
///
/// ## Deferred (v1 gaps)
/// - <b>Full <see cref="DamageDealtEvent"/> route</b>: the 3 damage goes
///   through <see cref="Player.LoseLife"/>; damage-prevention subscribers
///   won't see Mana Crypt's ping. Same scope decision as Mana Vault /
///   Manabarbs / Dark Confidant.
/// - <b>Per-game RNG threading</b>: see above — when a future PR threads
///   <see cref="Random.GameRandom"/> through the NamedCardFactory
///   dispatch, the default flip source should switch to
///   <c>game.Random.FlipCoin()</c>.
/// </summary>
public static class ManaCryptFactory
{
    public const string CardName = "Mana Crypt";
    public const string PrintedManaCost = "{0}";

    /// <summary>
    /// Construct Mana Crypt with no live trigger-manager wiring and a
    /// default <see cref="System.Random.Shared"/>-backed coin flip. The
    /// upkeep trigger is attached to the card's <see cref="Card.Abilities"/>
    /// collection so structural shape tests can observe it; for end-to-end
    /// firing pass a live <see cref="TriggerManager"/> via the overload.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, triggers: null, coinLoses: null);

    /// <summary>
    /// Construct Mana Crypt with optional trigger-manager wiring.
    /// <paramref name="coinLoses"/> overrides the default RNG-backed coin
    /// flip — return <c>true</c> to model "you lose the flip" (3 damage),
    /// <c>false</c> to model "you win the flip" (no damage).
    /// </summary>
    public static Artifact Create(
        Player owner,
        TriggerManager? triggers,
        Func<bool>? coinLoses = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // Default: System.Random.Shared 50/50. The factory accepts an
        // injectable seam so tests can force the loss/win branch.
        var flipLoses = coinLoses ?? (() => System.Random.Shared.Next(2) == 0);

        // ----------------------------------------------------------------
        // Upkeep trigger — CR 603.1, CR 500.4. "At the beginning of your
        // upkeep, flip a coin. If you lose the flip, Mana Crypt deals 3
        // damage to you." No intervening "if" — the flip happens
        // unconditionally each upkeep; only the damage clause is gated on
        // the flip result. The controller is sampled live at resolution so
        // a same-turn control-change effect targets the new controller
        // (matches Mana Vault's resolution-time controller lookup).
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            "Mana Crypt: at upkeep, flip a coin; on loss deal 3 damage to you",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;

                var controller = card.Controller ?? owner;
                if (flipLoses())
                {
                    controller.LoseLife(3);
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
        // {T}: Add {C}{C}.  ManaCost.Parse("CC") buckets two {C} into
        // Generic = 2 (CR 107.4c — engine collapses colourless to generic).
        // ----------------------------------------------------------------
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("CC")));

        return card;
    }
}
