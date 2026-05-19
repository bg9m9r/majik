using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Xunit;

public class ActionLogTests
{
    [Fact]
    public void NewLog_IsEmpty()
    {
        new ActionLog().Count.Should().Be(0);
    }

    [Fact]
    public void Append_RecordsInOrder()
    {
        var log = new ActionLog();
        var p1 = Guid.NewGuid();

        log.Append(new PassPriorityCommand { PlayerId = p1 });
        log.Append(new MulliganCommand(true) { PlayerId = p1 });

        log.Actions.Should().HaveCount(2);
        log.Actions[0].Command.Should().BeOfType<PassPriorityCommand>();
        log.Actions[1].Command.Should().BeOfType<MulliganCommand>();
    }

    [Fact]
    public void Append_Null_Throws()
    {
        var log = new ActionLog();
        var act = () => log.Append(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TimestampMonotonic()
    {
        var log = new ActionLog();
        var p = Guid.NewGuid();

        log.Append(new PassPriorityCommand { PlayerId = p });
        System.Threading.Thread.Sleep(2);
        log.Append(new PassPriorityCommand { PlayerId = p });

        log.Actions[1].At.Should().BeOnOrAfter(log.Actions[0].At);
    }
}
