using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LordOfTheUnrealFactory"/>.
///
/// Card: Lord of the Unreal (Magic 2012, {U}{U}), Creature — Human Wizard
/// 2/2. "Illusion creatures you control get +1/+1 and have hexproof."
///
/// Covers:
/// - Identity (name, type, mana cost, Human + Wizard subtypes, 2/2,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - LordStaticEffect: controller's Illusions get +1/+1 and Hexproof.
/// - Controller's own non-Illusion creature is NOT pumped / granted hexproof.
/// - Opponent's Illusion is NOT pumped (controller-scoped, CR 109.5).
/// - LTB lifts the bonus + hexproof (effect's IsActive gate falls when the
///   source leaves the battlefield).
/// - Lord itself (a Human Wizard, not an Illusion) is never pumped.
/// - Two Lords stack +1/+1 and the Hexproof grant is idempotent.
/// </summary>
[Trait("Color", "U")]
public class LordOfTheUnrealFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void LordOfTheUnreal_Identity()
    {
        var c = LordOfTheUnrealFactory.Create(_alice);

        c.Name.Should().Be("Lord of the Unreal");
        c.ManaCost.Should().Be("{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LordOfTheUnreal_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Lord of the Unreal", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Lord of the Unreal");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    [Fact]
    public void LordOfTheUnreal_BuffsControllerIllusion_Plus1Plus1AndHexproof()
    {
        var svc = new ContinuousEffectsService();

        var illusion = new Creature("Phantasmal Bear", "{U}", 2, 2,
            subtypes: new[] { CardSubtype.Illusion })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var lord = LordOfTheUnrealFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        illusion.GetPower().Should().Be(3,
            "Illusions controlled by the Lord's controller get +1/+1 (2 -> 3 power).");
        illusion.GetToughness().Should().Be(3);

        svc.Compute(illusion).Keywords.Should().Contain("Hexproof",
            "CR 702.11 — controller's Illusions gain hexproof from the Lord's static.");
    }

    [Fact]
    public void LordOfTheUnreal_DoesNotBuff_NonIllusion()
    {
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var lord = LordOfTheUnrealFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        bear.GetPower().Should().Be(2,
            "the Lord only buffs creatures matching the Illusion subtype.");
        bear.GetToughness().Should().Be(2);
        svc.Compute(bear).Keywords.Should().NotContain("Hexproof",
            "non-Illusion creatures don't get the granted hexproof.");
    }

    [Fact]
    public void LordOfTheUnreal_DoesNotBuff_OpponentIllusion()
    {
        var svc = new ContinuousEffectsService();

        var oppIllusion = new Creature("Phantasmal Bear", "{U}", 2, 2,
            subtypes: new[] { CardSubtype.Illusion })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var lord = LordOfTheUnrealFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        oppIllusion.GetPower().Should().Be(2,
            "the Lord's static is scoped to its controller's Illusions (CR 109.5 — 'you').");
        oppIllusion.GetToughness().Should().Be(2);
        svc.Compute(oppIllusion).Keywords.Should().NotContain("Hexproof",
            "opponent's Illusions don't get the granted hexproof.");
    }

    [Fact]
    public void LordOfTheUnreal_DoesNotBuffItself()
    {
        // The Lord is a Human Wizard, not an Illusion — the subtype gate
        // keeps it out of its own +1/+1 + hexproof buff.
        var svc = new ContinuousEffectsService();

        var lord = LordOfTheUnrealFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        lord.GetPower().Should().Be(2, "the Lord is not an Illusion, so it doesn't self-buff.");
        lord.GetToughness().Should().Be(2);
        svc.Compute(lord).Keywords.Should().NotContain("Hexproof");
    }

    [Fact]
    public void LordOfTheUnreal_LTB_LiftsBonusFromIllusion()
    {
        var svc = new ContinuousEffectsService();

        var illusion = new Creature("Phantasmal Bear", "{U}", 2, 2,
            subtypes: new[] { CardSubtype.Illusion })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var lord = LordOfTheUnrealFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        illusion.GetPower().Should().Be(3);
        svc.Compute(illusion).Keywords.Should().Contain("Hexproof");

        // Lord dies — LordStaticEffect.IsActive() short-circuits when the
        // source isn't on the battlefield (CR 613).
        lord.SetZone(ZoneType.Graveyard);

        illusion.GetPower().Should().Be(2, "bonus lifts on LTB");
        illusion.GetToughness().Should().Be(2);
        svc.Compute(illusion).Keywords.Should().NotContain("Hexproof",
            "granted hexproof lifts when the Lord leaves the battlefield.");
    }

    [Fact]
    public void TwoLords_StackPower_HexproofIdempotent()
    {
        var svc = new ContinuousEffectsService();

        var illusion = new Creature("Phantasmal Bear", "{U}", 2, 2,
            subtypes: new[] { CardSubtype.Illusion })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var lord1 = LordOfTheUnrealFactory.Create(_alice, svc);
        lord1.Zone = ZoneType.Battlefield;
        lord1.ActiveEffects = svc;

        var lord2 = LordOfTheUnrealFactory.Create(_alice, svc);
        lord2.Zone = ZoneType.Battlefield;
        lord2.ActiveEffects = svc;

        illusion.GetPower().Should().Be(4,
            "two Lords stack +1/+1 — 2 base + 2 from two lords = 4.");
        illusion.GetToughness().Should().Be(4);
        svc.Compute(illusion).Keywords.Should().Contain("Hexproof",
            "the Hexproof keyword grant is idempotent — set semantics.");
    }
}
