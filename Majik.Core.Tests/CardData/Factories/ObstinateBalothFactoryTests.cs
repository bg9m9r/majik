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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ObstinateBalothFactory"/> — Creature — Beast {2}{G}{G}
/// 4/4 (Magic 2011 / reprints). Oracle:
///   "When this creature enters, you gain 4 life.
///    If a spell or ability an opponent controls causes you to discard this
///    card, put it onto the battlefield instead of putting it into your
///    graveyard."
///
/// Covers (the implemented ETB lifegain clause):
///   - Card identity (Creature + Beast, {2}{G}{G}, 4/4, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: exactly one ETB <see cref="TriggeredAbility"/>.
///   - Trigger condition: this Baloth entering the battlefield → matches; an
///     unrelated creature entering → does not match.
///   - Trigger effect resolution: controller gains 4 life (CR 119.3).
///
/// The discard-replacement clause is deferred (the engine's discard funnel does
/// not record opponent-caused-discard attribution); see the factory doc.
/// </summary>
public class ObstinateBalothFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ObstinateBaloth_IsBeast_AtTwoGG_FourFour()
    {
        var c = ObstinateBalothFactory.Create(_alice);

        c.Name.Should().Be("Obstinate Baloth");
        c.ManaCost.Should().Be("{2}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ObstinateBaloth_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Obstinate Baloth", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Obstinate Baloth");
        card.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB lifegain trigger is attached");
    }

    [Fact]
    public void ObstinateBaloth_SelfEnters_TriggerMatches()
    {
        var baloth = ObstinateBalothFactory.Create(_alice);
        baloth.SetZone(ZoneType.Hand);

        var trigger = baloth.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(baloth, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "the Baloth's ETB lifegain fires when it enters the battlefield");
    }

    [Fact]
    public void ObstinateBaloth_OtherCreatureEnters_DoesNotMatch()
    {
        var baloth = ObstinateBalothFactory.Create(_alice);
        baloth.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);

        var trigger = baloth.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "the ETB trigger is self-scoped — only this Baloth entering fires it");
    }

    [Fact]
    public void ObstinateBaloth_OnResolve_ControllerGainsFourLife()
    {
        var baloth = ObstinateBalothFactory.Create(_alice);
        baloth.SetZone(ZoneType.Battlefield);

        var trigger = baloth.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(24, "Obstinate Baloth gains its controller 4 life");
    }
}
