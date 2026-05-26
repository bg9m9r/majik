using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="AbolethSpawnFactory"/> (Commander Legends: Battle for
/// Baldur's Gate, {3}{U}{U}).
///
/// Covers shape, Flash + Ward markers, ETB trigger structure, untap +
/// haste-EOT resolve, and target-legality re-check. The control-grant half
/// is a documented v1 gap (no EOT-bounded control swap primitive yet) so
/// tests assert the untap + haste shape that v1 ships.
/// </summary>
public class AbolethSpawnFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void AbolethSpawn_Identity()
    {
        var c = AbolethSpawnFactory.Create(_alice);

        c.Name.Should().Be("Aboleth Spawn");
        c.ManaCost.Should().Be("{3}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Fish).Should().BeTrue();
        c.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AbolethSpawn_HasFlashAndWardKeywordMarkers()
    {
        var c = AbolethSpawnFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash", "Aboleth Spawn prints Flash (CR 702.8)");
        keywords.Should().Contain("Ward", "Aboleth Spawn prints Ward {1} (CR 702.21)");
    }

    [Fact]
    public void AbolethSpawn_HasSingleEtbTrigger_WithOpponentCreatureTarget()
    {
        var c = AbolethSpawnFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "single ETB trigger");

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);
        etb.TargetRequests[0].MinTargets.Should().Be(1);
        etb.TargetRequests[0].MaxTargets.Should().Be(1);
        etb.TargetRequests[0].Description.Should().Contain("creature");
        etb.TargetRequests[0].Description.Should().Contain("opponent");
    }

    [Fact]
    public void AbolethSpawn_Etb_UntapsAndGrantsHasteToOpponentCreature()
    {
        var spawn = AbolethSpawnFactory.Create(_alice);
        spawn.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(spawn);

        // Bob controls a tapped creature; Aboleth Spawn steals (in v1 just
        // untaps + grants haste — the control-grant half is deferred).
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 3, 4);
        goyf.SetOwner(_bob);
        goyf.SetController(_bob);
        goyf.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(goyf);
        goyf.Tap();
        goyf.IsTapped.Should().BeTrue();

        // Wire ActiveEffects so the haste-EOT grant can register.
        goyf.ActiveEffects = new ContinuousEffectsService();

        var etb = spawn.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { goyf },
        });
        foreach (var e in etb.Effects) e.Execute();

        goyf.IsTapped.Should().BeFalse(
            "ETB untaps the targeted opponent creature (CR 701.20)");

        CombatAbilities.HasHaste(goyf).Should().BeTrue(
            "ETB grants haste until end of turn (CR 702.10 / CR 514.2)");
    }

    [Fact]
    public void AbolethSpawn_Etb_RejectsOwnCreatureAtResolution()
    {
        // CR 109.1 — the printed "opponent controls" rider is re-checked at
        // resolution. If somehow Alice's own creature ends up as the chosen
        // target (e.g. the target was opponent-controlled at trigger time
        // but control changed), the effect no-ops cleanly.
        var spawn = AbolethSpawnFactory.Create(_alice);
        spawn.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(spawn);

        var llanowar = new Creature("Llanowar Elves", "{G}", 1, 1);
        llanowar.SetOwner(_alice);
        llanowar.SetController(_alice);
        llanowar.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(llanowar);
        llanowar.Tap();
        llanowar.ActiveEffects = new ContinuousEffectsService();

        var etb = spawn.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { llanowar },
        });
        foreach (var e in etb.Effects) e.Execute();

        llanowar.IsTapped.Should().BeTrue(
            "same-controller target fails the CR 109.1 re-check → no untap");
        CombatAbilities.HasHaste(llanowar).Should().BeFalse(
            "no haste grant on a same-controller target");
    }

    [Fact]
    public void AbolethSpawn_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Aboleth Spawn", _alice);

        c.Should().NotBeNull();
        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Aboleth Spawn");
        ((Creature)c).Power.Should().Be(4);
        ((Creature)c).Toughness.Should().Be(3);
        c.HasSubtype(CardSubtype.Fish).Should().BeTrue();
        c.HasSubtype(CardSubtype.Horror).Should().BeTrue();
    }
}
