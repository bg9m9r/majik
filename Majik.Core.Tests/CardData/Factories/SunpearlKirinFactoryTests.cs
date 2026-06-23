using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Factories;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SunpearlKirinFactory"/>.
///
/// Covers:
/// - Identity ({1}{W}, 2/1, Creature — Kirin, Flash + Flying markers).
/// - ETB triggered ability shape — 0..1 "other target nonland permanent you
///   control", Bounce intent.
/// - Resolve: returns the targeted nonland permanent to its owner's hand
///   (CR 701.20).
/// - Resolve: bouncing a TOKEN draws a card; a nontoken does not (the
///   token-leaves draw rider, CR 603.10).
/// - Resolve: land target / opponent-controlled target fizzle (CR 305 /
///   CR 608.2b).
/// - Resolve: zero-target "up to one" branch is a clean no-op.
/// </summary>
[Trait("Color", "W")]
public class SunpearlKirinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SunpearlKirin_HasCorrectShape()
    {
        var c = SunpearlKirinFactory.Create(_alice);

        c.Name.Should().Be("Sunpearl Kirin");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Kirin).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);

        var keywordNames = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain(new[] { "Flash", "Flying" });
    }

    [Fact]
    public void SunpearlKirin_HasEtbTriggerWithUpToOneTarget()
    {
        var c = SunpearlKirinFactory.Create(_alice);

        var triggered = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggered.Should().HaveCount(1, "single ETB triggered ability");

        var tr = triggered[0].TargetRequests.Single();
        tr.MinTargets.Should().Be(0, "'up to one' rider — selecting zero is declining");
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("nonland");
        tr.Description.Should().Contain("you control");
        tr.Intent.Should().Be(BotIntent.Bounce);
    }

    // -----------------------------------------------------------------------
    // Resolve — bounce
    // -----------------------------------------------------------------------

    [Fact]
    public void SunpearlKirin_Resolve_ReturnsTargetedNonlandPermanentToHand()
    {
        var kirin = NewKirinOnBattlefield(_alice);
        var bear = NewControlledCreature(_alice, "Grizzly Bears");

        SetEtbTargets(kirin, new object[] { bear });
        FireEtbEffect(kirin);

        bear.Zone.Should().Be(ZoneType.Hand, "CR 701.20 — returned to owner's hand");
        _alice.Zones.Hand.GetCards().Should().Contain(bear);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void SunpearlKirin_Resolve_TokenTarget_DrawsACard()
    {
        var kirin = NewKirinOnBattlefield(_alice);
        var token = NewControlledCreature(_alice, "Soldier Token");
        token.MarkAsToken();

        var libraryCard = new Creature("Plains-like Filler", "{0}", 1, 1);
        libraryCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(libraryCard);

        SetEtbTargets(kirin, new object[] { token });
        FireEtbEffect(kirin);

        // "If it was a token, draw a card." — controller draws the top card.
        _alice.Zones.Hand.GetCards().Should().Contain(libraryCard,
            "the bounced permanent was a token, so the Kirin's controller draws");
    }

    [Fact]
    public void SunpearlKirin_Resolve_NontokenTarget_DoesNotDraw()
    {
        var kirin = NewKirinOnBattlefield(_alice);
        var bear = NewControlledCreature(_alice, "Grizzly Bears");

        var libraryCard = new Creature("Filler", "{0}", 1, 1);
        libraryCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(libraryCard);

        SetEtbTargets(kirin, new object[] { bear });
        FireEtbEffect(kirin);

        _alice.Zones.Hand.GetCards().Should().NotContain(libraryCard,
            "a nontoken permanent does not trigger the draw rider");
        bear.Zone.Should().Be(ZoneType.Hand, "but the bounce still happens");
    }

    [Fact]
    public void SunpearlKirin_Resolve_LandTarget_Fizzles()
    {
        var kirin = NewKirinOnBattlefield(_alice);
        var land = new Land("Plains");
        land.SetOwner(_alice);
        land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        SetEtbTargets(kirin, new object[] { land });
        FireEtbEffect(kirin);

        land.Zone.Should().Be(ZoneType.Battlefield,
            "'nonland' rider rejects a land at resolve → CR 608.2b no-effect");
    }

    [Fact]
    public void SunpearlKirin_Resolve_OpponentControlledTarget_Fizzles()
    {
        var kirin = NewKirinOnBattlefield(_alice);
        var bobBear = NewControlledCreature(_bob, "Goblin Guide");

        SetEtbTargets(kirin, new object[] { bobBear });
        FireEtbEffect(kirin);

        bobBear.Zone.Should().Be(ZoneType.Battlefield,
            "opponent-controlled target violates 'you control' → CR 608.2b no-effect");
    }

    [Fact]
    public void SunpearlKirin_Resolve_NoTargetChosen_DeclineUpToOne_NoOp()
    {
        var kirin = NewKirinOnBattlefield(_alice);
        var bear = NewControlledCreature(_alice, "Grizzly Bears");

        SetEtbTargets(kirin, Array.Empty<object>());
        FireEtbEffect(kirin);

        bear.Zone.Should().Be(ZoneType.Battlefield, "zero-target decline is a clean no-op");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewKirinOnBattlefield(Player owner)
    {
        var kirin = SunpearlKirinFactory.Create(owner);
        owner.Zones.Battlefield.AddCard(kirin);
        kirin.SetZone(ZoneType.Battlefield);
        return kirin;
    }

    private static Creature NewControlledCreature(Player owner, string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility EtbTrigger(Creature kirin) =>
        kirin.Abilities.OfType<TriggeredAbility>().First(t => t.TargetRequests.Count > 0);

    private static void SetEtbTargets(Creature kirin, IReadOnlyList<object> targets)
    {
        EtbTrigger(kirin).SetChosenTargets(new[] { targets });
    }

    private static void FireEtbEffect(Creature kirin)
    {
        foreach (var eff in EtbTrigger(kirin).Effects)
        {
            eff.Execute();
        }
    }
}
