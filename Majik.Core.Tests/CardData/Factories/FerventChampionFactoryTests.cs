using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FerventChampionFactory"/> (Throne of Eldraine, {R}).
///
/// Fervent Champion — Creature — Human Knight 1/1:
///   "First strike, haste"
///   "Whenever this creature attacks, another target attacking Knight you
///    control gets +1/+0 until end of turn."
///   "Equip abilities you activate that target this creature cost {3} less
///    to activate."
///
/// Covers:
/// - Identity: {R} 1/1 red Human Knight, mana value 1, dispatch.
/// - First strike + Haste keyword markers.
/// - Attack trigger: on attack, the chosen OTHER attacking Knight you control
///   gets +1/+0 EOT; non-Knights / the Champion itself are not legal.
/// - Equip-cost reduction: an Equip ability targeting Fervent Champion costs
///   {3} less (the deferral being paid down).
/// </summary>
[Trait("Color", "R")]
public class FerventChampionFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FerventChampionFactoryTests()
    {
        EquipCostReductionEffect.ResetForTests();
        ZeroEquipCostEffect.ResetForTests();
    }

    public void Dispose()
    {
        EquipCostReductionEffect.ResetForTests();
        ZeroEquipCostEffect.ResetForTests();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FerventChampion_IsRedHumanKnight_1_1_ManaValue1()
    {
        var card = FerventChampionFactory.Create(_alice);

        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Name.Should().Be("Fervent Champion");
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.ManaCostValue.TotalValue.Should().Be(1, "{R} is mana value 1");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FerventChampion()
    {
        var card = Majik.Core.CardData.NamedCardFactory.Create("Fervent Champion", _alice);
        card.Should().NotBeNull();
        card!.Name.Should().Be("Fervent Champion");
        card.Should().BeOfType<Creature>();
    }

    [Fact]
    public void Create_AttachesFirstStrike_AndHaste()
    {
        var card = FerventChampionFactory.Create(_alice);

        var keywords = card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();
        keywords.Should().Contain("First strike");
        keywords.Should().Contain("Haste");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — another target attacking Knight you control +1/+0 EOT
    // -----------------------------------------------------------------------

    [Fact]
    public void OnAttack_PumpsChosenAttackingKnight_NotItself()
    {
        var eventBus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(eventBus);
        var triggers = new TriggerManager(stack, eventBus);
        var combat = new CombatManager(eventBus);
        var effects = new ContinuousEffectsService();

        var champ = FerventChampionFactory.Create(
            _alice,
            eventBus: eventBus,
            triggers: triggers,
            attackingCreaturesSource: () => combat.CurrentCombat?.Attackers
                .Select(a => a.Creature).ToList() ?? new List<Creature>());
        champ.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(champ);
        champ.SetZone(ZoneType.Battlefield);
        champ.ClearSummoningSickness();

        // Another attacking Knight you control — a legal pump target.
        var allyKnight = new Creature("Knight Ally", "{1}{W}", 2, 2,
            subtypes: new[] { CardSubtype.Knight });
        allyKnight.SetOwner(_alice);
        allyKnight.SetController(_alice);
        allyKnight.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(allyKnight);
        allyKnight.SetZone(ZoneType.Battlefield);
        allyKnight.ClearSummoningSickness();

        combat.StartCombat(_alice);
        combat.DeclareAttackers(_alice, new[]
        {
            new AttackerDeclaration(champ, targetPlayer: _bob),
            new AttackerDeclaration(allyKnight, targetPlayer: _bob),
        });

        // The trigger is targeted — choose the ally Knight.
        var attackTrigger = champ.Abilities.OfType<TriggeredAbility>().Single();
        attackTrigger.SetChosenTargets(new[] { new object[] { allyKnight } });

        foreach (var eff in attackTrigger.Effects) eff.Execute();

        allyKnight.GetPower().Should().Be(3, "the chosen attacking Knight gets +1/+0");
        allyKnight.GetToughness().Should().Be(2, "the pump is +1/+0 — toughness unchanged");
        champ.GetPower().Should().Be(1, "Fervent Champion is not pumped by its own 'another target' trigger");
    }

    // -----------------------------------------------------------------------
    // Equip-cost reduction — the deferral being paid down
    // -----------------------------------------------------------------------

    [Fact]
    public void EquipTargetingFerventChampion_CostsThreeLess()
    {
        var champ = FerventChampionFactory.Create(_alice);
        champ.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(champ);

        // Colossus Hammer prints Equip {8}; targeting Fervent Champion → {5}.
        var hammer = ColossusHammerFactory.Create(_alice);
        hammer.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(hammer);

        var equip = hammer.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.SetChosenTargets(new[] { new object[] { champ } });

        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        _alice.AddManaToPool(ManaCost.Parse("{4}"));
        mana.CanPay(_alice).Should().BeFalse("{4} < reduced {5}");
        _alice.AddManaToPool(ManaCost.Parse("{1}"));
        mana.CanPay(_alice).Should().BeTrue("Equip {8} reduced by {3} = {5}");
    }

    [Fact]
    public void EquipTargetingFerventChampion_OnlyAppliesWhileOnBattlefield()
    {
        var eventBus = new EventBus();
        var champ = FerventChampionFactory.Create(_alice, eventBus, triggers: null,
            attackingCreaturesSource: null);

        // Not on the battlefield yet → reducer inactive.
        EquipCostReductionEffect.ReductionForTarget(champ).Should().Be(0,
            "the equip-cost reducer is gated on Fervent Champion being on the battlefield");

        // Move it onto the battlefield via the zone pipeline so the lifecycle
        // binder registers.
        champ.Zone = ZoneType.Battlefield;
        eventBus.Publish(new CardMovedEvent(champ, ZoneType.Hand, ZoneType.Battlefield));
        EquipCostReductionEffect.ReductionForTarget(champ).Should().Be(3,
            "on the battlefield the {3}-less reduction is active for Fervent Champion as the equip target");
    }
}
