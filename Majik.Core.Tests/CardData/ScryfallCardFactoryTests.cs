using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class ScryfallCardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Create_LightningBolt_ReturnsInstant_WithCorrectCost()
    {
        var factory = NewFactory(new()
        {
            ["Lightning Bolt"] = new CardEntity
            {
                Name = "Lightning Bolt",
                ManaCost = "{R}",
                TypeLine = "Instant",
                OracleText = "Lightning Bolt deals 3 damage to any target.",
            },
        });

        var card = factory.Create("Lightning Bolt", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Lightning Bolt");
        card.ManaCost.Should().Be("R");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_GrizzlyBears_BuildsCreatureWithPT()
    {
        var factory = NewFactory(new()
        {
            ["Grizzly Bears"] = new CardEntity
            {
                Name = "Grizzly Bears",
                ManaCost = "{1}{G}",
                TypeLine = "Creature — Bear",
                Power = "2", Toughness = "2",
            },
        });

        var card = factory.Create("Grizzly Bears", _alice);

        card.Should().BeOfType<Creature>();
        var c = (Creature)card;
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.ManaCost.Should().Be("1G");
    }

    [Fact]
    public void Create_BasicMountain_AttachesManaAbility()
    {
        var factory = NewFactory(new()
        {
            ["Mountain"] = new CardEntity
            {
                Name = "Mountain",
                TypeLine = "Basic Land — Mountain",
            },
        });

        var card = factory.Create("Mountain", _alice);

        card.Should().BeOfType<Land>();
        card.HasSupertype(CardSupertype.Basic).Should().BeTrue();
        card.Abilities.OfType<IManaAbility>().Should().ContainSingle();
    }

    [Fact]
    public void Create_UnknownName_ReturnsVanillaShellWithName()
    {
        var factory = NewFactory(new());

        var card = factory.Create("Made Up Card", _alice);

        card.Name.Should().Be("Made Up Card");
        card.Abilities.Should().BeEmpty();
    }

    [Fact]
    public void Create_NonNumericPower_DefaultsToZero()
    {
        var factory = NewFactory(new()
        {
            ["Tarmogoyf"] = new CardEntity
            {
                Name = "Tarmogoyf",
                ManaCost = "{1}{G}",
                TypeLine = "Creature — Lhurgoyf",
                Power = "*", Toughness = "1+*",
            },
        });

        var card = (Creature)factory.Create("Tarmogoyf", _alice);

        card.Power.Should().Be(0);
        card.Toughness.Should().Be(0);
    }

    [Fact]
    public void Create_Planeswalker_BuildsWithLoyalty()
    {
        var factory = NewFactory(new()
        {
            ["Jace, the Mind Sculptor"] = new CardEntity
            {
                Name = "Jace, the Mind Sculptor",
                ManaCost = "{2}{U}{U}",
                TypeLine = "Legendary Planeswalker — Jace",
                Loyalty = 3,
            },
        });

        var card = factory.Create("Jace, the Mind Sculptor", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    private static ScryfallCardFactory NewFactory(Dictionary<string, CardEntity> rows) =>
        new(new InMemRepo(rows));

    private sealed class InMemRepo : ICardRepository
    {
        private readonly Dictionary<string, CardEntity> _by;
        public InMemRepo(Dictionary<string, CardEntity> by) { _by = by; }
        public CardEntity? GetByName(string name) =>
            _by.TryGetValue(name, out var e) ? e : null;

        public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null, IReadOnlyList<int>? cmcBuckets = null)
            => throw new NotImplementedException();

        public bool IsImplemented(string name) => throw new NotImplementedException();

        public void SetImplemented(string name, bool value) => throw new NotImplementedException();
    }
}
