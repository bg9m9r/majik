using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// CR 601.2a — casting a spell moves its card to the stack, where it becomes
/// a stack object. The CardMoved payload of that move carries the stack entry
/// id the spell has (<see cref="StackObjectDto.Id"/>), so a client can animate
/// the card from the hand into the stack entry.
/// </summary>
public class CardMovedToStackEntryIdTests
{
    [Fact]
    public async Task CastingFromHand_TheMoveToTheStackNamesTheSpellsStackEntry()
    {
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        var facade = GameFacade.Create("Alice", "Bob", new ICard[] { bear }, Array.Empty<ICard>());
        facade.Alice.Zones.Battlefield.AddCard(Forest(facade.Alice));
        facade.Alice.Zones.Battlefield.AddCard(Forest(facade.Alice));
        facade.Alice.Zones.Library.RemoveCard(bear);
        facade.Alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        var moves = new List<System.Text.Json.JsonElement>();
        using var sub = facade.Subscribe(e =>
        {
            if (e.Type == nameof(Majik.Core.Events.CardMovedEvent) && e.Payload.GetProperty("to").GetString() == "Stack")
                lock (moves) moves.Add(e.Payload.Clone());
        });

        await facade.StartAsync();
        await facade.SubmitAsync(new CastSpellCommand(bear.InstanceId, Array.Empty<Guid>(), null, null)
            { PlayerId = facade.Alice.Id });
        await facade.SubmitAsync(new ChooseManaCommand(Array.Empty<Guid>()) { PlayerId = facade.Alice.Id });

        var onStack = facade.GetState().Stack.Single();
        onStack.SourceCardId.Should().Be(bear.InstanceId);
        var move = moves.Should().ContainSingle().Subject;
        move.GetProperty("cardId").GetGuid().Should().Be(bear.InstanceId);
        move.GetProperty("stackEntryId").GetGuid().Should().Be(onStack.Id,
            "it is the id of the stack entry the card became");
    }

    [Fact]
    public void AMoveToAnotherZone_HasNoStackEntryId()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(alice); bear.SetController(alice); bear.SetZone(ZoneType.Graveyard);
        var payload = EventPayloadBuilder.Build(new Majik.Core.Events.CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard));
        payload.TryGetProperty("stackEntryId", out _).Should().BeFalse();
    }

    private static Land Forest(Player controller)
    {
        var land = new Land("Forest", supertypes: new[] { CardSupertype.Basic }, subtypes: new[] { CardSubtype.Forest });
        land.SetOwner(controller);
        land.ChangeController(controller);
        land.SetZone(ZoneType.Battlefield);
        OracleManaBinder.BindBasicLandMana(land, controller);
        return land;
    }
}
