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
/// Unit tests for <see cref="UlcerateFactory"/> (Journey into Nyx, {B}).
/// "Target creature gets -3/-3 until end of turn. You lose 3 life."
/// Disfigure-shape -X/-X plus a caster life cost.
/// </summary>
[Trait("Color", "B")]
public class UlcerateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ChosenSpellParams Chosen(object target) =>
        new(ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

    [Fact]
    public void Identity_InstantAtB()
    {
        var card = UlcerateFactory.Create(_alice);

        card.Name.Should().Be("Ulcerate");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{B}");
        card.Owner.Should().BeSameAs(_alice);
    }
    [Fact]
    public void SpellDefinition_SingleTargetCreatureRequest()
    {
        var def = UlcerateFactory.BuildDefinition(_alice);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
    }

    [Fact]
    public void Resolve_AppliesMinus3Minus3_AndCasterLoses3Life()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            ActiveEffects = new ContinuousEffectsService(),
        };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = UlcerateFactory.BuildDefinition(_alice);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        bear.Power.Should().Be(-1, "2/2 with -3/-3 → -1/-1");
        bear.Toughness.Should().Be(-1);
        _alice.LifeTotal.Should().Be(17, "You lose 3 life");
    }

    [Fact]
    public void Resolve_IllegalTarget_NoLifeLoss()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var def = UlcerateFactory.BuildDefinition(_alice);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        _alice.LifeTotal.Should().Be(20, "illegal target → spell does nothing, no life loss");
    }

    [Fact]
    public void Resolve_LegalTargetWithoutActiveEffects_StillLoses3Life_NoThrow()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = UlcerateFactory.BuildDefinition(_alice);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        _alice.LifeTotal.Should().Be(17);
    }
}
