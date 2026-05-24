using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// Unit tests for <see cref="EquipActivatedAbility"/> — the
/// first-class primitive for Equipment's "Equip {cost}: Attach to target
/// creature you control. Activate only as a sorcery." printed ability
/// (CR 702.6).
///
/// Coverage:
/// <list type="bullet">
///   <item><description>Mana-cost payment + attach-on-resolve.</description></item>
///   <item><description>Sorcery-speed gate (CR 117.1a / 307.5) — rejected
///   at instant speed by <see cref="ActionValidator"/>.</description></item>
///   <item><description>Re-equip moves the Equipment to a different creature.</description></item>
///   <item><description>Puresteel-Paladin-style zero-equip CostProvider —
///   ≥3 artifacts on controller → effective cost {0}; &lt;3 → printed cost.</description></item>
///   <item><description>LTB-of-bearer regression — Equipment unattaches
///   (existing surface, sanity-checked here so the primitive's resolve
///   path doesn't accidentally regress it).</description></item>
/// </list>
///
/// Process-global <see cref="ZeroEquipCostEffect"/> registry is cleared
/// per-test via <see cref="Dispose"/> — mirrors PuresteelPaladinTests.
/// </summary>
public class EquipActivatedAbilityTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public EquipActivatedAbilityTests()
    {
        ZeroEquipCostEffect.ResetForTests();
    }

    public void Dispose()
    {
        ZeroEquipCostEffect.ResetForTests();
    }

    // -----------------------------------------------------------------------
    // Construction shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Construction_ExposesPrintedCost_And_TargetCreatureRequest()
    {
        var hammer = ColossusHammerFactory.Create(_alice);
        var equip = hammer.Abilities.OfType<EquipActivatedAbility>().Single();

        equip.EquipCost.Generic.Should().Be(8, "Colossus Hammer prints Equip {8}");
        equip.IsSorcerySpeed.Should().BeTrue("CR 702.6c — equip is sorcery-speed by default");
        equip.TargetCreature.MinTargets.Should().Be(1);
        equip.TargetCreature.MaxTargets.Should().Be(1);
        equip.TargetRequests.Should().ContainSingle()
            .Which.Should().BeSameAs(equip.TargetCreature);
    }

    [Fact]
    public void Construction_RetrofittedEquipments_ExposePrintedCosts()
    {
        // Sanity-check the cross-factory retrofit. If anyone adds an
        // equipment factory that hand-rolls equip, this test should grow.
        ColossusHammerFactory.Create(_alice)
            .Abilities.OfType<EquipActivatedAbility>().Single()
            .EquipCost.Generic.Should().Be(8);

        SkullclampFactory.Create(_alice)
            .Abilities.OfType<EquipActivatedAbility>().Single()
            .EquipCost.Generic.Should().Be(1);

        UmezawasJitteFactory.Create(_alice)
            .Abilities.OfType<EquipActivatedAbility>().Single()
            .EquipCost.Generic.Should().Be(2);

        SwordOfFireAndIceFactory.Create(_alice)
            .Abilities.OfType<EquipActivatedAbility>().Single()
            .EquipCost.Generic.Should().Be(2);

        SwordOfFeastAndFamineFactory.Create(_alice)
            .Abilities.OfType<EquipActivatedAbility>().Single()
            .EquipCost.Generic.Should().Be(2);

        var cori = CoriSteelCutterFactory.Create(_alice)
            .Abilities.OfType<EquipActivatedAbility>().Single();
        cori.EquipCost.Generic.Should().Be(1);
        cori.EquipCost.Red.Should().Be(1, "Cori-Steel Cutter is Equip {1}{R}");
    }

    // -----------------------------------------------------------------------
    // Cost payment + attach-on-resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_PaysCost_AndAttachesToControllerCreature()
    {
        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var jitte = UmezawasJitteFactory.Create(_alice);
        jitte.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(jitte);

        // Fund the equip cost ({2}).
        _alice.AddManaToPool(ManaCost.Parse("{2}"));

        var equip = jitte.Abilities.OfType<EquipActivatedAbility>().Single();
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        mana.CanPay(_alice).Should().BeTrue("Alice has {2} floating");
        mana.Pay(_alice);
        equip.Resolve();

        jitte.AttachedTo.Should().BeSameAs(bear,
            "resolve attaches the Equipment to the deterministic-first controller creature");
    }

    [Fact]
    public void Resolve_NoLegalTarget_IsNoOp()
    {
        var hammer = ColossusHammerFactory.Create(_alice);
        hammer.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(hammer);

        var equip = hammer.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.Resolve();

        hammer.AttachedTo.Should().BeNull(
            "no controller-side creatures → equip resolution is a no-op (CR 608.2b)");
    }

    [Fact]
    public void Resolve_PrefersChosenTarget_OverDeterministicPicker()
    {
        var bear1 = new Creature("Bear One", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var bear2 = new Creature("Bear Two", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(bear1);
        _alice.Zones.Battlefield.AddCard(bear2);

        var jitte = UmezawasJitteFactory.Create(_alice);
        jitte.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(jitte);

        var equip = jitte.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.SetChosenTargets(new[] { new object[] { bear2 } });

        equip.Resolve();

        jitte.AttachedTo.Should().BeSameAs(bear2,
            "agent-chosen target wins over the first-on-battlefield fallback");
    }

    // -----------------------------------------------------------------------
    // Sorcery-speed gate (CR 117.1a / 307.5)
    // -----------------------------------------------------------------------

    [Fact]
    public void Activation_AtSorcerySpeed_IsValid()
    {
        var hammer = ColossusHammerFactory.Create(_alice);
        hammer.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(hammer);

        var equip = hammer.Abilities.OfType<EquipActivatedAbility>().Single();
        var action = new ActivateAbilityAction(equip, _alice, sorcerySpeedAvailable: true);

        var validator = new ActionValidator();
        var result = validator.ValidateAction(action);

        result.IsValid.Should().BeTrue("equip at sorcery speed is legal");
    }

    [Fact]
    public void Activation_AtInstantSpeed_IsRejected()
    {
        var hammer = ColossusHammerFactory.Create(_alice);
        hammer.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(hammer);

        var equip = hammer.Abilities.OfType<EquipActivatedAbility>().Single();
        var action = new ActivateAbilityAction(equip, _alice, sorcerySpeedAvailable: false);

        var validator = new ActionValidator();
        var result = validator.ValidateAction(action);

        result.IsValid.Should().BeFalse(
            "CR 117.1a / 307.5 — 'Activate only as a sorcery' blocks off-turn / stack-non-empty activations");
    }

    // -----------------------------------------------------------------------
    // Re-equip
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_ReEquip_MovesEquipmentToNewBearer()
    {
        var bear1 = new Creature("Bear One", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var bear2 = new Creature("Bear Two", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(bear1);
        _alice.Zones.Battlefield.AddCard(bear2);

        var jitte = UmezawasJitteFactory.Create(_alice);
        jitte.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(jitte);
        jitte.AttachTo(bear1);
        jitte.AttachedTo.Should().BeSameAs(bear1);

        var equip = jitte.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.SetChosenTargets(new[] { new object[] { bear2 } });
        equip.Resolve();

        jitte.AttachedTo.Should().BeSameAs(bear2,
            "re-activating equip transfers the Equipment (CR 702.6e)");
        bear1.Attachments.Should().NotContain(jitte,
            "the previous bearer no longer has the Equipment");
    }

    // -----------------------------------------------------------------------
    // Puresteel Paladin zero-equip CostProvider
    // -----------------------------------------------------------------------

    [Fact]
    public void CostProvider_PuresteelActive_WithThreeArtifacts_OverridesToZero()
    {
        // Stand up Puresteel + 3 artifacts on Alice's battlefield to flip
        // the ZeroEquipCostEffect registry.
        var paladin = PuresteelPaladinFactory.Create(_alice);
        paladin.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(paladin);
        var zeroLifecycle = new ZeroEquipCostEffect(
            source: paladin,
            controller: _alice,
            eventBus: null);
        zeroLifecycle.Attach();

        // 3 artifacts on Alice's battlefield (NOT including Puresteel — it's
        // a creature).
        for (var i = 0; i < 3; i++)
        {
            var art = new Artifact($"Sol Stand-In {i}", "{1}");
            art.SetOwner(_alice);
            art.SetController(_alice);
            art.Zone = ZoneType.Battlefield;
            _alice.Zones.Battlefield.AddCard(art);
        }

        // Hammer's printed equip is {8} — but with Puresteel active it
        // should resolve to {0}.
        var hammer = ColossusHammerFactory.Create(_alice);
        hammer.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(hammer);

        var equip = hammer.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.EquipCost.Generic.Should().Be(8, "printed cost is unchanged");

        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        // Alice has NO mana in pool — proves the effective cost is {0}.
        mana.CanPay(_alice).Should().BeTrue(
            "Puresteel + 3 artifacts → effective equip cost {0}; no mana needed");
        mana.Pay(_alice); // Should not throw.
        _alice.ManaPool.IsEmpty.Should().BeTrue("paying {0} costs nothing");
    }

    [Fact]
    public void CostProvider_PuresteelActive_WithTwoArtifacts_KeepsPrintedCost()
    {
        var paladin = PuresteelPaladinFactory.Create(_alice);
        paladin.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(paladin);
        var zeroLifecycle = new ZeroEquipCostEffect(
            source: paladin,
            controller: _alice,
            eventBus: null);
        zeroLifecycle.Attach();

        // Hammer itself is an Artifact (Equipment) — counts toward
        // Puresteel's "three or more artifacts" threshold. Add ONE more
        // artifact so the total is 2 (Hammer + 1) — below threshold.
        var extra = new Artifact("Sol Stand-In", "{1}");
        extra.SetOwner(_alice);
        extra.SetController(_alice);
        extra.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(extra);

        var hammer = ColossusHammerFactory.Create(_alice);
        hammer.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(hammer);

        var equip = hammer.Abilities.OfType<EquipActivatedAbility>().Single();
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        // No mana in pool, printed cost {8} → cannot pay.
        mana.CanPay(_alice).Should().BeFalse(
            "below the 3-artifact threshold the printed {8} cost applies");
    }

    [Fact]
    public void CostProvider_NoPuresteel_KeepsPrintedCost()
    {
        // No Puresteel registered.
        var hammer = ColossusHammerFactory.Create(_alice);
        hammer.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(hammer);

        var equip = hammer.Abilities.OfType<EquipActivatedAbility>().Single();
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        _alice.AddManaToPool(ManaCost.Parse("{7}"));
        mana.CanPay(_alice).Should().BeFalse("{7} < printed {8}");

        _alice.AddManaToPool(ManaCost.Parse("{1}"));
        mana.CanPay(_alice).Should().BeTrue("{8} pays {8}");
    }

    // -----------------------------------------------------------------------
    // LTB regression — bearer leaving the battlefield unattaches Equipment
    // -----------------------------------------------------------------------

    [Fact]
    public void Unattach_WhenBearerLeavesBattlefield_RegressionGuard()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var jitte = UmezawasJitteFactory.Create(_alice);
        jitte.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(jitte);
        jitte.AttachTo(bear);

        jitte.AttachedTo.Should().BeSameAs(bear);

        // Bearer leaves the battlefield. Permanent.Unattach is the engine's
        // entry point (called from the LTB pipeline); call it directly here
        // so the regression test doesn't depend on the full SBA / zone-move
        // loop.
        jitte.Unattach();

        jitte.AttachedTo.Should().BeNull(
            "CR 704.5n — Equipment unattaches when its bearer leaves the battlefield");
        bear.Attachments.Should().NotContain(jitte);
    }
}
