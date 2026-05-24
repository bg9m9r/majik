using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Guide of Souls (Modern Horizons 3,
/// Creature — Spirit Cleric {W} 1/2). Anchor of the Modern Boros
/// Energy / Boros Convoke "small creatures + energy" axis.
///
/// Oracle text:
///   "Whenever Guide of Souls or another creature you control with
///    power 2 or less enters, you get {E} (an energy counter).
///    Pay {E}{E}: Target creature gains flying and gets +1/+1 until
///    end of turn."
///
/// ## Implemented (v1)
/// - 1/2 Creature — Spirit Cleric, mana cost {W}, owner / controller
///   wired.
/// - <b>Energy ETB trigger</b> (CR 603.6a + CR 106.13): a triggered
///   ability over <see cref="CardMovedEvent"/> Anywhere → Battlefield
///   filtered to (a) Card is a Creature, (b) Controller equals Guide's
///   controller, and (c) the entering card's <see cref="Creature.BasePower"/>
///   is ≤ 2. Guide of Souls itself is NOT excluded — the printed
///   "or another" disjunction means Guide's own ETB also triggers the
///   ability when its base power is 1, which it is. Power is read off
///   <see cref="Creature.BasePower"/> at trigger evaluation time
///   (printed P/T; CR 208.2) — using effective power would require a
///   live <see cref="ContinuousEffectsService"/> + a "what is power
///   right now" snapshot which the factory's structural attach path
///   doesn't carry, and printed P/T is the safer reading for v1 (matches
///   Champion of the Parish's printed-type predicate posture). On
///   resolution: <see cref="Player.GainEnergy"/> increments the
///   controller's player-scoped energy ledger by 1 (CR 106.13b — energy
///   is a player resource the {E} pip pays out of).
/// - <b>{E}{E} activated pump</b> (CR 602): a single
///   <see cref="ActivatedAbility"/> with one cost (the new
///   <see cref="PayEnergyCost"/> for 2 energy — sibling of
///   <see cref="RemoveVoidCounterCost"/> and
///   <see cref="SacrificeAClueCost"/>; private to this factory
///   because Aether Hub's mana-ability path uses
///   <see cref="ManaAbility"/>'s <c>additionalCostPayer</c> hook
///   rather than an <see cref="ICost"/>) and a single 1..1
///   "target creature" <see cref="TargetRequest"/>. On resolution the
///   effect reads <see cref="ActivatedAbility.ChosenTargets"/>,
///   validates the chosen Creature is still on the battlefield
///   (CR 608.2b), and registers two end-of-turn-scoped continuous
///   effects against the target's <see cref="Creature.ActiveEffects"/>:
///   a <see cref="GrantKeywordUntilEndOfTurnEffect"/> for Flying
///   (CR 613.1c Layer 6) and a <see cref="PumpUntilEndOfTurnEffect"/>
///   for +1/+1 (Layer 7c). Same EOT-scoped shape used by
///   <see cref="TemurBattleRageFactory"/>'s double-strike +
///   trample grants and <see cref="EarthshakerKhenraFactory"/>'s
///   CannotBlock rider.
/// - <b>Single-arg dispatcher path</b> attaches the ETB trigger
///   structurally (no <see cref="TriggerManager"/> registration, same
///   posture as <see cref="AetherHubFactory.Create(Player)"/>) and the
///   activated ability. The energy cost gate is always live — energy
///   is on <see cref="Player"/> (player-scoped resource) so the
///   activation check works cleanly without external runtime services;
///   when the target's <see cref="Creature.ActiveEffects"/> is null
///   (shape-only tests) the EOT grants silently no-op (same posture as
///   <see cref="EarthshakerKhenraFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Live TriggerManager wiring</b>: the single-arg factory attaches
///   the ETB trigger to the card but does NOT register it with a
///   <see cref="TriggerManager"/>. Tests fire the trigger by invoking
///   the effect directly. A 2-arg
///   <c>Create(owner, eventBus, triggers)</c> overload (mirroring
///   <see cref="AetherHubFactory.Create(Player, IEventBus?, TriggerManager?)"/>)
///   is not shipped here because the dispatcher path is the production
///   single-card construction site; bus-driven firing follows the same
///   pattern when needed.
/// - <b>Effective vs printed power</b>: v1 reads
///   <see cref="Creature.BasePower"/> — a creature pumped to 3/3 by
///   another effect that printed as 1/1 STILL triggers Guide's energy
///   (printed is ≤ 2). Promoting to effective power would require
///   threading a <see cref="ContinuousEffectsService"/> through the
///   factory; same posture as Champion of the Parish's printed-subtype
///   predicate (a creature animated to Human via an effect doesn't
///   currently trigger Champion either).
/// - <b>Agent-driven target prompt</b>: <see cref="ActivatedAbility"/>
///   honours pre-set <see cref="ActivatedAbility.ChosenTargets"/>; the
///   factory does not wire an <see cref="IPlayerAgent"/> prompt. Tests
///   call <see cref="ActivatedAbility.SetChosenTargets"/> directly
///   (same posture as Earthshaker Khenra's ETB target).
/// </summary>
[CardName("Guide of Souls")]
public static class GuideOfSoulsFactory
{
    public const string CardName = "Guide of Souls";
    public const string PrintedManaCost = "{W}";
    public const int Power = 1;
    public const int Toughness = 2;
    public const int EnergyEtbPowerThreshold = 2;
    public const int PumpEnergyCost = 2;

    /// <summary>
    /// Construct Guide of Souls. The ETB triggered ability for
    /// "you OR another creature you control with power 2 or less"
    /// is attached to the card shape (no TriggerManager wiring —
    /// tests fire the effect directly). The {E}{E} pump activated
    /// ability is attached with a 1..1 "target creature"
    /// <see cref="TargetRequest"/>; on resolution the target gains
    /// Flying + +1/+1 EOT via two
    /// <see cref="ContinuousEffect"/>s registered against the
    /// target's <see cref="Creature.ActiveEffects"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spirit, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a + CR 106.13.
        //   "Whenever Guide of Souls OR another creature you control
        //    with power 2 or less enters, you get {E}."
        //
        // Includes Guide itself (printed 1/2 — base power 1 ≤ 2).
        // Predicate gates on:
        //   - ToZone == Battlefield (entering)
        //   - Card.HasType(Creature)
        //   - Card.Controller == Guide's controller
        //   - Card is a Creature with BasePower ≤ 2 (printed P/T;
        //     CR 208.2 — same posture as Champion of the Parish's
        //     printed-subtype predicate)
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) =>
            {
                if (e.ToZone != ZoneType.Battlefield) return false;
                if (!e.Card.HasType(CardType.Creature)) return false;
                if (!ReferenceEquals(e.Card.Controller, card.Controller)) return false;
                if (e.Card is not Creature entering) return false;
                return entering.BasePower <= EnergyEtbPowerThreshold;
            });

        var etbEffect = new Effect(
            "Guide of Souls — controller gains {E} (an energy counter)",
            () =>
            {
                var controller = card.Controller ?? owner;
                controller.GainEnergy(1);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Activated ability — CR 602.
        //   "Pay {E}{E}: Target creature gains flying and gets +1/+1
        //    until end of turn."
        //
        // Cost: PayEnergyCost(2) — sibling of RemoveVoidCounterCost /
        // SacrificeAClueCost. Resolution-side body reads the chosen
        // target, validates it's still on the battlefield (CR 608.2b),
        // and registers two EOT-scoped continuous effects against the
        // target's ActiveEffects — Flying (Layer 6) + +1/+1 (Layer 7c).
        // ----------------------------------------------------------------
        ActivatedAbility? pumpAbility = null;
        var pumpEffect = new Effect(
            "Guide of Souls — target creature gains Flying and +1/+1 until end of turn",
            () =>
            {
                if (pumpAbility == null) return;
                var chosen = pumpAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return; // CR 608.2b
                if (target.ActiveEffects == null) return; // shape-only no-op

                // CR 613.1c Layer 6 — keyword grant (Flying).
                target.ActiveEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(target, "Flying"));

                // CR 613.7c Layer 7c — +P/+T modification (+1/+1).
                target.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(target, 1, 1));
            });

        pumpAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new PayEnergyCost(PumpEnergyCost) },
            effects: new IEffect[] { pumpEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        card.AddAbility(pumpAbility);

        return card;
    }
}

/// <summary>
/// Activation cost that spends N energy from the activating player's
/// player-scoped <see cref="Player.EnergyCounters"/> ledger (CR 106.13).
/// Sibling of <see cref="RemoveVoidCounterCost"/> /
/// <see cref="SacrificeAClueCost"/> — kept private-to-Guide for now
/// because Aether Hub's mana ability uses
/// <see cref="ManaAbility"/>'s inline <c>additionalCostPayer</c> rather
/// than an <see cref="ICost"/>; once a second activated-ability energy
/// cost ships, this can move up to <c>Majik.Core/Costs/</c>.
/// </summary>
public sealed class PayEnergyCost : ICost
{
    private readonly int _amount;

    public PayEnergyCost(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Energy amount cannot be negative.");
        }
        _amount = amount;
    }

    public int Amount => _amount;

    public string Description => $"Pay {string.Concat(Enumerable.Repeat("{E}", _amount))}";

    public bool CanPay(Player player)
    {
        if (player == null) return false;
        // CR 119.4 — players can't pay a resource they don't have.
        return player.EnergyCounters >= _amount;
    }

    public void Pay(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!player.PayEnergy(_amount))
        {
            throw new InvalidOperationException(
                $"Cannot pay {_amount} energy: controller has {player.EnergyCounters}.");
        }
    }
}
