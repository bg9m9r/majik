using FluentAssertions;
using Majik.Core.Abilities;
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
/// Unit tests for <see cref="EphemerateFactory"/>.
///
/// Covers:
/// - Identity (Instant, {W}, owner / controller).
/// - Rebound keyword marker (CR 702.88) — Rebound rider is deferred but
///   the marker is attached.
/// - NamedCardFactory dispatch.
/// - SpellDefinition shape — single 1..1 "target creature you control"
///   target, Protection intent.
/// - Resolve: exiles the targeted creature and immediately returns it
///   (CR 701.21 + CR 614).
/// - Resolve: opponent-controlled target fizzles at resolution-time
///   legality check (CR 608.2b).
/// </summary>
[Trait("Color", "W")]
public class EphemerateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + markers
    // -----------------------------------------------------------------------

    [Fact]
    public void Ephemerate_IsInstant_AtCostW()
    {
        var c = EphemerateFactory.Create(_alice);

        c.Name.Should().Be("Ephemerate");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Ephemerate_HasReboundKeywordMarker()
    {
        var c = EphemerateFactory.Create(_alice);

        var keywordNames = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain("Rebound",
            "CR 702.88 — Rebound marker attached even though the rider is deferred");
    }
    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Ephemerate_Definition_HasSingleControllerCreatureTarget()
    {
        var def = EphemerateFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature");
        tr.Description.Should().Contain("you control");
        tr.Intent.Should().Be(Majik.Core.Cards.BotIntent.Protection);
    }

    // -----------------------------------------------------------------------
    // Resolve — exile-then-return
    // -----------------------------------------------------------------------

    [Fact]
    public void Ephemerate_Resolve_ExilesThenReturnsTargetCreature()
    {
        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");

        var def = EphemerateFactory.BuildSpellDefinition(_alice);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bear } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "CR 614 — Ephemerate returns the exiled creature in the same resolution");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        _alice.Zones.Exile.GetCards().Should().NotContain(bear);
        bear.Controller.Should().BeSameAs(_alice,
            "return is 'under its owner's control'");
    }

    [Fact]
    public void Ephemerate_Resolve_OpponentControlledTarget_Fizzles()
    {
        var bobBear = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        var def = EphemerateFactory.BuildSpellDefinition(_alice);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bobBear } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Battlefield,
            "opponent-controlled target → CR 608.2b illegal-target → no effect");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobBear);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var bear = new Creature(name, cost, 2, 2);
        bear.SetOwner(owner);
        bear.SetController(owner);
        owner.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        return bear;
    }
}
