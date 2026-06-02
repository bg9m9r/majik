using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Deceiver Exarch (New Phyrexia, {2}{U}).
///
/// Covers:
///   - Card shape (name, types, subtypes, P/T, mana cost).
///   - Flash keyword marker.
///   - ETB trigger structure (mandatory 1..1 "target permanent" union
///     predicate covering both printed modes).
///   - Resolve-time legality:
///       * Untap an opponent's tapped permanent (mode 1).
///       * Untap a controller-owned noncreature permanent (mode 2).
///       * Reject an own-controlled creature (neither mode legal).
///       * Reject a target that left the battlefield (CR 608.2b).
///       * Idempotent on an already-untapped legal target.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "C")]
public class DeceiverExarchFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void DeceiverExarch_IsCreature_Cleric_1_4_AtCost2U()
    {
        var e = DeceiverExarchFactory.Create(_alice);

        e.Name.Should().Be("Deceiver Exarch");
        e.ManaCost.Should().Be("{2}{U}");
        e.HasType(CardType.Creature).Should().BeTrue();
        e.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        e.BasePower.Should().Be(1);
        e.BaseToughness.Should().Be(4);
    }

    [Fact]
    public void DeceiverExarch_HasFlash()
    {
        var e = DeceiverExarchFactory.Create(_alice);

        var keywords = e.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
    }

    [Fact]
    public void DeceiverExarch_EtbTrigger_DeclaresMandatoryTargetPermanent()
    {
        var e = DeceiverExarchFactory.Create(_alice);

        var triggers = e.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1, "printed \"Choose one —\" is mandatory once on resolution");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("opponent");
        req.Description.Should().Contain("noncreature");

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void DeceiverExarch_Etb_UntapsOpponentsPermanent()
    {
        var ex = DeceiverExarchFactory.Create(_alice);

        // Bob's tapped land (any permanent type qualifies for mode 1).
        var island = NamedCardFactory.Create("Island", _bob);
        if (island is Permanent isle)
        {
            isle.SetController(_bob);
            isle.SetZone(ZoneType.Battlefield);
            _bob.Zones.Battlefield.AddCard(isle);
            isle.Tap();

            var etb = ex.Abilities.OfType<TriggeredAbility>().Single();
            etb.SetChosenTargets(new IReadOnlyList<object>[]
            {
                new object[] { isle },
            });

            foreach (var eff in etb.Effects) eff.Execute();

            isle.IsTapped.Should().BeFalse("mode 1 untaps an opponent's permanent");
        }
        else
        {
            Assert.Fail("Island should be a Permanent");
        }
    }

    [Fact]
    public void DeceiverExarch_Etb_UntapsOwnNoncreaturePermanent()
    {
        var ex = DeceiverExarchFactory.Create(_alice);

        // Alice's tapped noncreature (a Land qualifies for mode 2).
        var island = NamedCardFactory.Create("Island", _alice);
        if (island is Permanent isle)
        {
            isle.SetController(_alice);
            isle.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(isle);
            isle.Tap();

            var etb = ex.Abilities.OfType<TriggeredAbility>().Single();
            etb.SetChosenTargets(new IReadOnlyList<object>[]
            {
                new object[] { isle },
            });

            foreach (var eff in etb.Effects) eff.Execute();

            isle.IsTapped.Should().BeFalse("mode 2 untaps own noncreature permanent");
        }
        else
        {
            Assert.Fail("Island should be a Permanent");
        }
    }

    [Fact]
    public void DeceiverExarch_Etb_RejectsOwnCreature()
    {
        var ex = DeceiverExarchFactory.Create(_alice);

        // Alice's own creature — illegal under both modes.
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        grizzly.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(grizzly);
        grizzly.Tap();

        var etb = ex.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });

        foreach (var eff in etb.Effects) eff.Execute();

        grizzly.IsTapped.Should().BeTrue(
            "own creature is illegal under both modes — no untap happens");
    }

    [Fact]
    public void DeceiverExarch_Etb_TargetLeftBattlefield_NoOp()
    {
        var ex = DeceiverExarchFactory.Create(_alice);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        grizzly.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(grizzly);

        var etb = ex.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });

        foreach (var eff in etb.Effects) eff.Execute();

        grizzly.IsTapped.Should().BeFalse(
            "CR 608.2b — target off battlefield → ability does nothing");
    }

    [Fact]
    public void DeceiverExarch_Etb_AlreadyUntappedTarget_Idempotent()
    {
        var ex = DeceiverExarchFactory.Create(_alice);

        var island = NamedCardFactory.Create("Island", _bob);
        if (island is Permanent isle)
        {
            isle.SetController(_bob);
            isle.SetZone(ZoneType.Battlefield);
            _bob.Zones.Battlefield.AddCard(isle);
            // Untapped to start.

            var etb = ex.Abilities.OfType<TriggeredAbility>().Single();
            etb.SetChosenTargets(new IReadOnlyList<object>[]
            {
                new object[] { isle },
            });

            // No throw — printed Untap is idempotent on already-untapped target.
            foreach (var eff in etb.Effects) eff.Execute();

            isle.IsTapped.Should().BeFalse();
        }
        else
        {
            Assert.Fail("Island should be a Permanent");
        }
    }
}
