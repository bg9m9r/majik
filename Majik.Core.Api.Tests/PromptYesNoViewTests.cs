using System.Text.Json;
using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Tests for the YesNoView field added to the optional-action prompt
/// payload (CR 117.x / 605.1). The portal renders a modal showing the
/// question + optional source-card label and dispatches a
/// <see cref="ChooseYesNoCommand"/> with the bool answer on click.
/// </summary>
public class PromptYesNoViewTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── RemoteAgent / PromptPayload unit tests ───────────────────────────

    [Fact]
    public async Task YesNoPrompt_PayloadCarriesYesNoView()
    {
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseYesNoAsync(
            ctx: null,
            question: "Pay 2 life for Overgrown Tomb to enter untapped?",
            sourceCardName: "Overgrown Tomb");

        agent.PendingPayload.Should().NotBeNull();
        var view = agent.PendingPayload!.YesNoView;
        view.Should().NotBeNull();
        view!.Question.Should().Contain("Overgrown Tomb");
        view.SourceCardName.Should().Be("Overgrown Tomb");
        // Default labels.
        view.YesLabel.Should().Be("Yes");
        view.NoLabel.Should().Be("No");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task NonYesNoPrompt_Priority_YesNoViewIsNull()
    {
        var agent = new RemoteAgent(_alice);

        _ = agent.ChoosePriorityActionAsync(NewContext());

        if (agent.PendingPayload != null)
        {
            agent.PendingPayload.YesNoView.Should().BeNull(
                "priority prompts must not carry a yes/no view");
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task YesNoPrompt_PayloadClearedAfterSubmit()
    {
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseYesNoAsync(ctx: null, question: "ok?", sourceCardName: "X");
        agent.PendingPayload.Should().NotBeNull();

        agent.Submit(new ChooseYesNoCommand(Answer: true) { PlayerId = _alice.Id });
        await task;

        agent.PendingPayload.Should().BeNull("payload cleared after submit");
    }

    // ── PromptDto wire-shape tests ───────────────────────────────────────

    [Fact]
    public async Task GameFacade_YesNoPrompt_PromptDtoCarriesYesNoView()
    {
        var facade = GameFacade.Create(
            "Alice", "Bob",
            aliceDeck: Array.Empty<ICard>(),
            bobDeck: Array.Empty<ICard>());

        var prompts = new List<PromptDto>();
        using var _ = facade.SubscribePrompts(prompts.Add);

        var aliceAgent = (RemoteAgent)typeof(GameFacade)
            .GetField("_aliceAgent",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(facade)!;

        var task = aliceAgent.ChooseYesNoAsync(
            ctx: null,
            question: "Pay 2 life for Watery Grave to enter untapped?",
            sourceCardName: "Watery Grave");

        prompts.Should().NotBeEmpty();
        var dto = prompts.Last();
        dto.YesNoView.Should().NotBeNull();
        dto.YesNoView!.SourceCardName.Should().Be("Watery Grave");
        dto.YesNoView!.Question.Should().Contain("Watery Grave");
        dto.ExpectedKinds.Should().Contain("ChooseYesNoCommand");

        // Cleanup
        aliceAgent.Submit(new ChooseYesNoCommand(Answer: false)
        {
            PlayerId = facade.Alice.Id,
        });
        await task;
    }

    // ── JSON serialization sanity ────────────────────────────────────────

    [Fact]
    public void PromptDto_WithYesNoView_SerializesYesNoViewField()
    {
        var dto = new PromptDto(
            GameId: Guid.NewGuid(),
            PlayerId: Guid.NewGuid(),
            ExpectedKinds: new[] { "ChooseYesNoCommand" },
            YesNoView: new YesNoViewDto(
                Question: "Pay 2 life?",
                SourceCardName: "Overgrown Tomb"));

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(dto, opts);

        json.Should().Contain("\"yesNoView\"",
            "yesNoView must serialize as camelCase");
        json.Should().Contain("\"sourceCardName\":\"Overgrown Tomb\"");
        json.Should().Contain("\"question\":\"Pay 2 life?\"");
    }

    [Fact]
    public void PromptDto_WithoutYesNoView_OmitsOrNullsField()
    {
        var dto = new PromptDto(
            GameId: Guid.NewGuid(),
            PlayerId: Guid.NewGuid(),
            ExpectedKinds: new[] { "PassPriorityCommand" });

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(dto, opts);

        if (json.Contains("\"yesNoView\""))
        {
            json.Should().Contain("\"yesNoView\":null");
        }
    }

    [Fact]
    public void ChooseYesNoCommand_RoundTripsThroughGameCommandPolymorphism()
    {
        // The wire discriminator must be "chooseYesNo" — the portal builds
        // commands with that $type string. Verify roundtrip.
        var cmd = new ChooseYesNoCommand(Answer: true) { PlayerId = Guid.NewGuid() };
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize<GameCommand>(cmd, opts);

        json.Should().Contain("\"$type\":\"chooseYesNo\"");
        json.Should().Contain("\"answer\":true");

        var roundtripped = JsonSerializer.Deserialize<GameCommand>(json, opts);
        roundtripped.Should().BeOfType<ChooseYesNoCommand>();
        ((ChooseYesNoCommand)roundtripped!).Answer.Should().BeTrue();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private GameContext NewContext() =>
        new(_alice, new[] { _alice }, _alice, 1,
            Majik.Core.StateMachine.PhaseStateType.PreCombatMain,
            new Majik.Core.Stack.Stack());
}
