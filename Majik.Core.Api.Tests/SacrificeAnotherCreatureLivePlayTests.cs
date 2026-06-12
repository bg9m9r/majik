using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Core.Api.Tests;

/// <summary>
/// LIVE-PLAY integration coverage for "Sacrifice another creature" appearing
/// both as an activated-ability COST (Yawgmoth, Thran Physician) and inside an
/// ability EFFECT (Grist, the Hunger Tide −2). Drives the real
/// <see cref="GameFacade"/> single-round priority loop (the same dispatch path
/// the server runs) with a manually-seeded battlefield, and a scripted
/// <see cref="RemoteAgent"/>.
///
/// <para>Reported live-play bugs (one shared root cause): choosing WHICH
/// creature to "sacrifice another creature" is never surfaced to the player.
/// For Yawgmoth it is a cost (so activation is rejected / silently auto-picks);
/// for Grist it is part of the effect (so the sacrifice silently no-ops and the
/// "if you do" destroy never happens).</para>
/// </summary>
public sealed class SacrificeAnotherCreatureLivePlayTests
{
    private readonly ITestOutputHelper _out;

    public SacrificeAnotherCreatureLivePlayTests(ITestOutputHelper output) => _out = output;

    // ── shared scaffolding ──────────────────────────────────────────────────

    /// <summary>A plain vanilla creature on a player's battlefield, not
    /// summoning sick.</summary>
    private static Creature SeedCreature(Player owner, string name)
    {
        var c = new Creature(name, "{G}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.ClearSummoningSickness();
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static (List<PromptDto> prompts, System.Threading.Channels.Channel<PromptDto> channel)
        SubscribePrompts(GameFacade facade)
    {
        var prompts = new List<PromptDto>();
        var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        facade.SubscribePrompts(p =>
        {
            prompts.Add(p);
            channel.Writer.TryWrite(p);
        });
        return (prompts, channel);
    }

    // ── Yawgmoth: "Sacrifice another creature" as a COST ────────────────────

    [Fact]
    public async Task Yawgmoth_Activate_PromptsForSacrifice_PaysCost_AndDraws()
    {
        var facade = GameFacade.Create("Alice", "Bob",
            Array.Empty<ICard>(), Array.Empty<ICard>());

        var alice = facade.Alice;

        // Build Yawgmoth via the production named factory + a second creature to
        // sacrifice. Yawgmoth must not be summoning sick (its ability has no tap
        // cost, but PriorityKinds narrows on summoning sickness for creatures).
        var yawgmoth = YawgmothFactory.Create(alice);
        yawgmoth.ClearSummoningSickness();
        alice.Zones.Battlefield.AddCard(yawgmoth);
        yawgmoth.SetZone(ZoneType.Battlefield);

        var fodder = SeedCreature(alice, "Dryad Arbor");
        // A second sacrificeable creature so a genuine CHOICE exists (with only
        // one eligible creature the engine auto-picks — no prompt needed).
        SeedCreature(alice, "Memnite");

        // A card in library so the draw is observable.
        var libraryCard = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        libraryCard.SetOwner(alice);
        alice.Zones.Library.AddCard(libraryCard);
        libraryCard.SetZone(ZoneType.Library);

        var ability = yawgmoth.Abilities.OfType<ActivatedAbility>().First();
        var startingLife = alice.LifeTotal;
        var startingHand = alice.Zones.Hand.GetCards().Count();

        await facade.StartAsync();
        facade.GetState().Phase.Should().Be(StepStateType.PreCombatMain.ToString());

        var (prompts, _) = SubscribePrompts(facade);

        // THE BUG (cost path): submitting the activate command is rejected
        // (400 invalid-command → InvalidOperationException through SubmitAsync)
        // OR auto-picks the sacrifice with no prompt. We assert the player IS
        // prompted to choose the creature to sacrifice, and the cost is paid.
        Exception? submitError = null;
        try
        {
            await facade.SubmitAsync(new ActivateAbilityCommand(yawgmoth.InstanceId, ability.Id)
            {
                PlayerId = alice.Id,
            });
        }
        catch (Exception ex)
        {
            submitError = ex;
            _out.WriteLine($"ACTIVATE REJECTED: {ex.Message}");
        }

        _out.WriteLine("prompt kinds: " + string.Join(" | ",
            prompts.Select(p => string.Join(",", p.ExpectedKinds))));

        submitError.Should().BeNull("activating Yawgmoth at instant speed must be accepted");

        // The player must have been prompted to CHOOSE which creature to
        // sacrifice (a choice/target prompt offering the OTHER creature).
        var sacPrompt = prompts.FirstOrDefault(p =>
            p.ExpectedKinds.Contains(nameof(ChooseTargetsCommand))
            || p.ExpectedKinds.Contains(nameof(ChoiceCommand)));
        sacPrompt.Should().NotBeNull(
            "Yawgmoth's 'Sacrifice another creature' cost must prompt the controller " +
            "to choose which creature to sacrifice (CR 700.6 — the controller chooses)");

        // Respond: choose the fodder creature.
        await RespondSacrificeChoice(facade, sacPrompt!, fodder.InstanceId, alice.Id);

        // Cost paid: 1 life, fodder sacrificed, ability on the stack.
        alice.LifeTotal.Should().Be(startingLife - 1, "Yawgmoth's ability costs 1 life");
        alice.Zones.Battlefield.GetCards().Should().NotContain(fodder,
            "the chosen creature must be sacrificed as a cost");
        alice.Zones.Graveyard.GetCards().Should().Contain((ICard)fodder);

        // Both players pass → the ability resolves: draw a card.
        await PassBoth(facade);

        alice.Zones.Hand.GetCards().Count().Should().Be(startingHand + 1,
            "Yawgmoth's ability draws a card on resolution");
    }

    // ── Grist −2: "Sacrifice another creature" inside the EFFECT ────────────

    [Fact]
    public async Task GristMinus2_PromptsForSacrifice_ThenDestroysTarget()
    {
        var facade = GameFacade.Create("Alice", "Bob",
            Array.Empty<ICard>(), Array.Empty<ICard>());

        var alice = facade.Alice;
        var bob = facade.Bob;

        // Grist on Alice's battlefield (a planeswalker), a creature she can
        // sacrifice, and a Bob creature to destroy.
        var grist = GristFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(grist);
        grist.SetZone(ZoneType.Battlefield);

        var fodder = SeedCreature(alice, "Llanowar Elves");
        // A second of Alice's creatures so a genuine sacrifice CHOICE exists.
        SeedCreature(alice, "Memnite");
        var bobCreature = SeedCreature(bob, "Grizzly Bears");

        var minus2 = grist.Abilities.OfType<LoyaltyAbility>()
            .First(a => a.LoyaltyChange == -2);

        await facade.StartAsync();
        facade.GetState().Phase.Should().Be(StepStateType.PreCombatMain.ToString());

        var (prompts, channel) = SubscribePrompts(facade);

        await facade.SubmitAsync(new ActivateLoyaltyAbilityCommand(grist.InstanceId, minus2.Id)
        {
            PlayerId = alice.Id,
        });

        // Resolve the loyalty ability: answer any prompts (destroy target +
        // sacrifice choice) as they arrive, passing priority otherwise, until
        // the stack is empty (the −2 has resolved).
        await DrainPrompts(facade, channel, fodder.InstanceId, bobCreature.InstanceId);

        _out.WriteLine("prompt kinds: " + string.Join(" | ",
            prompts.Select(p => $"{(p.PlayerId == alice.Id ? "A" : "B")}:{string.Join(",", p.ExpectedKinds)}")));

        // THE BUG: the player is never prompted to choose which creature to
        // sacrifice, so the sacrifice no-ops and the "if you do" destroy is
        // skipped. Assert both halves happen.
        alice.Zones.Battlefield.GetCards().Should().NotContain(fodder,
            "Grist's −2 must sacrifice the chosen creature");
        bob.Zones.Battlefield.GetCards().Should().NotContain(bobCreature,
            "after the sacrifice, Grist's −2 destroys the chosen target creature");
        bob.Zones.Graveyard.GetCards().Should().Contain((ICard)bobCreature);
    }

    // ── prompt helpers ──────────────────────────────────────────────────────

    private static async Task RespondSacrificeChoice(
        GameFacade facade, PromptDto prompt, Guid pickInstanceId, Guid playerId)
    {
        GameCommand cmd = prompt.ExpectedKinds.Contains(nameof(ChooseTargetsCommand))
            ? new ChooseTargetsCommand(new[] { pickInstanceId })
            : new ChoiceCommand(ChoiceKind.PickOne.ToString(), new[] { pickInstanceId });
        cmd = cmd with { PlayerId = playerId };
        await facade.SubmitAsync(cmd);
    }

    private static async Task PassBoth(GameFacade facade)
    {
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = facade.Alice.Id });
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = facade.Bob.Id });
    }

    /// <summary>Answer every prompt that arrives while resolving Grist's −2:
    /// pick the fodder creature for any sacrifice choice, pick Bob's creature
    /// for any destroy-target choice, and pass priority otherwise — until the
    /// stack empties (the −2 has resolved).</summary>
    private static async Task DrainPrompts(
        GameFacade facade,
        System.Threading.Channels.Channel<PromptDto> channel,
        Guid sacrificeId, Guid destroyId)
    {
        for (var step = 0; step < 60; step++)
        {
            var read = channel.Reader.WaitToReadAsync().AsTask();
            if (await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(3))) != read) break;
            if (!await read) break;
            if (!channel.Reader.TryRead(out var prompt)) continue;

            var cmd = BuildResponse(prompt, sacrificeId, destroyId);
            cmd = cmd with { PlayerId = prompt.PlayerId };
            await facade.SubmitAsync(cmd);
        }
    }

    private static GameCommand BuildResponse(PromptDto prompt, Guid sacrificeId, Guid destroyId)
    {
        var kinds = prompt.ExpectedKinds;
        if (kinds.Contains(nameof(ChooseTargetsCommand)))
        {
            // Destroy target — a TargetRequest.
            return new ChooseTargetsCommand(new[] { destroyId });
        }
        if (kinds.Contains(nameof(ChoiceCommand)))
        {
            // Sacrifice choice — a ChooseAsync PickOne over the controller's
            // own creatures.
            return new ChoiceCommand(ChoiceKind.PickOne.ToString(), new[] { sacrificeId });
        }
        return new PassPriorityCommand();
    }
}
