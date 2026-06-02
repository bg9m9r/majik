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
/// Unit tests for <see cref="PillarOfFlameFactory"/> (Avacyn Restored).
/// "Pillar of Flame deals 2 damage to any target. If a creature dealt damage
/// this way would die this turn, exile it instead." ({R} Sorcery.)
/// </summary>
[Trait("Color", "R")]
public class PillarOfFlameFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature CreatureOnBattlefield(Player owner, int power, int tough)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", power, tough);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    [Fact]
    public void Identity_SorceryAtR()
    {
        var card = PillarOfFlameFactory.Create(_alice);

        card.Name.Should().Be("Pillar of Flame");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
    }
    [Fact]
    public void SpellDefinition_HasSingleAnyTargetRequest()
    {
        var def = PillarOfFlameFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("any target");
    }

    [Fact]
    public void Resolve_DealsTwoDamageToPlayer()
    {
        var def = PillarOfFlameFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.LifeTotal.Should().Be(18);
    }

    [Fact]
    public void Resolve_DealsTwoDamageToCreature()
    {
        var bear = CreatureOnBattlefield(_bob, 2, 2);
        var def = PillarOfFlameFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { bear } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bear.Damage.Should().Be(2);
    }

    [Fact]
    public void Resolve_DamagedCreatureDeath_RewrittenToExile()
    {
        var bus = new ReplacementBus();
        var bear = CreatureOnBattlefield(_bob, 2, 2);

        var def = PillarOfFlameFactory.BuildSpellDefinition(resolver: x => x, replacements: bus);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { bear } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        var dying = new ZoneMoveIntent(bear, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        var result = bus.Apply(dying);
        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Exile,
            "a creature dealt damage by Pillar of Flame that would die is exiled instead");
    }

    [Fact]
    public void Resolve_UntargetedCreatureDeath_NotRewritten()
    {
        var bus = new ReplacementBus();
        var bear = CreatureOnBattlefield(_bob, 2, 2);
        var other = CreatureOnBattlefield(_alice, 1, 1);

        var def = PillarOfFlameFactory.BuildSpellDefinition(resolver: x => x, replacements: bus);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { bear } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // A different creature dying is unaffected (CR 700.3 — scoped to the
        // creature dealt damage this way): its death stays a graveyard move.
        var dying = new ZoneMoveIntent(other, ZoneType.Battlefield, ZoneType.Graveyard, _alice);
        bus.Apply(dying)!.ToZone.Should().Be(ZoneType.Graveyard);
    }
}
