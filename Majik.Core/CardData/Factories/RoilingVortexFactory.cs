using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Roiling Vortex (Zendikar Rising, {R}).
///
/// Enchantment. Oracle text:
///   "At the beginning of your upkeep, Roiling Vortex deals 1 damage to
///    each player.
///    Whenever a player casts a spell, if no mana was spent to cast it,
///    Roiling Vortex deals 3 damage to that player.
///    {1}{R}, Sacrifice Roiling Vortex: Roiling Vortex deals 3 damage to
///    any target.
///    Players can't gain life."
///
/// ## Implemented (v1)
/// - <b>Enchantment {R}</b> — vanilla card shape, owner / controller wired.
/// - <b>Upkeep "deals 1 to each player" trigger (CR 603.1 / CR 500.4)</b>
///   via <see cref="Triggers.OnStepBegin"/> filtered to (Upkeep,
///   controller). Resolution iterates the optional
///   <paramref name="allPlayersResolver"/> and drains 1 life from each;
///   without a resolver the controller-only path mirrors
///   <see cref="TheMeathookMassacreFactory"/>'s convention. Same v1
///   non-combat-damage shape as Manabarbs / Dark Confidant — damage routes
///   through <see cref="Player.LoseLife"/> rather than a full
///   <see cref="Events.DamageDealtEvent"/>.
/// - <b>Free-cast trigger (CR 603.1)</b> over
///   <see cref="SpellCastEvent"/> gated on
///   <see cref="Majik.Core.Spells.Spell.WasFreeCast"/>. The flag is
///   stamped by <see cref="Majik.Core.Game.SpellCastFlow"/> when the
///   collapsed total cost (printed + alt-cost + cost reductions + +X +
///   Delve) is <see cref="ManaCost.IsZero"/> — captures Cascade,
///   Suspend, Memnite-style {0} casts, and free alt-cost spells (Force
///   of Will / Misdirection-style pitch costs that resolve to zero mana
///   paid). Resolution drains 3 life from the casting player.
///   Hand-built test spells without an explicit stamp default to
///   <c>WasFreeCast=false</c> and never fire this trigger.
/// - <b>{1}{R}, Sacrifice Roiling Vortex: deal 3 to any target
///   (CR 602)</b> — <see cref="ActivatedAbility"/> with
///   <see cref="ManaCostCost"/> + <see cref="AdditionalCost.Sacrifice"/>;
///   resolution reads the first chosen target via
///   <see cref="OracleSpellBinder.DealDamage"/> (Player /
///   Creature target shapes supported). The sacrifice payment in the
///   current <see cref="AdditionalCost"/> implementation is a no-op stub
///   (zone move deferred to a future zone-service refactor — same gap
///   noted on <see cref="RelicOfProgenitusFactory"/>) so the activated
///   ability does NOT move Vortex to its owner's graveyard in v1.
/// - <b>"Players can't gain life" static (CR 614 / CR 119.6)</b> via a
///   new <see cref="LifeGainIntent"/> + bus-registered replacement that
///   rewrites every gain through the supplied
///   <see cref="ReplacementBus"/> to a zero-gain. <see cref="Player.GainLife"/>
///   itself routes through the bus when an active bus is attached to the
///   player (additive lifecycle — see
///   <see cref="Player.AttachReplacementBus"/>). The single-arg
///   <see cref="Create(Player)"/> overload omits all service wiring
///   (mirrors Valakut's posture).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Both triggers and
///   the activated ability are attached for shape observability; nothing
///   is registered with a <see cref="TriggerManager"/> or
///   <see cref="ReplacementBus"/>. Suitable for dispatcher / structural
///   tests.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?, Func{IReadOnlyList{Player}}?)"/>
///   — fully wired. <paramref name="triggers"/> registers both
///   triggered abilities; <paramref name="replacements"/> registers the
///   life-gain blocker; <paramref name="allPlayersResolver"/> widens the
///   upkeep ping to every player at the table.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side-effect</b> — <see cref="AdditionalCost.Sacrifice"/>
///   is a no-op today; activating Vortex does not actually graveyard the
///   card. Same gap as Relic of Progenitus / Nihil Spellbomb.
/// - <b>"Any target" agent prompt</b> — v1 honours pre-supplied
///   <see cref="ActivatedAbility.SetChosenTargets"/>; absent a target
///   the damage no-ops (CR 608.2b).
/// - <b>Full <see cref="DamageDealtEvent"/> route</b> — upkeep + free-cast
///   damage both go through <see cref="Player.LoseLife"/>; damage
///   prevention / replacement subscribers won't observe Vortex's pings.
///   Same shape as Manabarbs / Dark Confidant.
/// </summary>
[CardName("Roiling Vortex")]
public static class RoilingVortexFactory
{
    public const string CardName = "Roiling Vortex";
    public const string PrintedManaCost = "{R}";

    /// <summary>
    /// Construct Roiling Vortex with no live runtime services. All three
    /// abilities are attached to the card shape for structural
    /// observability; none are registered with a
    /// <see cref="TriggerManager"/> or <see cref="ReplacementBus"/>.
    /// Suitable for dispatcher / shape tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null, replacements: null, allPlayersResolver: null);

    /// <summary>
    /// Construct Roiling Vortex with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager — when supplied both the
    /// upkeep ping and the free-cast 3-damage trigger are registered so
    /// the bus drives them automatically.</param>
    /// <param name="replacements">Replacement bus — when supplied the
    /// "players can't gain life" static is registered as a
    /// <see cref="LifeGainIntent"/> replacement that rewrites every gain
    /// to zero (CR 614 / 615). Without a bus the static silently
    /// no-ops, matching the Valakut single-arg posture.</param>
    /// <param name="allPlayersResolver">Optional resolver supplying the
    /// full table for the upkeep "deals 1 to each player" effect.
    /// Without one the ping drains the controller only (defensive
    /// fallback — same convention as Pernicious Deed / Meathook
    /// Massacre).</param>
    public static Enchantment Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Upkeep ping — CR 603.1 / CR 500.4.
        //   "At the beginning of your upkeep, Roiling Vortex deals 1
        //    damage to each player."
        // Symmetric — controller takes 1 too (oracle: "each player").
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            $"{CardName}: at upkeep, deal 1 damage to each player",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;

                var players = allPlayersResolver?.Invoke()
                    ?? (IReadOnlyList<Player>)new[] { owner };

                foreach (var p in players)
                {
                    if (p.HasLost) continue;
                    p.LoseLife(1);
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
        // Free-cast ping — CR 603.1.
        //   "Whenever a player casts a spell, if no mana was spent to
        //    cast it, Roiling Vortex deals 3 damage to that player."
        // Predicate samples the WasFreeCast sentinel stamped on the spell
        // by SpellCastFlow when the collapsed total cost is ManaCost.Zero
        // (printed + alt-cost + cost-reduction + +X + Delve). The casting
        // player is captured at event time and read by the resolution
        // effect (same closure-pendingX pattern as Manabarbs).
        // ----------------------------------------------------------------
        Player? freeCastPlayer = null;

        var freeCastCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (e.Spell is not Majik.Core.Spells.Spell s) return false;
            if (!s.WasFreeCast) return false;
            freeCastPlayer = s.Controller;
            return true;
        });

        var freeCastEffect = new Effect(
            $"{CardName}: deal 3 damage to the player who cast a free spell",
            () =>
            {
                var target = freeCastPlayer;
                freeCastPlayer = null;
                if (target == null) return;
                if (card.Zone != ZoneType.Battlefield) return;
                if (target.HasLost) return;
                target.LoseLife(3);
            });

        var freeCastTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: freeCastCondition,
            effects: new IEffect[] { freeCastEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(freeCastTrigger);
        triggers?.RegisterTriggeredAbility(freeCastTrigger);

        // ----------------------------------------------------------------
        // Activated ability — CR 602.
        //   "{1}{R}, Sacrifice Roiling Vortex: Roiling Vortex deals 3
        //    damage to any target."
        // Mana cost + AdditionalCost.Sacrifice on self. The sacrifice
        // payment is a no-op stub in v1 (same gap as Relic of
        // Progenitus / Nihil Spellbomb), so the activated ability does
        // not actually graveyard Vortex; future zone-service refactor
        // unifies it. Target reads from ChosenTargets[0][0] —
        // OracleSpellBinder.DealDamage dispatches on Player vs Creature.
        // ----------------------------------------------------------------
        ActivatedAbility? sacAbility = null;

        var sacEffect = new Effect(
            $"{CardName}: deal 3 damage to any target",
            () =>
            {
                if (sacAbility == null) return;
                if (sacAbility.ChosenTargets.Count == 0) return;
                if (sacAbility.ChosenTargets[0].Count == 0) return;

                var target = sacAbility.ChosenTargets[0][0];
                OracleSpellBinder.DealDamage(target, 3);
            });

        sacAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}{R}"),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { sacEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(sacAbility);

        // ----------------------------------------------------------------
        // "Players can't gain life" — CR 614 / CR 119.6.
        //   Register a LifeGainIntent replacement that rewrites every
        //   gain to a zero-amount intent. The intent dispatcher in
        //   Player.GainLife passes the request through the player's
        //   attached bus before mutating the life total. Without a bus
        //   the static silently no-ops (single-arg dispatcher posture —
        //   mirrors Valakut's ETB-tapped replacement).
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
