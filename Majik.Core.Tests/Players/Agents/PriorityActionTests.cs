using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.Players.Agents;

public class PriorityActionTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Pass_IsSingleton()
    {
        PriorityAction.Pass.Should().BeSameAs(PriorityAction.Pass);
    }

    [Fact]
    public void CastSpell_ExposesCardAndTargets()
    {
        var card = new Instant("Bolt", "R") { Owner = _alice };
        var target = new Creature("Bear", "1G", 2, 2) { Owner = _alice };

        var action = new PriorityAction.CastSpell(card, new[] { (object)target });

        action.Card.Should().BeSameAs(card);
        action.Targets.Should().ContainSingle().Which.Should().BeSameAs(target);
    }

    [Fact]
    public void PlayLand_ExposesLand()
    {
        var land = new Land("Mountain") { Owner = _alice };

        var action = new PriorityAction.PlayLand(land);

        action.Land.Should().BeSameAs(land);
    }

    [Fact]
    public void PatternMatch_DispatchesPerCase()
    {
        PriorityAction[] actions =
        {
            PriorityAction.Pass,
            new PriorityAction.PlayLand(new Land("Mountain") { Owner = _alice }),
            new PriorityAction.CastSpell(new Instant("Bolt", "R") { Owner = _alice }, Array.Empty<object>()),
        };

        var labels = actions.Select(a => a switch
        {
            PriorityAction.PassAction => "pass",
            PriorityAction.PlayLand => "land",
            PriorityAction.CastSpell => "spell",
            _ => "?",
        }).ToList();

        labels.Should().Equal("pass", "land", "spell");
    }
}
