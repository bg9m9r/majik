using System.Text.Json;
using FluentAssertions;
using Majik.Core.Api.Commands;
using Xunit;

namespace Majik.Core.Api.Tests.Commands;

public class CommandPolymorphismTests
{
    [Fact]
    public void Pass_RoundTrips_AsPolymorphicBase()
    {
        GameCommand cmd = new PassPriorityCommand { PlayerId = Guid.NewGuid() };

        var json = JsonSerializer.Serialize(cmd);

        json.Should().Contain("\"$type\":\"pass\"");
        var back = JsonSerializer.Deserialize<GameCommand>(json);
        back.Should().BeOfType<PassPriorityCommand>()
            .Which.PlayerId.Should().Be(((PassPriorityCommand)cmd).PlayerId);
    }

    [Fact]
    public void CastSpell_RoundTrips()
    {
        var card = Guid.NewGuid();
        var t1 = Guid.NewGuid();
        GameCommand cmd = new CastSpellCommand(card, new[] { t1 }, XValue: null, ModeIndex: null)
        {
            PlayerId = Guid.NewGuid(),
        };

        var json = JsonSerializer.Serialize(cmd);
        var back = JsonSerializer.Deserialize<GameCommand>(json);

        back.Should().BeOfType<CastSpellCommand>();
        var cast = (CastSpellCommand)back!;
        cast.CardInstanceId.Should().Be(card);
        cast.TargetInstanceIds.Should().ContainSingle().Which.Should().Be(t1);
    }

    [Theory]
    [InlineData("pass", typeof(PassPriorityCommand))]
    [InlineData("play-land", typeof(PlayLandCommand))]
    [InlineData("mulligan", typeof(MulliganCommand))]
    [InlineData("targets", typeof(ChooseTargetsCommand))]
    [InlineData("x", typeof(ChooseXCommand))]
    [InlineData("mode", typeof(ChooseModeCommand))]
    [InlineData("mana", typeof(ChooseManaCommand))]
    [InlineData("order-triggers", typeof(OrderTriggersCommand))]
    [InlineData("attackers", typeof(DeclareAttackersCommand))]
    [InlineData("blockers", typeof(DeclareBlockersCommand))]
    public void Discriminator_ResolvesToConcreteType(string disc, Type expected)
    {
        var json = disc switch
        {
            "play-land" => $"{{\"$type\":\"{disc}\",\"LandInstanceId\":\"{Guid.NewGuid()}\"}}",
            "mulligan" => $"{{\"$type\":\"{disc}\",\"Keep\":true}}",
            "targets" => $"{{\"$type\":\"{disc}\",\"TargetInstanceIds\":[]}}",
            "x" => $"{{\"$type\":\"{disc}\",\"X\":3}}",
            "mode" => $"{{\"$type\":\"{disc}\",\"ModeIndex\":0}}",
            "mana" => $"{{\"$type\":\"{disc}\",\"SourceInstanceIds\":[]}}",
            "order-triggers" => $"{{\"$type\":\"{disc}\",\"StackObjectIdsInOrder\":[]}}",
            "attackers" => $"{{\"$type\":\"{disc}\",\"Attackers\":[]}}",
            "blockers" => $"{{\"$type\":\"{disc}\",\"Blockers\":[]}}",
            _ => $"{{\"$type\":\"{disc}\"}}",
        };

        var back = JsonSerializer.Deserialize<GameCommand>(json);

        back.Should().BeOfType(expected);
    }
}
