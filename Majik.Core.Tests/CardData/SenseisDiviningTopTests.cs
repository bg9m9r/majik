using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SenseisDiviningTopFactory"/>.
///
/// Sensei's Divining Top (Champions of Kamigawa, {1}, Artifact):
///   "{T}: Look at the top three cards of your library, then put them
///    back in any order."
///   "{1}, {T}: Draw a card, then put Sensei's Divining Top on top of
///    its owner's library."
///
/// Covers:
///   - Card identity (name, artifact type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Two activated abilities: peek-3 (tap-only) and draw-return (mana +
///     tap).
///   - {T} reorder: agent-driven reverse moves the previous bottom of the
///     peeked window to the top.
///   - {1}, {T} draw-return: hand gets the top of library; Top ends up on
///     top of its owner's library.
///   - {1}, {T} on an empty library: draw flags MarkTriedToDrawFromEmptyLibrary
///     and Top still moves to library top.
/// </summary>
public class SenseisDiviningTopTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        // Tests register agents on the global AgentRegistry; clear so cross-
        // test ordering can't leak scry decisions into unrelated tests.
        AgentRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SenseisDiviningTop_HasExpectedShape()
    {
        var card = SenseisDiviningTopFactory.Create(_alice);

        card.Name.Should().Be("Sensei's Divining Top");
        card.ManaCost.Should().Be("{1}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SenseisDiviningTop()
    {
        var card = NamedCardFactory.Create("Sensei's Divining Top", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Sensei's Divining Top");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SenseisDiviningTop_HasTwoActivatedAbilities_PeekAndDrawReturn()
    {
        var card = SenseisDiviningTopFactory.Create(_alice);
        var abilities = card.Abilities.OfType<ActivatedAbility>().ToList();

        abilities.Should().HaveCount(2,
            "one {T} peek-and-reorder, one {1}{T} draw-and-return");

        // First ability: tap-only, no mana cost.
        var peek = abilities[0];
        peek.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);
        peek.Costs.OfType<ManaCostCost>().Should().BeEmpty();

        // Second ability: mana {1} + tap.
        var drawReturn = abilities[1];
        drawReturn.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);
        drawReturn.Costs.OfType<ManaCostCost>().Should().ContainSingle();
    }

    // -----------------------------------------------------------------------
    // {T}: peek 3, reorder
    // -----------------------------------------------------------------------

    [Fact]
    public void PeekAbility_DefaultReorder_KeepsTopThreeInPlace()
    {
        // Library: [a, b, c, d]. No agent registered → default keeps the
        // peeked window [a, b, c] in original order on top.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var top = SenseisDiviningTopFactory.Create(_alice);
        PutOnBattlefield(top);

        var peek = top.Abilities.OfType<ActivatedAbility>().First();
        foreach (var e in peek.Effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().Equal(new[] { a, b, c, d });
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void PeekAbility_AgentReversesTop_ReordersLibraryFront()
    {
        // Library: [a, b, c, d]. ScriptedAgent reverses → [c, b, a, d].
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { c, b, a }));
        AgentRegistry.Set(_alice, agent);

        var top = SenseisDiviningTopFactory.Create(_alice);
        PutOnBattlefield(top);

        var peek = top.Abilities.OfType<ActivatedAbility>().First();
        foreach (var e in peek.Effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().Equal(new[] { c, b, a, d });
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void PeekAbility_ShortLibrary_PeeksWhatExists_DoesNotThrow()
    {
        // Library has fewer than 3 cards. Peek returns what's there; no reorder
        // needed (default preserves order). Should not throw.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");

        var top = SenseisDiviningTopFactory.Create(_alice);
        PutOnBattlefield(top);

        var peek = top.Abilities.OfType<ActivatedAbility>().First();
        Action act = () => { foreach (var e in peek.Effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Library.GetCards().Should().Equal(new[] { a, b });
    }

    [Fact]
    public void PeekAbility_EmptyLibrary_NoOp()
    {
        var top = SenseisDiviningTopFactory.Create(_alice);
        PutOnBattlefield(top);

        var peek = top.Abilities.OfType<ActivatedAbility>().First();
        Action act = () => { foreach (var e in peek.Effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should()
            .BeFalse("the peek ability does not draw");
    }

    // -----------------------------------------------------------------------
    // {1}, {T}: draw, then put Top on top of library
    // -----------------------------------------------------------------------

    [Fact]
    public void DrawReturnAbility_DrawsTopOfLibrary_ThenReturnsTopToLibraryTop()
    {
        // Library: [a, b, c]. Activation should:
        //   - move `a` to hand (the draw),
        //   - move Top from battlefield to library index 0,
        //   - leave [Top, b, c] on the library, [a] in hand.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");

        var top = SenseisDiviningTopFactory.Create(_alice);
        PutOnBattlefield(top);

        var drawReturn = top.Abilities.OfType<ActivatedAbility>().Last();
        foreach (var e in drawReturn.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().Equal(new ICard[] { top, b, c });
        _alice.Zones.Battlefield.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Library);
        a.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void DrawReturnAbility_EmptyLibrary_FlagsDrawFromEmpty_AndTopStillReturns()
    {
        // No library cards. Draw flags TriedToDrawFromEmptyLibrary (CR 704.5b),
        // then Top moves onto the (now non-empty) library top.
        var top = SenseisDiviningTopFactory.Create(_alice);
        PutOnBattlefield(top);

        var drawReturn = top.Abilities.OfType<ActivatedAbility>().Last();
        Action act = () => { foreach (var e in drawReturn.Effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
        _alice.Zones.Library.GetCards().Should().Equal(new ICard[] { top });
        top.Zone.Should().Be(ZoneType.Library);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Card SeedLibraryCard(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private void PutOnBattlefield(Artifact top)
    {
        _alice.Zones.Battlefield.AddCard(top);
        top.SetZone(ZoneType.Battlefield);
    }
}
