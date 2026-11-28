using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// A <see cref="ChoiceCommand"/> that names an id not offered, names one twice,
/// or picks fewer than <see cref="ChoiceRequest.Min"/> / more than
/// <see cref="ChoiceRequest.Max"/> is rejected with the prompt left pending.
/// YesNo keeps its semantics.
/// </summary>
public class ChoiceCommandRejectionTests
{
    private readonly Player _alice = new("Alice", 20);

    private GameContext Ctx() => new(_alice, new[] { _alice }, _alice, 1,
        Majik.Core.StateMachine.StepStateType.PreCombatMain, new Majik.Core.Stack.Stack());

    private Creature Bear(string name) => new(name, "{1}{G}", 2, 2) { Owner = _alice };

    private ChoiceCommand Pick(ChoiceKind kind, params Guid[] ids) =>
        new(kind.ToString(), ids) { PlayerId = _alice.Id };

    [Fact]
    public async Task AnUnknownId_IsRejected_ThenAValidPickIsAccepted()
    {
        var a = Bear("A");
        var agent = new RemoteAgent(_alice);
        var task = agent.ChooseAsync(Ctx(), new ChoiceRequest(ChoiceKind.PickOne, "pick one", 1, 1, new object[] { a }));

        FluentActions.Invoking(() => agent.Submit(Pick(ChoiceKind.PickOne, Guid.NewGuid())))
            .Should().Throw<InvalidOperationException>().WithMessage("*not in the offered*");
        agent.HasPending.Should().BeTrue();

        agent.Submit(Pick(ChoiceKind.PickOne, a.InstanceId));
        (await task).Should().ContainSingle().Which.Should().BeSameAs(a);
    }

    [Fact]
    public async Task TooFewTooManyOrRepeatedPicks_AreRejected()
    {
        var a = Bear("A"); var b = Bear("B"); var c = Bear("C");
        var agent = new RemoteAgent(_alice);
        var task = agent.ChooseAsync(Ctx(), new ChoiceRequest(ChoiceKind.PickN, "pick two", 2, 2, new object[] { a, b, c }));

        foreach (var bad in new[] { Pick(ChoiceKind.PickN, a.InstanceId), Pick(ChoiceKind.PickN, a.InstanceId, b.InstanceId, c.InstanceId) })
        {
            FluentActions.Invoking(() => agent.Submit(bad))
                .Should().Throw<InvalidOperationException>().WithMessage("*between 2 and 2*");
        }
        FluentActions.Invoking(() => agent.Submit(Pick(ChoiceKind.PickN, a.InstanceId, a.InstanceId)))
            .Should().Throw<InvalidOperationException>().WithMessage("*more than once*");
        agent.HasPending.Should().BeTrue();

        agent.Submit(Pick(ChoiceKind.PickN, c.InstanceId, a.InstanceId));
        (await task).Should().Equal(c, a);
    }

    [Fact]
    public async Task AnOptionalChoice_AcceptsNoPick_AndAMinLargerThanThePool_NeedsOnlyThePool()
    {
        var a = Bear("A");
        var agent = new RemoteAgent(_alice);
        var optional = agent.ChooseAsync(Ctx(), new ChoiceRequest(ChoiceKind.PickOne, "may pick", 1, 1, new object[] { a }, Optional: true));
        agent.Submit(Pick(ChoiceKind.PickOne));
        (await optional).Should().BeEmpty();

        // "Choose two" over a single candidate: as many as possible (one).
        var capped = agent.ChooseAsync(Ctx(), new ChoiceRequest(ChoiceKind.PickN, "pick two", 2, 2, new object[] { a }));
        agent.Submit(Pick(ChoiceKind.PickN, a.InstanceId));
        (await capped).Should().ContainSingle();
    }

    [Fact]
    public async Task YesNo_KeepsItsSemantics()
    {
        var agent = new RemoteAgent(_alice);
        var yes = agent.ChooseAsync(Ctx(), new ChoiceRequest(ChoiceKind.YesNo, "may", 0, 1, Array.Empty<object>()));
        agent.Submit(new ChoiceCommand("YesNo", Array.Empty<Guid>(), YesNo: true) { PlayerId = _alice.Id });
        (await yes).Should().ContainSingle();

        var no = agent.ChooseAsync(Ctx(), new ChoiceRequest(ChoiceKind.YesNo, "may", 0, 1, Array.Empty<object>()));
        agent.Submit(new ChoiceCommand("YesNo", Array.Empty<Guid>()) { PlayerId = _alice.Id });
        (await no).Should().BeEmpty();
    }
}