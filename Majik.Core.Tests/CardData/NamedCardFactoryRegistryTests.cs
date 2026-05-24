using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Smoke / contract tests for <see cref="NamedCardFactory"/>'s source-gen
/// dispatch table. The 296 individual arms are also exercised piecemeal
/// by each card's dedicated <c>*Tests</c> file (e.g. <c>SnapcasterMageTests</c>,
/// <c>WrathOfGodTests</c>) — this file just confirms the *registry*
/// itself is wired correctly and covers a representative cross-section
/// of types (creature, land, planeswalker, instant, artifact, enchantment).
///
/// If you add a new <c>[CardName("...")]</c> factory, you do NOT need
/// to touch this file. The generated <c>CreateGenerated</c> method picks
/// the new arm up automatically and the per-card test file takes care of
/// semantic coverage.
/// </summary>
public class NamedCardFactoryRegistryTests
{
    private readonly Player _alice = new("Alice");

    [Fact]
    public void GeneratedRegistrationCount_IncludesEveryAttributedFactory()
    {
        // Floor — guards against the source generator silently dropping
        // arms (e.g. a regression where ForAttributeWithMetadataName
        // stops matching). The exact count climbs as cards are added;
        // we just assert at-least the current population.
        NamedCardFactory.GeneratedRegistrationCount.Should().BeGreaterThanOrEqualTo(296);
    }

    [Theory]
    [InlineData("Abrupt Decay")]
    [InlineData("Path to Exile")]
    [InlineData("Wrath of God")]
    [InlineData("Snapcaster Mage")]
    [InlineData("Wrenn and Six")]
    [InlineData("Walking Ballista")]
    [InlineData("Yawgmoth, Thran Physician")]   // comma in name
    [InlineData("Yawgmoth's Will")]              // apostrophe in name
    [InlineData("Boseiju, Who Endures")]
    [InlineData("Aether Vial")]
    public void Create_KnownCard_ReturnsTypedCard(string cardName)
    {
        var card = NamedCardFactory.Create(cardName, _alice);

        card.Should().NotBeNull();
        card.Name.Should().Be(cardName);
        // Vanilla shell would have a zero-length mana cost AND be the
        // base Card type with no abilities — registered cards always
        // surface as a more specific subclass.
        card.Should().NotBeOfType<Card>(
            because: $"'{cardName}' is registered via [CardName] and should resolve to a typed subclass");
    }

    [Theory]
    [InlineData("Abrupt Decay")]
    public void Create_RegisteredInstant_IsInstant(string name)
    {
        var card = NamedCardFactory.Create(name, _alice);
        card.Should().BeAssignableTo<Instant>();
    }

    [Theory]
    [InlineData("Wrath of God")]
    public void Create_WrathOfGod_IsSorcery(string name)
    {
        var card = NamedCardFactory.Create(name, _alice);
        card.Should().BeAssignableTo<Sorcery>();
    }

    [Theory]
    [InlineData("Snapcaster Mage")]
    [InlineData("Walking Ballista")]
    public void Create_NamedCreatures_AreCreatures(string name)
    {
        var card = NamedCardFactory.Create(name, _alice);
        card.Should().BeAssignableTo<Creature>();
    }

    [Theory]
    [InlineData("Boseiju, Who Endures")]
    [InlineData("Aether Hub")]
    public void Create_NamedLands_AreLands(string name)
    {
        var card = NamedCardFactory.Create(name, _alice);
        card.Should().BeAssignableTo<Land>();
    }

    [Theory]
    [InlineData("Wrenn and Six")]
    public void Create_Planeswalkers_ArePlaneswalkers(string name)
    {
        var card = NamedCardFactory.Create(name, _alice);
        card.Should().BeAssignableTo<Planeswalker>();
    }

    [Fact]
    public void Create_UnknownName_ReturnsVanillaShell()
    {
        var card = NamedCardFactory.Create("Definitely Not A Real Card", _alice);

        card.Should().NotBeNull();
        card.GetType().Should().Be(typeof(Card),
            because: "unknown card names fall through to the vanilla Card shell");
        card.Name.Should().Be("Definitely Not A Real Card");
        card.Owner.Should().Be(_alice);
    }

    [Fact]
    public void Create_BasicLand_StillResolvedInline()
    {
        // Basic lands intentionally stay on the inline fallback path —
        // the inline branch attaches a per-instance mana ability that
        // the source-gen route never sees. Regression test: confirm
        // the inline branch still fires.
        var forest = NamedCardFactory.Create("Forest", _alice);

        forest.Should().BeAssignableTo<Land>();
        forest.HasSupertype(CardSupertype.Basic).Should().BeTrue();
        forest.HasSubtype(CardSubtype.Forest).Should().BeTrue();
    }

    [Fact]
    public void Create_NullOwner_Throws()
    {
        Action act = () => NamedCardFactory.Create("Lightning Bolt", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_BlankName_Throws(string? name)
    {
        Action act = () => NamedCardFactory.Create(name!, _alice);
        act.Should().Throw<ArgumentException>();
    }
}
