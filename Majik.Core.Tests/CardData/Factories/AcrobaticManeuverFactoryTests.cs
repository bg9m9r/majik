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
/// Unit tests for <see cref="AcrobaticManeuverFactory"/>.
///
/// Covers:
/// - Identity (Instant, {2}{W}, owner / controller).
/// - NamedCardFactory dispatch.
/// - SpellDefinition shape — single 1..1 "target creature you control"
///   target, Protection intent.
/// - Resolve: exiles + returns the targeted creature, then caster draws
///   a card.
/// - Resolve: opponent-controlled target fizzles the flicker half, but
///   the draw still resolves (CR 608.2b only fizzles target-contingent
///   effect lines; "Draw a card" is unconditional).
/// - Resolve: empty target list short-circuits cleanly (won't happen in
///   prod because MinTargets = 1, but documents the no-op posture).
/// </summary>
[Trait("Color", "W")]
public class AcrobaticManeuverFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AcrobaticManeuver_IsInstant_AtCost2W()
    {
        var c = AcrobaticManeuverFactory.Create(_alice);

        c.Name.Should().Be("Acrobatic Maneuver");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void AcrobaticManeuver_Definition_HasSingleControllerCreatureTarget()
    {
        var def = AcrobaticManeuverFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature");
        tr.Description.Should().Contain("you control");
        tr.Intent.Should().Be(BotIntent.Protection);
    }

    // -----------------------------------------------------------------------
    // Resolve — flicker + draw
    // -----------------------------------------------------------------------

    [Fact]
    public void AcrobaticManeuver_Resolve_ExilesAndReturnsTarget_ThenDraws()
    {
        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");
        SeedLibrary(_alice, count: 3);
        var startingHandCount = _alice.Zones.Hand.Count;

        var def = AcrobaticManeuverFactory.BuildSpellDefinition(_alice);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bear } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "CR 614 — Acrobatic Maneuver returns the exiled creature in the same resolution");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        _alice.Zones.Exile.GetCards().Should().NotContain(bear);
        bear.Controller.Should().BeSameAs(_alice);

        _alice.Zones.Hand.Count.Should().Be(startingHandCount + 1,
            "CR 121.1 — 'Draw a card' fires after the flicker resolves");
    }

    [Fact]
    public void AcrobaticManeuver_Resolve_OpponentControlledTarget_FlickerFizzles_DrawStillFires()
    {
        var bobBear = NewControlledCreature(_bob, "Goblin Guide", "{R}");
        SeedLibrary(_alice, count: 3);
        var startingHandCount = _alice.Zones.Hand.Count;

        var def = AcrobaticManeuverFactory.BuildSpellDefinition(_alice);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bobBear } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Battlefield,
            "opponent-controlled target → CR 608.2b illegal-target → flicker fizzles");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobBear);

        _alice.Zones.Hand.Count.Should().Be(startingHandCount + 1,
            "the draw rider is unconditional — CR 608.2b only fizzles the target-contingent flicker");
    }

    [Fact]
    public void AcrobaticManeuver_Resolve_NoTargets_NoOp()
    {
        var def = AcrobaticManeuverFactory.BuildSpellDefinition(_alice);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: System.Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty));

        effects.Should().BeEmpty("no targets → no effects produced (prod cast requires MinTargets = 1)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static void SeedLibrary(Player owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var card = new Creature($"LibCard{i}", "{1}", 1, 1);
            card.SetOwner(owner);
            owner.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }
    }
}
