using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sulfuric Vortex (Scourge, {1}{R}{R}).
///
/// Enchantment. Oracle text:
///   "At the beginning of each player's upkeep, Sulfuric Vortex deals 2
///    damage to that player.
///    If a player would gain life, that player gains no life instead."
///
/// ## Implemented (v1)
///
/// - Enchantment {1}{R}{R}, owner/controller wired.
/// - <b>Upkeep ping triggered ability (CR 603.1 / CR 500.4)</b>: fires
///   on <see cref="StepStartedEvent"/> matching <see cref="PhaseStateType.Upkeep"/>
///   for ANY player (the printed text reads "each player's upkeep", not
///   "your upkeep" — different from Roiling Vortex's controller-only
///   upkeep). The damage routes to the active player whose upkeep is
///   firing (read from <see cref="StepStartedEvent.Player"/>). The
///   damage is delivered via <see cref="Player.LoseLife"/> — same
///   non-combat-damage shape Roiling Vortex / Manabarbs ships with;
///   damage prevention / replacement subscribers don't observe the
///   ping. See "Deferred" below.
/// - <b>"Players can't gain life" static (CR 614 / CR 119.6)</b>: when a
///   <see cref="ReplacementBus"/> is supplied, register a
///   <see cref="LifeGainIntent"/> replacement that rewrites every gain
///   to a zero-amount intent (CR 614.1). The intent dispatcher in
///   <see cref="Player.GainLife"/> passes the request through the
///   player's attached bus before mutating the life total. Identical
///   wiring shape to <see cref="RoilingVortexFactory"/>'s "Players can't
///   gain life" static. Without a bus the static silently no-ops
///   (matches Roiling Vortex / Valakut's single-arg posture).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. Upkeep trigger attached
///   for shape observability; no <see cref="TriggerManager"/> +
///   <see cref="ReplacementBus"/> registrations. Suitable for
///   dispatcher / shape tests.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?)"/> —
///   fully wired. <paramref name="triggers"/> picks the upkeep ping off
///   the bus; <paramref name="replacements"/> registers the life-gain
///   blocker.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Full <see cref="Majik.Core.Events.DamageDealtEvent"/> route</b>:
///   the upkeep ping mutates state through <see cref="Player.LoseLife"/>
///   directly, bypassing damage-prevention / replacement subscribers
///   (Aegis of the Heavens, Story Circle, etc.). Same gap as Roiling
///   Vortex / Manabarbs / Dark Confidant — a future engine pass that
///   unifies non-combat damage through the bus picks Sulfuric Vortex up
///   for free.
/// </summary>
[CardName("Sulfuric Vortex")]
public static class SulfuricVortexFactory
{
    public const string CardName = "Sulfuric Vortex";
    public const string PrintedManaCost = "{1}{R}{R}";
    public const int UpkeepDamage = 2;

    /// <summary>
    /// Construct Sulfuric Vortex with no live runtime services. Both
    /// abilities are attached for shape observability; nothing is
    /// registered on a trigger manager or replacement bus.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Sulfuric Vortex with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager — when supplied, the
    /// each-player's-upkeep ping registers so the bus drives it
    /// automatically.</param>
    /// <param name="replacements">Replacement bus — when supplied, the
    /// "players can't gain life" static registers as a
    /// <see cref="LifeGainIntent"/> replacement (CR 614 / 119.6) that
    /// rewrites every gain to zero. Without a bus the static silently
    /// no-ops.</param>
    public static Enchantment Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Upkeep ping — CR 603.1 / CR 500.4.
        //   "At the beginning of each player's upkeep, Sulfuric Vortex
        //    deals 2 damage to that player."
        // Unlike Roiling Vortex this is symmetric AND fires on every
        // player's upkeep (active player whose upkeep is starting). We
        // capture the active player on the StepStartedEvent and the
        // resolved effect drains 2 life from them. Damage routes through
        // Player.LoseLife — same v1 non-combat-damage shape as Roiling
        // Vortex / Manabarbs.
        // ----------------------------------------------------------------
        Player? upkeepPlayer = null;

        var upkeepCondition = new EventTriggerCondition<StepStartedEvent>((e, _) =>
        {
            if (e.StepType != PhaseStateType.Upkeep) return false;
            upkeepPlayer = e.Player;
            return true;
        });

        var upkeepEffect = new Effect(
            $"{CardName}: deal {UpkeepDamage} damage to the active player at upkeep",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var target = upkeepPlayer;
                upkeepPlayer = null;
                if (target == null) return;
                if (target.HasLost) return;
                target.LoseLife(UpkeepDamage);
            });

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: upkeepCondition,
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        // ----------------------------------------------------------------
        // "Players can't gain life" static — CR 614 / CR 119.6.
        //   Register a LifeGainIntent replacement that rewrites every
        //   gain to a zero-amount intent. Same shape as Roiling Vortex.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new LambdaReplacement<LifeGainIntent>(
                applies: (_, _) => true,
                replace: (intent, _) => intent with { Amount = 0 },
                oneShot: false,
                tag: card));
        }

        return card;
    }
}
