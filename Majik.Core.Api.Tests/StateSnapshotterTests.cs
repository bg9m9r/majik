using System.Text.Json;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

public class StateSnapshotterTests
{
    private readonly EventBus _bus = new();

    [Fact]
    public void Snapshot_NewGame_NoCycles_SerializesCleanly()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var stack = new Majik.Core.Stack.Stack(_bus);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice,
            new[] { alice, bob }, stack);

        dto.Players.Should().HaveCount(2);
        dto.ActivePlayerId.Should().Be(alice.Id);
        dto.Phase.Should().Be("PreCombatMain");
        dto.Stack.Should().BeEmpty();

        // Must round-trip — no cycles.
        var json = JsonSerializer.Serialize(dto);
        var back = JsonSerializer.Deserialize<GameStateDto>(json);
        back.Should().BeEquivalentTo(dto);
    }

    [Fact]
    public void Snapshot_CardInZone_AppearsInZoneDto()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice };
        alice.Zones.Library.AddCard(bear);
        var zones = new ZoneService(_bus);
        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: alice);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice,
            new[] { alice }, new Majik.Core.Stack.Stack(_bus));

        var aliceDto = dto.Players.Single(p => p.Id == alice.Id);
        aliceDto.Battlefield.Cards.Should().ContainSingle()
            .Which.Name.Should().Be("Bear");
        aliceDto.Battlefield.Cards[0].Power.Should().Be(2);
        aliceDto.Battlefield.Cards[0].InstanceId.Should().Be(bear.InstanceId);
    }

    [Fact]
    public void Snapshot_StackHasSpell_DescribedInDto()
    {
        var alice = new Player("Alice", 20);
        var bolt = new Instant("Bolt", "R") { Owner = alice };
        var stack = new Majik.Core.Stack.Stack(_bus);
        var spell = new Majik.Core.Spells.Spell(bolt, alice);
        stack.Push(spell);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice, new[] { alice }, stack);

        dto.Stack.Should().ContainSingle();
        dto.Stack[0].Kind.Should().Be("Spell");
        dto.Stack[0].ControllerId.Should().Be(alice.Id);
        dto.Stack[0].Description.Should().Be("Bolt");
    }

    [Fact]
    public void Snapshot_ActivatedAbilityOnCard_AbilityDtoExposesId()
    {
        // A permanent with one IActivatedAbility — the DTO must carry the
        // ability's stable Guid so clients can reference it in
        // ActivateAbilityCommand(permanentInstanceId, abilityId).
        var alice = new Player("Alice", 20);
        var bear = new Creature("Pinger", "2U", 1, 1) { Owner = alice };
        alice.Zones.Library.AddCard(bear);
        var zones = new ZoneService(_bus);
        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: alice);

        var ability = new ActivatedAbility(bear, alice);
        bear.AddAbility(ability);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice,
            new[] { alice }, new Majik.Core.Stack.Stack(_bus));

        var cardDto = dto.Players.Single(p => p.Id == alice.Id)
                         .Battlefield.Cards
                         .Single(c => c.InstanceId == bear.InstanceId);

        var abilityDto = cardDto.Abilities.Should().ContainSingle(a => a.Kind == "Activated")
                                .Subject;
        abilityDto.Id.Should().Be(ability.Id);
    }

    [Fact]
    public void Snapshot_StaticAbilityOnCard_AbilityDtoIdIsNull()
    {
        // Clients don't activate static abilities, so Id should remain null.
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice };
        alice.Zones.Library.AddCard(bear);
        var zones = new ZoneService(_bus);
        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: alice);

        bear.AddAbility(new KeywordAbility("Flying", bear, alice));

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice,
            new[] { alice }, new Majik.Core.Stack.Stack(_bus));

        var cardDto = dto.Players.Single(p => p.Id == alice.Id)
                         .Battlefield.Cards
                         .Single(c => c.InstanceId == bear.InstanceId);

        var abilityDto = cardDto.Abilities.Should().ContainSingle(a => a.Kind == "Static")
                                .Subject;
        abilityDto.Id.Should().BeNull();
    }
}
