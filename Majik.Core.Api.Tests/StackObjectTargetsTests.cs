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
/// CR 601.2c / 603.3d / 115.1 — each stack object's chosen targets are part of
/// the public game state: <see cref="StackObjectDto.Targets"/> lists them in
/// announcement order, as Card / Player / Spell references. CR 706 — a target
/// card in a zone hidden from the viewer is shown with an empty id.
/// </summary>
public class StackObjectTargetsTests
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
    public void SpellAndAbilityTargets_AreListedInOrder_AsCardPlayerAndSpellRefs()
    {
        var stack = new Majik.Core.Stack.Stack();
        var bear = Put(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob, ZoneType.Battlefield);
        var charm = Put(new Instant("Test Charm", "{R}"), _alice, ZoneType.Stack);
        var spell = new Spell(charm, _alice);
        spell.AnnouncedTargets = new IReadOnlyList<object>[] { new object[] { bear }, new object[] { _bob } };
        stack.Push(spell);

        var source = Put(new Creature("Spellstutter", "{1}{U}", 1, 1), _bob, ZoneType.Battlefield);
        var trigger = new TriggeredAbility(source, _bob, Triggers.OnEnterBattlefieldSelf(source));
        trigger.SetChosenTargets(new[] { new object[] { spell } });
        stack.Push(trigger);

        var untargeted = new TriggeredAbility(source, _bob, Triggers.OnEnterBattlefieldSelf(source));
        stack.Push(untargeted);

        var dto = Snap(stack, _alice).Stack;

        dto.Should().HaveCount(3);
        dto[0].Id.Should().Be(spell.Id, "the wire order is bottom-to-top");
        dto[0].Targets.Should().Equal(
            new TargetRefDto(bear.InstanceId, "Card"),
            new TargetRefDto(_bob.Id, "Player"));
        dto[1].Targets.Should().Equal(new TargetRefDto(spell.Id, "Spell"));
        dto[2].Targets.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ATargetCardInAZoneHiddenFromTheViewer_HasAnEmptyId()
    {
        var stack = new Majik.Core.Stack.Stack();
        var secret = Put(new Instant("Secret", "{U}"), _bob, ZoneType.Hand);
        var card = Put(new Sorcery("Peek", "{U}"), _alice, ZoneType.Stack);
        var spell = new Spell(card, _alice);
        spell.AnnouncedTargets = new IReadOnlyList<object>[] { new object[] { secret } };
        stack.Push(spell);

        Snap(stack, _alice).Stack[0].Targets.Should().Equal(new TargetRefDto(Guid.Empty, "Card"));
        Snap(stack, _bob).Stack[0].Targets.Should().Equal(new TargetRefDto(secret.InstanceId, "Card"));
        // A null viewer is the all-revealed view.
        Snap(stack, null).Stack[0].Targets.Should().Equal(new TargetRefDto(secret.InstanceId, "Card"));
    }
}
