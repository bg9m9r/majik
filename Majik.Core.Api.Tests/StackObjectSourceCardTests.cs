using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Players;
using Spell = Majik.Core.Spells.Spell;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// <see cref="StackObjectDto.SourceCardId"/> — the card a stack object comes
/// from, so a client can link a stack entry to the card it saw in a hand or on
/// the battlefield (Card.InstanceId is kept across zone moves). A spell: the
/// card being cast (CR 601.2a). An ability: its source (CR 113.7). Null when
/// that card is in a zone hidden from the viewer (CR 706).
/// </summary>
public class StackObjectSourceCardTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private T Put<T>(T card, Player owner, ZoneType zone) where T : Card
    {
        card.SetOwner(owner); card.SetController(owner);
        owner.Zones.GetZone(zone).AddCard(card); card.SetZone(zone);
        return card;
    }

    private GameStateDto Snap(Majik.Core.Stack.Stack stack, Player? viewer) =>
        StateSnapshotter.Snapshot(Guid.NewGuid(), 1, null, _alice, new[] { _alice, _bob }, stack, viewer);

    [Fact]
    public void ASpell_CarriesTheIdOfTheCardBeingCast_TheIdItHadInHand()
    {
        var bolt = Put(new Instant("Lightning Bolt", "{R}"), _alice, ZoneType.Hand);
        var idInHand = Snap(new Majik.Core.Stack.Stack(), _alice).Players.Single(p => p.Id == _alice.Id).Hand.Cards.Single().InstanceId;

        _alice.Zones.Hand.RemoveCard(bolt);
        _alice.Zones.Stack.AddCard(bolt); bolt.SetZone(ZoneType.Stack);
        var stack = new Majik.Core.Stack.Stack();
        stack.Push(new Spell(bolt, _alice));

        foreach (var viewer in new[] { _alice, _bob, null })
            Snap(stack, viewer).Stack.Single().SourceCardId.Should().Be(idInHand);
    }

    [Fact]
    public void AnAbility_CarriesItsSourceCardId()
    {
        var pinger = Put(new Creature("Pinger", "{R}", 1, 1), _bob, ZoneType.Battlefield);
        var stack = new Majik.Core.Stack.Stack();
        stack.Push(new TriggeredAbility(pinger, _bob, Triggers.OnEnterBattlefieldSelf(pinger)));
        stack.Push(new ActivatedAbility(pinger, _bob));

        var dto = Snap(stack, _alice).Stack;
        dto.Should().OnlyContain(s => s.SourceCardId == pinger.InstanceId);
    }

    [Fact]
    public void AnAbilityWhoseSourceIsInAHiddenZone_HidesTheIdFromOtherPlayers()
    {
        var cycler = Put(new Creature("Secret Cycler", "{1}", 1, 1), _bob, ZoneType.Hand);
        var stack = new Majik.Core.Stack.Stack();
        stack.Push(new TriggeredAbility(cycler, _bob, Triggers.OnEnterBattlefieldSelf(cycler)));

        Snap(stack, _alice).Stack.Single().SourceCardId.Should().BeNull();
        Snap(stack, _bob).Stack.Single().SourceCardId.Should().Be(cycler.InstanceId);
        Snap(stack, null).Stack.Single().SourceCardId.Should().Be(cycler.InstanceId);
    }
}
