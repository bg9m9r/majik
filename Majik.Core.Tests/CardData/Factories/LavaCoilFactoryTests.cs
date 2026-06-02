using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LavaCoilFactory"/> (Guilds of Ravnica).
/// "Lava Coil deals 4 damage to target creature. If that creature would die
/// this turn, exile it instead." ({1}{R} Sorcery.)
/// </summary>
[Trait("Color", "R")]
public class LavaCoilFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature CreatureOnBattlefield(Player owner, int power, int tough)
    {
        var c = new Creature("Tarmogoyf", "{1}{G}", power, tough);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    [Fact]
    public void Identity_SorceryAt1R()
    {
        var card = LavaCoilFactory.Create(_alice);

        card.Name.Should().Be("Lava Coil");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }
    [Fact]
    public void SpellDefinition_HasSingleTargetCreatureRequest()
    {
        var def = LavaCoilFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
    }

    [Fact]
    public void Resolve_DealsFourDamageToCreature()
    {
        var goyf = CreatureOnBattlefield(_bob, 5, 5);
        var def = LavaCoilFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { goyf } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        goyf.Damage.Should().Be(4);
    }

    [Fact]
    public void Resolve_DamagedCreatureDeath_RewrittenToExile()
    {
        var bus = new ReplacementBus();
        var goyf = CreatureOnBattlefield(_bob, 4, 4);

        var def = LavaCoilFactory.BuildSpellDefinition(resolver: x => x, replacements: bus);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { goyf } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        var dying = new ZoneMoveIntent(goyf, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        var result = bus.Apply(dying);
        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Resolve_DoesNotDamagePlayer_NoOpOnNonCreature()
    {
        // Player target is not legal; resolver passes a player through but the
        // effect only acts on creatures.
        var def = LavaCoilFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.LifeTotal.Should().Be(20, "Lava Coil only deals damage to creatures");
    }
}
