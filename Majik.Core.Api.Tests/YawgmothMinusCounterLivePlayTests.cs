using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Core.Api.Tests;

/// <summary>
/// LIVE-PLAY integration coverage for Yawgmoth, Thran Physician's first
/// activated ability EFFECT — "Put a -1/-1 counter on UP TO ONE target
/// creature, then draw a card." (CR 115.1b "up to one" — an OPTIONAL target).
///
/// <para>Reported live-play bug: after paying the cost (Pay 1 life, Sacrifice
/// another creature), the player was never prompted to choose WHERE to put the
/// -1/-1 counter — the optional target was silently skipped (the counter half
/// was DEFERRED in the factory). These tests drive the real
/// <see cref="GameFacade"/> activation dispatch (the same path the server runs)
/// and assert the controller is prompted for the optional target, that a chosen
/// creature gets the -1/-1 counter, that DECLINING (choosing none) places no
/// counter, and that the draw happens either way.</para>
/// </summary>
public sealed class YawgmothMinusCounterLivePlayTests
{
    private readonly ITestOutputHelper _out;

    public YawgmothMinusCounterLivePlayTests(ITestOutputHelper output) => _out = output;

    private static Creature SeedCreature(Player owner, string name, int power = 1, int toughness = 1)
    {
        var c = new Creature(name, "{G}", power, toughness);
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

    /// <summary>
    /// Build the standard board: Yawgmoth (not summoning sick) + a fodder
    /// creature to sacrifice + a second creature so the sacrifice is a genuine
    /// CHOICE + a victim creature to receive the -1/-1 counter + a library card
    /// so the draw is observable.
    /// </summary>
    private static (GameFacade facade, Creature yawgmoth, ActivatedAbility ability,
        Creature fodder, Creature victim, int startLife, int startHand)
        BuildBoard(out Player alice, out Player bob)
    {
        var facade = GameFacade.Create("Alice", "Bob",
            Array.Empty<ICard>(), Array.Empty<ICard>());
        alice = facade.Alice;
        bob = facade.Bob;

        var yawgmoth = YawgmothFactory.Create(alice);
        yawgmoth.ClearSummoningSickness();
        alice.Zones.Battlefield.AddCard(yawgmoth);
        yawgmoth.SetZone(ZoneType.Battlefield);

        var fodder = SeedCreature(alice, "Dryad Arbor");
        SeedCreature(alice, "Memnite"); // second sacrificeable → genuine choice
        // A juicy 3-toughness victim (either player's creature is a legal target).
        var victim = SeedCreature(bob, "Grizzly Bears", 2, 3);
        // Wire the CR 613 layer executor so the victim's EFFECTIVE P/T reflects
        // the -1/-1 counter (a manually-seeded creature otherwise has a null
        // ActiveEffects and falls back to BaseToughness). In a real game every
        // battlefield permanent already carries this link.
        victim.ActiveEffects = facade.ContinuousEffects;

        var libraryCard = new Creature("Llanowar Elves", "{G}", 1, 1);
        libraryCard.SetOwner(alice);
        alice.Zones.Library.AddCard(libraryCard);
        libraryCard.SetZone(ZoneType.Library);

        var ability = yawgmoth.Abilities.OfType<ActivatedAbility>().First();
        return (facade, yawgmoth, ability, fodder, victim,
            alice.LifeTotal, alice.Zones.Hand.GetCards().Count());
    }

    private static async Task PassBoth(GameFacade facade)
    {
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = facade.Alice.Id });
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = facade.Bob.Id });
    }

    /// <summary>
    /// Answer every prompt that arrives while activating Yawgmoth, in arrival
    /// order, until the activation has settled (no more prompts): pick the
    /// fodder creature for the "Sacrifice another creature" cost
    /// (<see cref="ChoiceCommand"/>), and the supplied counter pick for the
    /// "up to one target creature" prompt (<see cref="ChooseTargetsCommand"/>;
    /// <paramref name="counterPick"/> = null ⇒ decline). Tracks whether a
    /// counter (target) prompt was ever surfaced.
    /// </summary>
    private static async Task<bool> DrainActivation(
        GameFacade facade,
        System.Threading.Channels.Channel<PromptDto> channel,
        Guid sacrificeId, Guid? counterPick, Guid playerId)
    {
        var sawTargetPrompt = false;
        for (var step = 0; step < 30; step++)
        {
            var read = channel.Reader.WaitToReadAsync().AsTask();
            if (await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(3))) != read) break;
            if (!await read) break;
            if (!channel.Reader.TryRead(out var prompt)) continue;

            GameCommand cmd;
            if (prompt.ExpectedKinds.Contains(nameof(ChooseTargetsCommand)))
            {
                sawTargetPrompt = true;
                cmd = new ChooseTargetsCommand(
                    counterPick is { } id ? new[] { id } : Array.Empty<Guid>());
            }
            else if (prompt.ExpectedKinds.Contains(nameof(ChoiceCommand)))
            {
                cmd = new ChoiceCommand(ChoiceKind.PickOne.ToString(), new[] { sacrificeId });
            }
            else
            {
                break; // back to priority — activation has settled
            }

            await facade.SubmitAsync(cmd with { PlayerId = playerId });
        }
        return sawTargetPrompt;
    }

    // ── Case 1: choose a creature → counter placed + draw ───────────────────

    [Fact]
    public async Task Yawgmoth_Activate_PromptsForCounterTarget_PlacesCounter_AndDraws()
    {
        var (facade, yawgmoth, ability, fodder, victim, startLife, startHand) =
            BuildBoard(out var alice, out _);

        await facade.StartAsync();
        facade.GetState().Phase.Should().Be(StepStateType.PreCombatMain.ToString());

        var (prompts, channel) = SubscribePrompts(facade);

        await facade.SubmitAsync(new ActivateAbilityCommand(yawgmoth.InstanceId, ability.Id)
        {
            PlayerId = alice.Id,
        });

        // Answer prompts as they arrive: sacrifice = fodder, counter = victim.
        var sawTargetPrompt = await DrainActivation(
            facade, channel, fodder.InstanceId, victim.InstanceId, alice.Id);

        _out.WriteLine("prompt kinds: " + string.Join(" | ",
            prompts.Select(p => string.Join(",", p.ExpectedKinds))));

        // THE BUG: the controller must be prompted to choose the "-1/-1 counter
        // on up to one target creature" target (CR 115.1b).
        sawTargetPrompt.Should().BeTrue(
            "Yawgmoth's ability must prompt the controller for the optional " +
            "'-1/-1 counter on up to one target creature' (CR 115.1b)");

        // Cost paid.
        alice.LifeTotal.Should().Be(startLife - 1);
        alice.Zones.Graveyard.GetCards().Should().Contain((ICard)fodder);

        await PassBoth(facade);

        // Counter placed on the chosen creature; toughness dropped 3 → 2.
        victim.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1,
            "the chosen creature receives a -1/-1 counter");
        victim.Toughness.Should().Be(2, "a -1/-1 counter lowers effective toughness");

        // Draw still happens.
        alice.Zones.Hand.GetCards().Count().Should().Be(startHand + 1,
            "Yawgmoth draws a card after the counter step");
    }

    // ── Case 2: decline the optional target → no counter, still draws ───────

    [Fact]
    public async Task Yawgmoth_Activate_DeclineOptionalTarget_NoCounter_StillDraws()
    {
        var (facade, yawgmoth, ability, fodder, victim, startLife, startHand) =
            BuildBoard(out var alice, out _);

        await facade.StartAsync();

        var (_, channel) = SubscribePrompts(facade);

        await facade.SubmitAsync(new ActivateAbilityCommand(yawgmoth.InstanceId, ability.Id)
        {
            PlayerId = alice.Id,
        });

        // Sacrifice = fodder; DECLINE the optional counter target (null pick).
        var sawTargetPrompt = await DrainActivation(
            facade, channel, fodder.InstanceId, counterPick: null, alice.Id);

        sawTargetPrompt.Should().BeTrue(
            "'up to one' still prompts so the player can decline");

        alice.LifeTotal.Should().Be(startLife - 1);

        await PassBoth(facade);

        victim.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0,
            "declining the optional target places no counter");
        victim.Toughness.Should().Be(3, "no counter → toughness unchanged");

        alice.Zones.Hand.GetCards().Count().Should().Be(startHand + 1,
            "the draw happens even when the optional target is declined");
    }
}
