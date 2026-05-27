using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="VendettaFactory"/> (Mirage, {B}).
/// "Destroy target nonblack creature. It can't be regenerated.
///  You lose life equal to that creature's toughness."
/// </summary>
public class VendettaFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ChosenSpellParams Chosen(object target) =>
        new(ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

    // ── Identity ────────────────────────────────────────────────────────────

    [Fact]
    public void Identity_InstantAtB_BlackColoured()
    {
        var card = VendettaFactory.Create(_alice);

        card.Name.Should().Be("Vendetta");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // ── Dispatch ─────────────────────────────────────────────────────────────

    [Fact]
    public void DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Vendetta", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Vendetta");
    }

    // ── SpellDefinition shape ────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_SingleTargetNonblackCreatureRequest()
    {
        var def = VendettaFactory.BuildDefinition(_alice, o => o);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("nonblack");
        def.TargetRequests[0].Description.Should().Contain("creature");
    }

    // ── Happy path: nonblack 2/3 ─────────────────────────────────────────────

    [Fact]
    public void Resolve_NonblackCreature_MovesToGraveyard_CasterLosesToughnessLife()
    {
        // Grizzly Bears 2/3 (nonblack)
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 3)
        {
            Owner = _bob,
            Controller = _bob,
        };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = VendettaFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "Vendetta destroys the target — it goes to the graveyard");
        _alice.LifeTotal.Should().Be(17,
            "Alice loses life equal to the creature's toughness (3)");
    }

    // ── No-op: black creature ────────────────────────────────────────────────

    [Fact]
    public void Resolve_BlackCreature_NoEffect_NoCasterLifeLoss()
    {
        // Black creature ({B} pip in cost)
        var darkRitual = new Creature("Unholy Priest", "{1}{B}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        darkRitual.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(darkRitual);

        var def = VendettaFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(darkRitual))) e.Execute();

        darkRitual.Zone.Should().Be(ZoneType.Battlefield,
            "black creature is an illegal target — no destroy");
        _alice.LifeTotal.Should().Be(20,
            "CR 608.2b — illegal target → whole spell does nothing, no life loss");
    }

    // ── No-op: target left battlefield (CR 608.2b) ───────────────────────────

    [Fact]
    public void Resolve_TargetNotOnBattlefield_NoEffect_NoCasterLifeLoss()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 3)
        {
            Owner = _bob,
            Controller = _bob,
        };
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var def = VendettaFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        _alice.LifeTotal.Should().Be(20,
            "CR 608.2b — target is not on the battlefield → spell does nothing");
    }
}
