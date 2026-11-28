using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// CR 601.2a — casting a spell first moves it to the stack; its targets, modes,
/// X and costs are chosen while it is there. The published snapshot must show
/// it on the stack (and not in hand) for those prompts, under the id it keeps
/// once the cast completes, for both viewers.
/// </summary>
public class SpellBeingCastSnapshotTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    [Fact]
    public async Task OnTheTargetsPrompt_TheSpellIsTheTopStackObject_NotInHand_AndKeepsItsIdOnceCast()
    {
        var aliceDeck = Enumerable.Range(0, 20).Select(_ => (ICard)new Land("Forest")).ToList();
        var bobDeck = Enumerable.Range(0, 20).Select(_ => (ICard)new Land("Forest")).ToList();
        var facade = GameFacade.Create("Alice", "Bob", aliceDeck, bobDeck, cardRepo: Repo);
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(facade.Alice); bolt.SetController(facade.Alice);
        facade.Alice.Zones.Hand.AddCard(bolt); bolt.SetZone(ZoneType.Hand);
        var mountain = new Land("Mountain", supertypes: new[] { CardSupertype.Basic }, subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(facade.Alice); mountain.ChangeController(facade.Alice);
        mountain.SetZone(ZoneType.Battlefield);
        OracleManaBinder.BindBasicLandMana(mountain, facade.Alice);
        facade.Alice.Zones.Battlefield.AddCard(mountain);

        GameStateDto? aliceView = null, bobView = null;
        GameStateDto? afterCast = null;
        var cast = false;
        var targeted = false;
        using var sub = facade.SubscribePrompts(p =>
        {
            GameCommand? answer;
            if (p.ExpectedKinds.Contains(nameof(MulliganCommand))) answer = new MulliganCommand(Keep: true);
            else if (p.ExpectedKinds.Contains(nameof(ChooseTargetsCommand)))
            {
                aliceView = facade.GetStateFor(facade.Alice.Id);
                bobView = facade.GetStateFor(facade.Bob.Id);
                targeted = true;
                answer = new ChooseTargetsCommand(new[] { facade.Bob.Id });
            }
            else if (p.ExpectedKinds.Contains(nameof(ChooseManaCommand))) answer = new ChooseManaCommand(new[] { mountain.InstanceId });
            else if (p.PlayerId == facade.Alice.Id && !cast && p.ExpectedKinds.Contains(nameof(CastSpellCommand)))
            {
                cast = true;
                answer = new CastSpellCommand(bolt.InstanceId, Array.Empty<Guid>(), null, null);
            }
            else if (p.ExpectedKinds.Contains(nameof(PassPriorityCommand)))
            {
                if (targeted && afterCast is null) afterCast = facade.GetStateFor(facade.Alice.Id);
                answer = new PassPriorityCommand();
            }
            else return;
            _ = facade.SubmitAsync(answer with { PlayerId = p.PlayerId });
        });

        await facade.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 1, rng: new GameRandom(1));
        await facade.FullGameTask!.WaitAsync(TimeSpan.FromSeconds(30));

        targeted.Should().BeTrue("the cast reached its targets prompt");
        foreach (var view in new[] { aliceView!, bobView! })
        {
            var top = view.Stack.Should().ContainSingle().Subject;
            top.IsBeingCast.Should().BeTrue();
            top.Kind.Should().Be("Spell");
            top.ControllerId.Should().Be(facade.Alice.Id);
            top.Description.Should().Contain("Lightning Bolt");
            view.Players.Single(pl => pl.Id == facade.Alice.Id).Hand.Cards
                .Should().NotContain(c => c.InstanceId == bolt.InstanceId, "CR 601.2a — the spell left the hand for the stack");
        }
        var beingCastId = aliceView!.Stack[0].Id;
        bobView!.Stack[0].Id.Should().Be(beingCastId);

        var after = afterCast!.Stack.Should().ContainSingle().Subject;
        after.Id.Should().Be(beingCastId, "one continuous object from cast start to the stack");
        after.IsBeingCast.Should().BeFalse();
        after.Targets.Should().Equal(new TargetRefDto(facade.Bob.Id, "Player"));
    }
}