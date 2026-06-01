using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// CR 711 — Layer-0 per-face characteristic replacement for transform DFC
/// permanents. When a transform DFC is on its BACK face,
/// <see cref="ContinuousEffectsService.Compute"/> seeds from the back face's
/// printed characteristics (name/types/subtypes/supertypes/keywords/colour/PT)
/// BEFORE the CR 613 layer pipeline (anthems / counters / type+colour grants)
/// applies on top. Reverts on transform-back; face-down (CR 708.2) still wins.
/// </summary>
public class DfcBackFaceCharacteristicsTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ContinuousEffectsService Wire(Creature c)
    {
        var ces = new ContinuousEffectsService();
        c.ActiveEffects = ces;
        return ces;
    }

    // ------------------------------------------------------------------
    // Delver of Secrets → Insectile Aberration (3/2 flying, blue Insect)
    // ------------------------------------------------------------------

    [Fact]
    public void Delver_FrontFace_Computes_OneOne_NoFlying()
    {
        var delver = DelverOfSecretsFactory.Create(_alice);
        var ces = Wire(delver);

        var chars = (CreatureCharacteristics)ces.Compute((Permanent)delver);
        chars.Power.Should().Be(1);
        chars.Toughness.Should().Be(1);
        chars.Keywords.Should().NotContain("Flying");
    }

    [Fact]
    public void Delver_BackFace_Computes_ThreeTwo_Flying()
    {
        var delver = DelverOfSecretsFactory.Create(_alice);
        var ces = Wire(delver);

        delver.MdfcState!.Transform(); // → Insectile Aberration

        var chars = (CreatureCharacteristics)ces.Compute((Permanent)delver);
        chars.Power.Should().Be(3, "Insectile Aberration is 3/2");
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().Contain("Flying", "the back face has Flying");
        chars.Subtypes.Should().Contain(CardSubtype.Insect);
        chars.Colors.Should().Contain(ManaColorEnum.Blue);
    }

    [Fact]
    public void Delver_BackFace_CombatPT_IsThreeTwo_AndRevertsOnFlipBack()
    {
        var delver = DelverOfSecretsFactory.Create(_alice);
        Wire(delver);

        delver.MdfcState!.Transform();
        delver.GetPower().Should().Be(3);
        delver.GetToughness().Should().Be(2);

        delver.MdfcState!.Transform(); // back to front
        delver.GetPower().Should().Be(1, "reverts to the 1/1 front body");
        delver.GetToughness().Should().Be(1);
    }

    [Fact]
    public void Delver_BackFace_AnthemAndCountersLayerOnTopOfBackPT()
    {
        var delver = DelverOfSecretsFactory.Create(_alice);
        var ces = Wire(delver);
        delver.MdfcState!.Transform(); // 3/2 back

        // +1/+1 counter rides Layer 7c on top of the back-face P/T.
        delver.Counters.Add(Majik.Core.Counters.CounterType.PlusOnePlusOne);

        // Layer-7c anthem (+2/+2 to this creature).
        ces.Register(new PlusTwoAnthem(delver));

        var chars = (CreatureCharacteristics)ces.Compute((Permanent)delver);
        chars.Power.Should().Be(3 + 1 + 2, "3 base back + 1 counter + 2 anthem");
        chars.Toughness.Should().Be(2 + 1 + 2);
        chars.Keywords.Should().Contain("Flying", "back-face Flying survives the layer pipeline");
    }

    /// <summary>Local Layer-7c +2/+2 anthem to a specific creature.</summary>
    private sealed class PlusTwoAnthem : ContinuousEffect
    {
        private readonly Creature _target;
        public PlusTwoAnthem(Creature target) => _target = target;
        public override Layer Layer => Layer.PT_Modify;
        public override bool IsActive() => _target.Zone == Majik.Core.Zones.ZoneType.Battlefield
            || _target.ActiveEffects != null; // shape tests: not on battlefield
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Power += 2;
            chars.Toughness += 2;
        }
    }

    [Fact]
    public void Delver_BackFace_FaceDownStillWins_TwoTwo()
    {
        var delver = DelverOfSecretsFactory.Create(_alice);
        Wire(delver);
        delver.MdfcState!.Transform(); // back 3/2
        delver.MarkFaceDown();         // CR 708.2 — 2/2 vanilla wins

        delver.GetPower().Should().Be(2, "face-down overrides the back-face seed");
        delver.GetToughness().Should().Be(2);
    }

    // ------------------------------------------------------------------
    // Graveyard Trespasser → Graveyard Glutton (4/4 black Werewolf)
    // ------------------------------------------------------------------

    [Fact]
    public void GraveyardTrespasser_BackFace_Computes_FourFour_Werewolf_Black()
    {
        var gt = GraveyardTrespasserFactory.Create(_alice);
        var ces = Wire(gt);

        gt.MdfcState!.Transform(); // → Graveyard Glutton

        var chars = (CreatureCharacteristics)ces.Compute((Permanent)gt);
        chars.Power.Should().Be(4, "Graveyard Glutton is 4/4");
        chars.Toughness.Should().Be(4);
        chars.Subtypes.Should().Contain(CardSubtype.Werewolf);
        chars.Colors.Should().Contain(ManaColorEnum.Black);
    }

    [Fact]
    public void GraveyardTrespasser_TransformViaDayNight_YieldsFourFourBackBody()
    {
        var gt = GraveyardTrespasserFactory.Create(_alice);
        Wire(gt);

        Majik.Core.Keywords.DayboundNightbound.OnDayNightChanged(
            new[] { (Card)gt }, Majik.Core.Game.DayNightDesignation.Night);

        gt.MdfcState!.IsBackFace.Should().BeTrue();
        gt.GetPower().Should().Be(4, "the daybound→nightbound transform yields the 4/4 back body");
        gt.GetToughness().Should().Be(4);
    }

    [Fact]
    public void TransformBumpsGenerationCache()
    {
        var delver = DelverOfSecretsFactory.Create(_alice);
        var ces = Wire(delver);

        // Prime the cache on the front face.
        delver.GetPower().Should().Be(1);

        // Transform must invalidate the memoized P/T (front 1 → back 3).
        delver.MdfcState!.Transform();
        delver.GetPower().Should().Be(3, "transform invalidates the CES generation cache");
    }
}
