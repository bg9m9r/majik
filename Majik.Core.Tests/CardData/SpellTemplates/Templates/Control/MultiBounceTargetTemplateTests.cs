using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Control;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Control;

public class MultiBounceTargetTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20),
            _ => _,
            Effects: null,
            Stack: null);

    [Theory]
    [InlineData("Return up to two target creatures to their owners' hands.")]
    [InlineData("Return up to three target creatures to their owners' hands.")]
    [InlineData("Return up to four target creatures to their owners' hands.")]
    public void Binds_OnFamilyOracle(string oracle)
    {
        new MultiBounceTargetTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Theory]
    // Single-target bounce — out of family; BounceTargetTemplate handles it.
    [InlineData("Return target creature to its owner's hand.")]
    // "up to one" isn't part of the pattern.
    [InlineData("Return up to one target creature to its owner's hand.")]
    // Non-creature.
    [InlineData("Return up to two target permanents to their owners' hands.")]
    // Reverse polarity (exile, not bounce).
    [InlineData("Exile up to two target creatures.")]
    public void DoesNotBind_OutOfFamily(string oracle)
    {
        new MultiBounceTargetTemplate().TryBind(Ctx(oracle))
            .Should().BeNull();
    }

    [Fact]
    public void TargetRequest_Allows_UpToN()
    {
        var def = new MultiBounceTargetTemplate().TryBind(Ctx(
            "Return up to three target creatures to their owners' hands."));
        def.Should().NotBeNull();
        def!.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(0);
        def.TargetRequests[0].MaxTargets.Should().Be(3);
    }

    [Fact]
    public void Intent_IsBounce()
    {
        new MultiBounceTargetTemplate().Intent.Should().Be(BotIntent.Bounce);
    }

    [Fact]
    public void Priority_Is_65()
    {
        new MultiBounceTargetTemplate().Priority.Should().Be(65);
    }

    [Fact]
    public void Effect_ReturnsEveryChosenCreatureToItsOwnersHand()
    {
        var alice = new Player("A", 20);
        var bob = new Player("B", 20);

        var c1 = new Creature("Bear1", "1G", 2, 2)
        {
            Owner = alice, Controller = alice, Zone = ZoneType.Battlefield,
        };
        var c2 = new Creature("Bear2", "1G", 1, 1)
        {
            Owner = bob, Controller = bob, Zone = ZoneType.Battlefield,
        };
        alice.Zones.Battlefield.AddCard(c1);
        bob.Zones.Battlefield.AddCard(c2);

        var def = new MultiBounceTargetTemplate().TryBind(Ctx(
            "Return up to two target creatures to their owners' hands."));
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { c1, c2 } },
            Mana: new ManaPayment(Array.Empty<ICard>()));

        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        c1.Zone.Should().Be(ZoneType.Hand);
        c2.Zone.Should().Be(ZoneType.Hand);
        alice.Zones.Hand.GetCards().Should().Contain(c1);
        bob.Zones.Hand.GetCards().Should().Contain(c2);
        alice.Zones.Battlefield.GetCards().Should().NotContain(c1);
        bob.Zones.Battlefield.GetCards().Should().NotContain(c2);
    }

    [Fact]
    public void Effect_NoTargetsChosen_NoOp()
    {
        var def = new MultiBounceTargetTemplate().TryBind(Ctx(
            "Return up to two target creatures to their owners' hands."));
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { Array.Empty<object>() },
            Mana: new ManaPayment(Array.Empty<ICard>()));

        var resolved = def!.EffectFactory(chosen);
        var act = () => { foreach (var e in resolved) e.Execute(); };
        act.Should().NotThrow();
    }

    [Fact]
    public void OracleSpellBinder_RegistersTemplate()
    {
        Majik.Core.CardData.OracleSpellBinder.Registry.OrderedTemplates
            .Should().Contain(t => t.Name == "MultiBounceTarget");
    }
}
