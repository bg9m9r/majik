using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RestorationAngelFactory"/>.
///
/// Covers:
/// - Identity (Creature, 3/4, Angel, {3}{W}, Flash + Flying markers).
/// - NamedCardFactory dispatch.
/// - ETB triggered ability shape — 0..1 "another target non-Angel creature
///   you control", Protection intent.
/// - Resolve: exiles + immediately returns the targeted non-Angel creature
///   (CR 701.21 + CR 614).
/// - Resolve: Angel-subtype target is excluded by the candidate gather and
///   the resolve-time legality check (CR 109.5 / CR 608.2b).
/// - Resolve: opponent-controlled target fizzles (CR 608.2b).
/// - Resolve: zero-target "may" branch is a clean no-op.
/// </summary>
public class RestorationAngelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RestorationAngel_HasCorrectShape()
    {
        var c = RestorationAngelFactory.Create(_alice);

        c.Name.Should().Be("Restoration Angel");
        c.ManaCost.Should().Be("{3}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var keywordNames = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain(new[] { "Flash", "Flying" });
    }

    [Fact]
    public void RestorationAngel_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Restoration Angel", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Restoration Angel");
        c.ManaCost.Should().Be("{3}{W}");
    }

    [Fact]
    public void RestorationAngel_HasEtbTriggerWithUpToOneTarget()
    {
        var c = RestorationAngelFactory.Create(_alice);

        var triggered = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggered.Should().HaveCount(1, "single ETB triggered ability");

        var etb = triggered[0];
        etb.TargetRequests.Should().HaveCount(1);
        var tr = etb.TargetRequests[0];
        tr.MinTargets.Should().Be(0, "'may' rider — selecting zero is declining");
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("non-Angel");
        tr.Description.Should().Contain("you control");
        tr.Intent.Should().Be(BotIntent.Protection);
    }

    // -----------------------------------------------------------------------
    // Resolve — exile-then-return
    // -----------------------------------------------------------------------

    [Fact]
    public void RestorationAngel_Resolve_FlickersTargetedNonAngelCreature()
    {
        var resto = NewControlledRestoOnBattlefield(_alice);
        var bear = NewControlledCreature(_alice, "Wall of Omens", "{1}{W}");

        SetEtbTargets(resto, new object[] { bear });
        FireEtbEffect(resto);

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "CR 614 — Restoration Angel returns the exiled creature in the same resolution");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        _alice.Zones.Exile.GetCards().Should().NotContain(bear);
        bear.Controller.Should().BeSameAs(_alice,
            "return is 'under your control' — Restoration Angel's controller is Alice");
    }

    [Fact]
    public void RestorationAngel_Resolve_AngelTarget_Fizzles()
    {
        var resto = NewControlledRestoOnBattlefield(_alice);
        // Another Angel — "non-Angel" rider should reject at resolve time
        // even if the agent set it as a target (CR 608.2b).
        var seraph = new Creature("Serra Angel", "{3}{W}{W}", 4, 4,
            subtypes: new[] { CardSubtype.Angel });
        seraph.SetOwner(_alice);
        seraph.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(seraph);
        seraph.SetZone(ZoneType.Battlefield);

        SetEtbTargets(resto, new object[] { seraph });
        FireEtbEffect(resto);

        seraph.Zone.Should().Be(ZoneType.Battlefield,
            "Angel target violates the 'non-Angel' filter → CR 608.2b no-effect");
    }

    [Fact]
    public void RestorationAngel_Resolve_OpponentControlledTarget_Fizzles()
    {
        var resto = NewControlledRestoOnBattlefield(_alice);
        var bobBear = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        SetEtbTargets(resto, new object[] { bobBear });
        FireEtbEffect(resto);

        bobBear.Zone.Should().Be(ZoneType.Battlefield,
            "opponent-controlled target violates 'you control' → CR 608.2b no-effect");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobBear);
    }

    [Fact]
    public void RestorationAngel_Resolve_NoTargetChosen_DeclineMay_NoOp()
    {
        var resto = NewControlledRestoOnBattlefield(_alice);
        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");

        // Zero-target "may" decline.
        SetEtbTargets(resto, Array.Empty<object>());
        FireEtbEffect(resto);

        bear.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Exile.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewControlledRestoOnBattlefield(Player owner)
    {
        var resto = RestorationAngelFactory.Create(owner);
        owner.Zones.Battlefield.AddCard(resto);
        resto.SetZone(ZoneType.Battlefield);
        return resto;
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var bear = new Creature(name, cost, 2, 2);
        bear.SetOwner(owner);
        bear.SetController(owner);
        owner.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        return bear;
    }

    private static TriggeredAbility EtbTrigger(Creature resto) =>
        resto.Abilities.OfType<TriggeredAbility>().First(t => t.TargetRequests.Count > 0);

    private static void SetEtbTargets(Creature resto, IReadOnlyList<object> targets)
    {
        EtbTrigger(resto).SetChosenTargets(new[] { targets });
    }

    private static void FireEtbEffect(Creature resto)
    {
        foreach (var eff in EtbTrigger(resto).Effects)
        {
            eff.Execute();
        }
    }
}
