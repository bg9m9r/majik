using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Abilities;

public class TriggersTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Mock<ITriggeredAbility> _ability = new();

    [Fact]
    public void OnEnterBattlefieldSelf_Matches_OnlySourceEnteringBattlefield()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var other = new Creature("Wolf", "2G", 3, 3) { Owner = _alice };

        var cond = Triggers.OnEnterBattlefieldSelf(source);

        cond.Matches(new CardMovedEvent(source, ZoneType.Hand, ZoneType.Battlefield), _ability.Object).Should().BeTrue();
        cond.Matches(new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield), _ability.Object).Should().BeFalse();
        cond.Matches(new CardMovedEvent(source, ZoneType.Battlefield, ZoneType.Graveyard), _ability.Object).Should().BeFalse();
    }

    [Fact]
    public void OnAnyCreatureEntersBattlefield_Matches_AnyCreatureEntering()
    {
        var creature = new Creature("Wolf", "2G", 3, 3) { Owner = _alice };
        var land = new Land("Forest") { Owner = _alice };

        var cond = Triggers.OnAnyCreatureEntersBattlefield();

        cond.Matches(new CardMovedEvent(creature, ZoneType.Hand, ZoneType.Battlefield), _ability.Object).Should().BeTrue();
        cond.Matches(new CardMovedEvent(land, ZoneType.Hand, ZoneType.Battlefield), _ability.Object).Should().BeFalse();
        cond.Matches(new CardMovedEvent(creature, ZoneType.Battlefield, ZoneType.Graveyard), _ability.Object).Should().BeFalse();
    }

    [Fact]
    public void OnDies_Matches_CreatureMovingFromBattlefieldToGraveyard()
    {
        var creature = new Creature("Wolf", "2G", 3, 3) { Owner = _alice };

        var cond = Triggers.OnDies(creature);

        cond.Matches(new CardMovedEvent(creature, ZoneType.Battlefield, ZoneType.Graveyard), _ability.Object).Should().BeTrue();
        cond.Matches(new CardMovedEvent(creature, ZoneType.Battlefield, ZoneType.Exile), _ability.Object).Should().BeFalse();
    }

    [Fact]
    public void OnCardDrawnByPlayer_OnlyMatchesThatPlayer()
    {
        var card = new Instant("X", "1") { Owner = _alice };

        var cond = Triggers.OnCardDrawnByPlayer(_alice);

        cond.Matches(new CardDrawnEvent(card, _alice), _ability.Object).Should().BeTrue();
        cond.Matches(new CardDrawnEvent(card, _bob), _ability.Object).Should().BeFalse();
    }

    [Fact]
    public void OnSpellCast_Matches_AnySpellCastEvent()
    {
        var spell = new Mock<ISpell>().Object;

        var cond = Triggers.OnSpellCast();

        cond.Matches(new SpellCastEvent(spell), _ability.Object).Should().BeTrue();
        var unrelatedCard = new Instant("X", "1") { Owner = _alice };
        cond.Matches(new CardDrawnEvent(unrelatedCard, _alice), _ability.Object).Should().BeFalse();
    }

    [Fact]
    public void OnEnterBattlefieldSelf_NullSource_Throws()
    {
        var act = () => Triggers.OnEnterBattlefieldSelf(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
