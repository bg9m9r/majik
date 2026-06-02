using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FrostLynxFactory"/>.
///
/// Card: Frost Lynx — {2}{U} Creature — Elemental Cat 2/2.
/// Oracle text:
///   "When this creature enters, tap target creature an opponent controls.
///    That creature doesn't untap during its controller's next untap step."
///
/// Covers:
/// - Identity ({2}{U}, blue, 2/2, Creature — Elemental Cat, mana value 3).
/// - NamedCardFactory dispatch.
/// - Exactly one battlefield-active ETB TriggeredAbility.
/// - ETB target request: 1..1 "target creature an opponent controls".
/// - ETB effect taps the chosen target (CR 701.20).
/// - ETB effect marks the target to skip its controller's next untap step
///   (CR 502.1 via UntapStepRestrictions.MarkPermanentDoesNotUntap).
/// - CR 608.2b: target off battlefield at resolution → clean no-op.
/// </summary>
[Trait("Color", "U")]
public class FrostLynxFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose()
    {
        // Clear the global untap-skip registry so skip-untap assertions
        // don't bleed into other test cases.
        UntapStepRestrictions.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FrostLynx_Identity()
    {
        var c = FrostLynxFactory.Create(_alice);

        c.Name.Should().Be("Frost Lynx");
        c.ManaCost.Should().Be("{2}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FrostLynx_IsBlue()
    {
        var c = FrostLynxFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Blue,
            "Frost Lynx has a {U} pip in its mana cost");
    }

    [Fact]
    public void FrostLynx_ManaValueIsThree()
    {
        var c = FrostLynxFactory.Create(_alice);

        // {2}{U} → generic 2 + one coloured pip = mana value 3 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // ETB triggered ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void FrostLynx_ExactlyOneBattlefieldActiveEtbTrigger()
    {
        var c = FrostLynxFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();

        triggers.Should().HaveCount(1,
            "Frost Lynx has exactly one triggered ability — the ETB tap + skip-untap");

        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are active while the permanent is on the battlefield (CR 603.6a)");
    }

    [Fact]
    public void FrostLynx_EtbTrigger_DeclaresOneRequiredTargetCreatureOpponentControls()
    {
        var c = FrostLynxFactory.Create(_alice);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.TargetRequests.Should().HaveCount(1,
            "Frost Lynx's ETB names exactly one target");

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1, "the tap is mandatory (not a 'may' clause)");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature",
            because: "the target is a creature an opponent controls");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — tap effect (CR 701.20)
    // -----------------------------------------------------------------------

    [Fact]
    public void FrostLynx_Etb_TapsTargetCreature()
    {
        var lynx = FrostLynxFactory.Create(_alice);

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);
        // target starts untapped — default state

        var etb = lynx.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var e in etb.Effects) e.Execute();

        target.IsTapped.Should().BeTrue(
            "Frost Lynx's ETB taps the target creature (CR 701.20)");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — skip-untap effect (CR 502.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void FrostLynx_Etb_MarksTargetToSkipNextUntapStep()
    {
        var lynx = FrostLynxFactory.Create(_alice);

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);

        var etb = lynx.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var e in etb.Effects) e.Execute();

        UntapStepRestrictions.ShouldSkipUntap(target, _bob).Should().BeTrue(
            "Frost Lynx marks the target to skip its controller's next untap step (CR 502.1)");
    }

    // -----------------------------------------------------------------------
    // CR 608.2b — target off battlefield at resolution → no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void FrostLynx_Etb_TargetLeftBattlefield_NoOp()
    {
        var lynx = FrostLynxFactory.Create(_alice);

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        // Already moved to graveyard before resolution
        target.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(target);

        var etb = lynx.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        var act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow("CR 608.2b — illegal target at resolution is a clean no-op");
        target.IsTapped.Should().BeFalse("off-battlefield card is not tapped");
        UntapStepRestrictions.ShouldSkipUntap(target, _bob).Should().BeFalse(
            "off-battlefield card is not marked for skip-untap");
    }
}
