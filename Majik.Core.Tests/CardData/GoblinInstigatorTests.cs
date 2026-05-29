using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GoblinInstigatorFactory"/>.
///
/// Goblin Instigator (Dominaria, {1}{R}):
///   Creature — Goblin Rogue 1/1.
///   When this creature enters, create a 1/1 red Goblin creature token.
///
/// Closest shipped analogue: <see cref="MoggWarMarshalFactory"/> (same
/// {1}{R} 1/1 Goblin + ETB "create a 1/1 red Goblin token") — Goblin
/// Instigator is the strict ETB-only subset (no Echo, no dies trigger).
///
/// Covers:
///   - Identity (Goblin Rogue 1/1, {1}{R}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Exactly one ETB trigger (Battlefield active), self-match.
///   - Resolving the ETB trigger creates exactly one 1/1 red Goblin
///     creature token under the card's controller.
/// </summary>
public class GoblinInstigatorTests
{
    private readonly Player _alice = new("Alice", 20);

    private static TriggeredAbility GetEtbTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Count == 1
                && t.ActiveZones.Contains(ZoneType.Battlefield));

    [Fact]
    public void GoblinInstigator_Identity()
    {
        var c = GoblinInstigatorFactory.Create(_alice);

        c.Name.Should().Be("Goblin Instigator");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GoblinInstigator_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Goblin Instigator", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Goblin Instigator");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    [Fact]
    public void GoblinInstigator_HasExactlyOneEtbTrigger()
    {
        var c = GoblinInstigatorFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1,
            "Goblin Instigator has exactly one ETB token-creation trigger.");

        var etb = GetEtbTrigger(c);
        etb.ActiveZones.Should().BeEquivalentTo(new[] { ZoneType.Battlefield });
    }

    [Fact]
    public void GoblinInstigator_EtbTrigger_FiresOnEnterBattlefield()
    {
        var c = GoblinInstigatorFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var etb = GetEtbTrigger(c);
        var enter = new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield);
        etb.IsTriggered(enter).Should().BeTrue(
            "ETB trigger fires when Goblin Instigator enters the battlefield (CR 603.6a).");
    }

    [Fact]
    public void GoblinInstigator_EtbTrigger_DoesNotFire_OnOtherCardEntering()
    {
        var c = GoblinInstigatorFactory.Create(_alice);
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);

        var etb = GetEtbTrigger(c);
        var other = new CardMovedEvent(bears, ZoneType.Hand, ZoneType.Battlefield);
        etb.IsTriggered(other).Should().BeFalse(
            "the ETB trigger is self-match — other creatures entering don't fire it.");
    }

    [Fact]
    public void GoblinInstigator_EtbResolve_CreatesOneOneRedGoblinToken()
    {
        var c = GoblinInstigatorFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);

        var goblinsBefore = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(g => g.HasSubtype(CardSubtype.Goblin) && g.IsToken);

        var etb = GetEtbTrigger(c);
        foreach (var e in etb.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(g => g.HasSubtype(CardSubtype.Goblin) && g.IsToken)
            .ToList();
        tokens.Should().HaveCount(goblinsBefore + 1,
            "CR 111 — the ETB trigger creates exactly one 1/1 Goblin token.");

        var token = tokens.Last();
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice);
    }
}
