using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Every prompt must carry the data needed to answer it from the payload
/// alone: the X bounds (CR 107.3 / 601.2b), the mode list and how many modes
/// to pick (CR 700.2), and an answerable id for every option of a declarative
/// choice — including players and value sentinels (a colour, a card name, a
/// mode index), which the card-only <see cref="PromptDto.Candidates"/> cannot
/// represent.
/// </summary>
public class PromptAnswerDataTests
{
    private readonly GameFacade _facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
    private readonly List<PromptDto> _prompts = new();
    private readonly IDisposable _sub;
    private readonly RemoteAgent _agent;

    public PromptAnswerDataTests()
    {
        _sub = _facade.SubscribePrompts(_prompts.Add);
        _agent = (RemoteAgent)typeof(GameFacade)
            .GetField("_aliceAgent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(_facade)!;
    }

    private Player Alice => _facade.Alice;

    private T Put<T>(T card, ZoneType zone) where T : Card
    {
        card.SetOwner(Alice);
        card.SetController(Alice);
        if (zone == ZoneType.Hand) Alice.Zones.Hand.AddCard(card);
        else Alice.Zones.Battlefield.AddCard(card);
        card.SetZone(zone);
        return card;
    }

    private void Forests(int n)
    {
        for (var i = 0; i < n; i++)
        {
            var forest = Put(new Land("Forest"), ZoneType.Battlefield);
            forest.AddAbility(new ManaAbility(forest, Alice, ManaCost.Parse("G")));
        }
    }

    private T Cmd<T>(T command) where T : GameCommand => command with { PlayerId = Alice.Id };

    [Fact]
    public async Task ChooseX_ForASpellBeingCast_ShipsMinZero_AndTheManaAffordableMax()
    {
        // CR 601.2b / 107.3 — X is announced while casting; X can't be negative.
        // Three Forests, a {X}{G} spell: at most 3 - 1 = 2 can go into X.
        Forests(3);
        var hydra = Put(new Creature("Hydra", "{X}{G}", 0, 0), ZoneType.Hand);

        var task = _agent.ChooseXAsync(null!, hydra);

        var prompt = _prompts.Should().ContainSingle().Subject;
        prompt.ExpectedKinds.Should().Equal(nameof(ChooseXCommand));
        prompt.MinX.Should().Be(0);
        prompt.MaxX.Should().Be(2);
        _agent.Submit(Cmd(new ChooseXCommand(2)));
        (await task).Should().Be(2);
    }

    [Fact]
    public async Task ChooseX_WithoutAnXManaCost_HasNoKnownUpperBound()
    {
        // e.g. an X chosen during resolution (Harnessed Lightning's energy) —
        // the engine knows no bound, so MaxX is null.
        var source = Put(new Instant("Harnessed Lightning", "{1}{R}"), ZoneType.Hand);

        var task = _agent.ChooseXAsync(null!, source);

        var prompt = _prompts.Should().ContainSingle().Subject;
        prompt.MinX.Should().Be(0);
        prompt.MaxX.Should().BeNull();
        _agent.Submit(Cmd(new ChooseXCommand(5)));
        (await task).Should().Be(5, "MaxX is a hint; the engine does not cap X");
    }

    [Fact]
    public async Task ChooseX_Negative_IsRejected_PromptStaysPending()
    {
        var source = Put(new Creature("Hydra", "{X}{G}", 0, 0), ZoneType.Hand);
        var task = _agent.ChooseXAsync(null!, source);

        FluentActions.Invoking(() => _agent.Submit(Cmd(new ChooseXCommand(-1))))
            .Should().Throw<InvalidOperationException>().WithMessage("*X can't be negative*");
        _agent.HasPending.Should().BeTrue();
        _prompts.Should().ContainSingle("the prompt is not raised again");

        _agent.Submit(Cmd(new ChooseXCommand(0)));
        (await task).Should().Be(0);
    }

    [Fact]
    public async Task ChooseMode_ShipsEveryMode_AndPickExactlyOne_OutOfRangeRejected()
    {
        // CR 700.2 — "choose one": the mode index ChooseModeCommand accepts.
        var task = _agent.ChooseModeAsync(null!, new[] { "Deal 3 damage", "Draw a card" });

        var prompt = _prompts.Should().ContainSingle().Subject;
        prompt.ExpectedKinds.Should().Equal(nameof(ChooseModeCommand));
        prompt.ModeOptions.Should().Equal(new ModeOptionDto(0, "Deal 3 damage"), new ModeOptionDto(1, "Draw a card"));
        prompt.MinModes.Should().Be(1);
        prompt.MaxModes.Should().Be(1);

        FluentActions.Invoking(() => _agent.Submit(Cmd(new ChooseModeCommand(2))))
            .Should().Throw<InvalidOperationException>().WithMessage("*mode index 2 is out of range*");
        _agent.HasPending.Should().BeTrue();

        _agent.Submit(Cmd(new ChooseModeCommand(1)));
        (await task).Should().Be(1);
    }

    [Fact]
    public async Task ChooseModes_ChooseOneOrMore_ShipsModesBoundsAndAnswerableIds()
    {
        // CR 700.2d / 700.2e — "choose one or more" (here: one or two of three).
        IPlayerAgent agent = _agent;
        var task = agent.ChooseModesAsync(null!, new[] { "Counter", "Bounce", "Draw" }, minModes: 1, maxModes: 2);

        var prompt = _prompts.Should().ContainSingle().Subject;
        prompt.ExpectedKinds.Should().Equal(nameof(ChoiceCommand));
        prompt.ModeOptions.Should().Equal(
            new ModeOptionDto(0, "Counter"), new ModeOptionDto(1, "Bounce"), new ModeOptionDto(2, "Draw"));
        prompt.MinModes.Should().Be(1);
        prompt.MaxModes.Should().Be(2);
        prompt.ChoiceOptions.Should().HaveCount(3);
        prompt.ChoiceOptions!.Select(o => o.Label).Should().Equal("Counter", "Bounce", "Draw");
        prompt.ChoiceOptions!.Select(o => o.Id).Should().OnlyHaveUniqueItems();

        var pick = new[] { prompt.ChoiceOptions![2].Id, prompt.ChoiceOptions![0].Id };
        _agent.Submit(Cmd(new ChoiceCommand(prompt.ChoiceView!.Kind, pick)));
        (await task).Should().Equal(2, 0);
    }

    [Fact]
    public async Task Choice_OverPlayers_ShipsPlayerOptions()
    {
        // CR 109.1 / 601.2c — "choose an opponent"-style pick (more than one
        // candidate so the engine actually asks).
        var task = _agent.ChooseAsync(null!, new ChoiceRequest(
            ChoiceKind.PickOne, "Choose a player", 1, 1, new object[] { _facade.Alice, _facade.Bob }));

        var prompt = _prompts.Should().ContainSingle().Subject;
        prompt.ChoiceOptions.Should().Equal(
            new ChoiceOptionDto(_facade.Alice.Id, "Player", "Alice"),
            new ChoiceOptionDto(_facade.Bob.Id, "Player", "Bob"));
        prompt.PlayerCandidates.Should().NotBeNull();
        prompt.PlayerCandidates!.Select(p => p.Id).Should().Equal(_facade.Alice.Id, _facade.Bob.Id);

        _agent.Submit(Cmd(new ChoiceCommand("PickOne", new[] { _facade.Bob.Id })));
        (await task).Should().Equal(_facade.Bob);
    }

    [Fact]
    public async Task Choice_OverValueSentinels_ColourChoice_IsAnswerable()
    {
        // CR 105.1 — "choose a color" routes through ChooseAsync with boxed
        // ManaColor candidates, which have no instance id of their own.
        IPlayerAgent agent = _agent;
        var task = agent.ChooseColorAsync(null, "Choose a color");

        var prompt = _prompts.Should().ContainSingle().Subject;
        prompt.ChoiceOptions.Should().NotBeNull();
        prompt.ChoiceOptions!.Select(o => o.Label).Should().Equal("White", "Blue", "Black", "Red", "Green");
        prompt.ChoiceOptions!.Should().OnlyContain(o => o.Kind == "Value");

        var blue = prompt.ChoiceOptions!.Single(o => o.Label == "Blue").Id;
        _agent.Submit(Cmd(new ChoiceCommand("PickOne", new[] { blue })));
        (await task).Should().Be(ManaColor.Blue);
    }

    [Fact]
    public async Task Choice_OverCards_KeepsCandidates_AndAddsMatchingOptions()
    {
        var bear = Put(new Creature("Bear", "{1}{G}", 2, 2), ZoneType.Battlefield);
        var task = _agent.ChooseAsync(null!, new ChoiceRequest(
            ChoiceKind.PickOne, "Sacrifice a creature", 1, 1, new object[] { bear }));

        var prompt = _prompts.Should().ContainSingle().Subject;
        prompt.Candidates!.Select(c => c.InstanceId).Should().Equal(bear.InstanceId);
        prompt.ChoiceOptions.Should().Equal(new ChoiceOptionDto(bear.InstanceId, "Card", "Bear"));

        _agent.Submit(Cmd(new ChoiceCommand("PickOne", new[] { bear.InstanceId })));
        (await task).Should().Equal(bear);
    }

    [Fact]
    public void YesNoPrompt_CarriesNoneOfTheNewFields()
    {
        _ = _agent.ChooseYesNoAsync(null, "Pay 2 life?", sourceCardName: null);

        var prompt = _prompts.Should().ContainSingle().Subject;
        prompt.MinX.Should().BeNull();
        prompt.MaxX.Should().BeNull();
        prompt.ModeOptions.Should().BeNull();
        prompt.MinModes.Should().BeNull();
        prompt.MaxModes.Should().BeNull();
        prompt.ChoiceOptions.Should().BeNull();
    }

    [Theory]
    [InlineData(new[] { 0, 0 }, "more than once")]
    [InlineData(new[] { 0, 1, 2 }, "between 1 and 2")]
    [InlineData(new int[0], "between 1 and 2")]
    public async Task ChooseModes_ADuplicateOrOutOfRangeAnswer_IsRejectedWithAReason_AndThePromptStaysPending(
        int[] picks, string reason)
    {
        // CR 700.2d — no mode twice; CR 700.2 — the pick word's count. Same
        // policy as every prompt: reject with a reason, keep the prompt open.
        IPlayerAgent agent = _agent;
        var task = agent.ChooseModesAsync(null!, new[] { "Counter", "Bounce", "Draw" }, minModes: 1, maxModes: 2);
        var prompt = _prompts.Should().ContainSingle().Subject;
        var ids = picks.Select(i => prompt.ChoiceOptions![i].Id).ToArray();

        var act = () => _agent.Submit(Cmd(new ChoiceCommand(prompt.ChoiceView!.Kind, ids)));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{reason}*");
        task.IsCompleted.Should().BeFalse("the prompt stays pending");
        _agent.Submit(Cmd(new ChoiceCommand(prompt.ChoiceView!.Kind, new[] { prompt.ChoiceOptions![1].Id })));
        (await task).Should().Equal(1);
    }

    [Fact]
    public async Task ChooseModes_AnId_NotOffered_IsRejected()
    {
        IPlayerAgent agent = _agent;
        var task = agent.ChooseModesAsync(null!, new[] { "Counter", "Bounce" }, minModes: 1, maxModes: 2);
        var prompt = _prompts.Should().ContainSingle().Subject;

        var act = () => _agent.Submit(Cmd(new ChoiceCommand(prompt.ChoiceView!.Kind, new[] { Guid.NewGuid() })));

        act.Should().Throw<InvalidOperationException>().WithMessage("*not in the offered*");
        task.IsCompleted.Should().BeFalse();
    }
}