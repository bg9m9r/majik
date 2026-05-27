using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="QuickStudyFactory"/>.
///
/// Oracle text: "Draw two cards." ({2}{U} Instant)
///
/// Covers:
/// - Card identity (Instant, {2}{U}, blue, CMC 3, owner/controller).
/// - NamedCardFactory dispatch by name.
/// - SpellDefinition shape — no modes, no X, no target requests.
/// - Resolve: caster draws exactly 2 cards; library shrinks by 2.
/// - Resolve: empty library flags CR 704.5b loss SBA but does not throw.
/// </summary>
public class QuickStudyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void QuickStudy_HasInstantShape_Blue_AtCost2U()
    {
        var card = QuickStudyFactory.Create(_alice);

        card.Name.Should().Be("Quick Study");
        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCostValue.TotalValue.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsQuickStudyShape()
    {
        var dispatched = NamedCardFactory.Create("Quick Study", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Quick Study");
        dispatched.ManaCost.Should().Be("{2}{U}");
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void QuickStudy_SpellDefinition_HasNoTargets_NoModes_NoX()
    {
        var def = QuickStudyFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void QuickStudy_Resolve_DrawsExactlyTwoCards_LibraryShrinksByTwo()
    {
        // Library = [L1, L2, L3, L4]. Hand starts empty.
        // After resolve: hand = [L1, L2], library = [L3, L4].
        var l1 = NewLibraryCard("L1");
        var l2 = NewLibraryCard("L2");
        var l3 = NewLibraryCard("L3");
        var l4 = NewLibraryCard("L4");

        var effect = QuickStudyFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2);
        _alice.Zones.Hand.GetCards().Should().Equal(new[] { l1, l2 });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { l3, l4 });
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();

        l1.Zone.Should().Be(ZoneType.Hand);
        l2.Zone.Should().Be(ZoneType.Hand);
        l3.Zone.Should().Be(ZoneType.Library);
        l4.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void QuickStudy_Resolve_EmptyLibrary_FlagsLossSba_DoesNotThrow()
    {
        // Library empty — first draw attempt flags CR 704.5b; second draw
        // sees the flag already set; neither throws.
        var act = () =>
        {
            var effect = QuickStudyFactory.BuildResolveEffect(_alice).Single();
            effect.Execute();
        };

        act.Should().NotThrow();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            because: "drawing from an empty library stamps the SBA loss flag (CR 704.5b)");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private ICard NewLibraryCard(string name)
    {
        var c = new Sorcery(name, "{0}") { Owner = _alice, Controller = _alice };
        c.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c);
        return c;
    }
}
