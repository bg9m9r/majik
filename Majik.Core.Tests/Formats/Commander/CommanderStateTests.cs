using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Formats.Commander;
using Majik.Core.Players;
using Xunit;

public class CommanderStateTests
{
    private readonly Player _alice = new("Alice", 40);
    private readonly Player _bob = new("Bob", 40);

    [Fact]
    public void CommanderTax_StartsAtZero_IncrementsBy2PerCast()
    {
        var cmdr = new Card("Edgar Markov", "3WBR");
        var state = new CommanderState(_alice, cmdr);

        state.CommanderTaxSurcharge().Should().Be(0);

        state.NotifyCastFromCommandZone();
        state.CommanderTaxSurcharge().Should().Be(2);

        state.NotifyCastFromCommandZone();
        state.CommanderTaxSurcharge().Should().Be(4);
    }

    [Fact]
    public void CommanderDamage_AccumulatesPerCommander()
    {
        var attackerCmdr = new Card("Krenko", "3R");
        var state = new CommanderState(_alice, new Card("Edgar Markov", "3WBR"));

        state.TakeCommanderDamage(attackerCmdr, 7);
        state.TakeCommanderDamage(attackerCmdr, 6);

        state.CommanderDamageTaken[attackerCmdr].Should().Be(13);
        state.HasLostToCommanderDamage().Should().BeFalse();
    }

    [Fact]
    public void CommanderDamage_21Plus_TriggersLoss()
    {
        var attackerCmdr = new Card("Krenko", "3R");
        var state = new CommanderState(_alice, new Card("Edgar Markov", "3WBR"));

        state.TakeCommanderDamage(attackerCmdr, 21);

        state.HasLostToCommanderDamage().Should().BeTrue();
    }

    [Fact]
    public void CommanderDamage_TwoCommandersBelow21_NoLoss()
    {
        var k = new Card("Krenko", "3R");
        var u = new Card("Urza", "5");
        var state = new CommanderState(_alice, new Card("Edgar Markov", "3WBR"));

        state.TakeCommanderDamage(k, 15);
        state.TakeCommanderDamage(u, 15);

        state.HasLostToCommanderDamage().Should().BeFalse();
    }
}
