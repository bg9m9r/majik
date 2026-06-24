using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HivespineWolverineFactory"/>.
///
/// Card: Hivespine Wolverine — Creature — Elemental Wolverine {3}{G}{G}, 5/4.
///   "When this creature enters, choose one —
///    • Put a +1/+1 counter on target creature you control.
///    • This creature fights target creature token.
///    • Destroy target artifact or enchantment."
///
/// Covers (the card's UNIQUE behaviour — the modal ETB):
/// - Identity ({3}{G}{G} Creature — Elemental Wolverine, 5/4, mono-green, MV 5).
/// - Exactly one battlefield-active ETB modal triggered ability.
/// - Mode 0 (+1/+1 counter on target creature you control).
/// - Mode 1 (fight target creature token; a non-token creature is a no-op).
/// - Mode 2 (destroy target artifact or enchantment; a creature is a no-op).
/// </summary>
[Trait("Color", "G")]
public class HivespineWolverineFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => AgentRegistry.Clear();

    private static void SetTarget(TriggeredAbility etb, int slot, object pick)
    {
        var targets = new IReadOnlyList<object>[3];
        for (var i = 0; i < 3; i++) targets[i] = System.Array.Empty<object>();
        targets[slot] = new[] { pick };
        etb.SetChosenTargets(targets);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void HivespineWolverine_Identity()
    {
        var c = HivespineWolverineFactory.Create(_alice);

        c.Name.Should().Be("Hivespine Wolverine");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(4);
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue("Hivespine Wolverine is an Elemental");
        c.HasSubtype(CardSubtype.Wolverine).Should().BeTrue("Hivespine Wolverine is a Wolverine");
        c.ManaCost.Should().Be("{3}{G}{G}");
        // {3}{G}{G} = mana value 5 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(5);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "{G} pips");
        colors.Should().HaveCount(1, "mono-green");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HivespineWolverine_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = HivespineWolverineFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one ETB modal trigger");

        var etb = triggers.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
        etb.InterveningIf.Should().BeNull("unconditional ETB — no intervening-if");
    }

    // -----------------------------------------------------------------------
    // Mode 0 — Put a +1/+1 counter on target creature you control
    // -----------------------------------------------------------------------

    [Fact]
    public void HivespineWolverine_Mode0_PutsCounterOnTargetCreatureYouControl()
    {
        var wolverine = HivespineWolverineFactory.Create(_alice, mode: HivespineWolverineFactory.ModeCounter);
        wolverine.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var etb = wolverine.Abilities.OfType<TriggeredAbility>().Single();
        SetTarget(etb, HivespineWolverineFactory.ModeCounter, bear);
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "mode 0 puts a +1/+1 counter on the targeted creature you control");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — This creature fights target creature token
    // -----------------------------------------------------------------------

    [Fact]
    public void HivespineWolverine_Mode1_FightsTargetCreatureToken()
    {
        var wolverine = HivespineWolverineFactory.Create(_alice, mode: HivespineWolverineFactory.ModeFight);
        wolverine.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(wolverine);

        // A 4/4 creature TOKEN controlled by Bob.
        var token = new Creature("Beast", "", 4, 4);
        token.SetOwner(_bob);
        token.SetController(_bob);
        token.MarkAsToken();
        token.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(token);

        var etb = wolverine.Abilities.OfType<TriggeredAbility>().Single();
        SetTarget(etb, HivespineWolverineFactory.ModeFight, token);
        foreach (var effect in etb.Effects) effect.Execute();

        // CR 701.12a — mutual simultaneous damage: 5 to the token, 4 to Hivespine.
        token.Damage.Should().Be(5, "Hivespine (5 power) deals 5 to the token");
        wolverine.Damage.Should().Be(4, "the 4/4 token deals 4 back to Hivespine");
    }

    [Fact]
    public void HivespineWolverine_Mode1_NonTokenCreature_IsNoOp()
    {
        var wolverine = HivespineWolverineFactory.Create(_alice, mode: HivespineWolverineFactory.ModeFight);
        wolverine.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(wolverine);

        // A NONtoken creature is not a legal "creature token" target.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var etb = wolverine.Abilities.OfType<TriggeredAbility>().Single();
        SetTarget(etb, HivespineWolverineFactory.ModeFight, bear);
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Damage.Should().Be(0,
            "a nontoken creature is an illegal target — clean no-op (CR 608.2b)");
        wolverine.Damage.Should().Be(0, "no fight occurred");
    }

    // -----------------------------------------------------------------------
    // Mode 2 — Destroy target artifact or enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void HivespineWolverine_Mode2_DestroysTargetArtifact()
    {
        var wolverine = HivespineWolverineFactory.Create(_alice, mode: HivespineWolverineFactory.ModeDestroy);

        var signet = new Artifact("Boros Signet", "{2}");
        signet.SetOwner(_bob);
        signet.SetController(_bob);
        signet.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(signet);

        var etb = wolverine.Abilities.OfType<TriggeredAbility>().Single();
        SetTarget(etb, HivespineWolverineFactory.ModeDestroy, signet);
        foreach (var effect in etb.Effects) effect.Execute();

        signet.Zone.Should().Be(ZoneType.Graveyard,
            "mode 2 destroys the targeted artifact (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(signet);
    }

    [Fact]
    public void HivespineWolverine_Mode2_IllegalTarget_IsNoOp()
    {
        var wolverine = HivespineWolverineFactory.Create(_alice, mode: HivespineWolverineFactory.ModeDestroy);

        // A creature is NOT a legal "artifact or enchantment" target.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var etb = wolverine.Abilities.OfType<TriggeredAbility>().Single();
        SetTarget(etb, HivespineWolverineFactory.ModeDestroy, bear);
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "a creature is an illegal target — clean no-op (CR 608.2b)");
    }
}
