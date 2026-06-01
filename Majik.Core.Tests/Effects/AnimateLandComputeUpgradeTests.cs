using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 613.1c / 613.7b — the Compute creature-row upgrade. When a Layer-4
/// effect grants <see cref="CardType.Creature"/> to a NON-creature permanent
/// (a Land/Artifact runtime instance), <see cref="ContinuousEffectsService.Compute(Permanent)"/>
/// must upgrade the working set to a <see cref="CreatureCharacteristics"/> so
/// the animated form's P/T surfaces (manlands + Earthbend). Without other
/// effects a Land seeds a plain <see cref="PermanentCharacteristics"/>.
/// </summary>
public class AnimateLandComputeUpgradeTests
{
    private readonly Player _alice = new("Alice", 20);

    private Land MakeLand(ContinuousEffectsService svc)
    {
        var land = new Land("Mutavault")
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(land);
        return land;
    }

    [Fact]
    public void NonAnimatedLand_SeedsPlainPermanentRow_NoPT()
    {
        var svc = new ContinuousEffectsService();
        var land = MakeLand(svc);

        var chars = svc.Compute(land);

        chars.Should().NotBeOfType<CreatureCharacteristics>(
            "a land with no creature-grant has no P/T row");
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
    }

    [Fact]
    public void AnimatedLand_ComputeUpgradesToCreatureRow_WithSetBasePT()
    {
        var svc = new ContinuousEffectsService();
        var land = MakeLand(svc);

        // Manland: 2/2 Elemental creature, still a land.
        AnimateLandEffect.Register(svc, land, CardSubtype.Elemental, 2, 2, grantsHaste: true);

        var chars = svc.Compute(land);

        chars.Should().BeOfType<CreatureCharacteristics>(
            "the Layer-4 Creature grant upgrades the row (CR 613.1c)");
        chars.Types.Should().Contain(CardType.Creature);
        chars.Types.Should().Contain(CardType.Land, "still a land");
        chars.Subtypes.Should().Contain(CardSubtype.Elemental);
        chars.Keywords.Should().Contain("Haste");

        var cc = (CreatureCharacteristics)chars;
        cc.Power.Should().Be(2, "Layer-7b set-base lands on the upgraded creature row");
        cc.Toughness.Should().Be(2);
    }

    /// <summary>
    /// +1/+1 counters layer on top of the animated base (Layer-7c postlude)
    /// — the Earthbend-N → N/N math, generalised to any manland.
    /// </summary>
    [Fact]
    public void AnimatedLand_CountersLayerOnTopOfSetBase()
    {
        var svc = new ContinuousEffectsService();
        var land = MakeLand(svc);

        AnimateLandEffect.Register(svc, land, CardSubtype.Elemental, 0, 0, grantsHaste: true);
        land.Counters.Add(CounterType.PlusOnePlusOne, 2);

        var cc = (CreatureCharacteristics)svc.Compute(land);
        cc.Power.Should().Be(2, "0/0 base + two +1/+1 counters");
        cc.Toughness.Should().Be(2);
    }

    /// <summary>
    /// An anthem-style Layer-7c pump stacks on top of the animated base P/T —
    /// proves the upgraded creature row participates fully in the P/T layers
    /// like any printed creature would (helps the Creeping Tar Pit class).
    /// </summary>
    [Fact]
    public void AnimatedLand_AnthemPumpStacksOnAnimatedBase()
    {
        var svc = new ContinuousEffectsService();
        var land = MakeLand(svc);

        AnimateLandEffect.Register(svc, land, CardSubtype.Elemental, 3, 2, grantsHaste: false);
        svc.Register(new TestPumpL7c(land, 1, 1)); // +1/+1 anthem

        var cc = (CreatureCharacteristics)svc.Compute(land);
        cc.Power.Should().Be(4, "3 base + 1 anthem");
        cc.Toughness.Should().Be(3, "2 base + 1 anthem");
    }

    [Fact]
    public void AnimatedLand_RevertsToPlainRow_OnLeaveBattlefield()
    {
        var svc = new ContinuousEffectsService();
        var land = MakeLand(svc);
        AnimateLandEffect.Register(svc, land, CardSubtype.Elemental, 2, 2, grantsHaste: true);
        svc.Compute(land).Should().BeOfType<CreatureCharacteristics>();

        land.SetZone(ZoneType.Graveyard);

        svc.Compute(land).Should().NotBeOfType<CreatureCharacteristics>(
            "animate effects are inactive off the battlefield → no creature row");
    }

    private sealed class TestPumpL7c : ContinuousEffect
    {
        private readonly Permanent _t; private readonly int _p, _to;
        public TestPumpL7c(Permanent t, int p, int to) { _t = t; _p = p; _to = to; }
        public override Layer Layer => Layer.PT_Modify;
        public override bool AppliesTo(Creature c) => AppliesTo((Permanent)c);
        public override bool AppliesTo(Permanent perm) => ReferenceEquals(perm, _t);
        public override Permanent? Source => _t;
        public override void Apply(CreatureCharacteristics chars)
        { chars.Power += _p; chars.Toughness += _to; }
    }
}
