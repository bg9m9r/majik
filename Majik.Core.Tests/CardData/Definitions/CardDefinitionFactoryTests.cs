using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

public class CardDefinitionFactoryTests
{
    private static readonly Player Alice = new("Alice", 20);

    [Fact]
    public void FromJson_RoundTripsMinimalLand()
    {
        const string json = """
        { "name": "Plains", "types": ["Land"], "supertypes": ["Basic"], "subtypes": ["Plains"] }
        """;

        var def = CardDefinitionLoader.FromJson(json);

        def.Name.Should().Be("Plains");
        def.Types.Should().BeEquivalentTo(new[] { "Land" });
        def.Supertypes.Should().BeEquivalentTo(new[] { "Basic" });
        def.Subtypes.Should().BeEquivalentTo(new[] { "Plains" });
    }

    [Fact]
    public void FromJson_ParsesPolymorphicManaAbility()
    {
        const string json = """
        {
            "name": "Wastewood Verge",
            "types": ["Land"],
            "abilities": [
                { "kind": "mana", "produces": "G" },
                { "kind": "mana", "produces": "B" }
            ]
        }
        """;

        var def = CardDefinitionLoader.FromJson(json);

        def.Abilities.Should().HaveCount(2);
        def.Abilities.OfType<ManaAbilityDefinition>().Select(a => a.Produces)
            .Should().BeEquivalentTo(new[] { "G", "B" });
    }

    [Fact]
    public void Build_LandWithManaAbilities_ProducesLandWithTwoManaAbilities()
    {
        var def = new CardDefinition
        {
            Name = "Wastewood Verge",
            Types = new List<string> { "Land" },
            Abilities = new List<AbilityDefinition>
            {
                new ManaAbilityDefinition { Produces = "G" },
                new ManaAbilityDefinition { Produces = "B" },
            },
        };

        var card = CardDefinitionFactory.Build(def, Alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Wastewood Verge");
        card.Owner.Should().BeSameAs(Alice);
        card.Controller.Should().BeSameAs(Alice);
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void Build_CreatureWithoutPower_Throws()
    {
        var def = new CardDefinition
        {
            Name = "Broken Bear",
            Types = new List<string> { "Creature" },
            ManaCost = "1G",
            // Power + Toughness missing — must fail loudly, not silently
            // default to zero (which would create a creature that immediately
            // dies to state-based actions).
        };

        Action call = () => CardDefinitionFactory.Build(def, Alice);
        call.Should().Throw<ArgumentException>().WithMessage("*missing required 'power'*");
    }

    [Fact]
    public void Build_MultiTypeCard_AddsSecondaryTypes()
    {
        var def = new CardDefinition
        {
            Name = "Walking Ballista",
            Types = new List<string> { "Creature", "Artifact" },
            Subtypes = new List<string> { "Construct" },
            ManaCost = "XX",
            Power = 0,
            Toughness = 0,
        };

        var card = CardDefinitionFactory.Build(def, Alice);

        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSubtype(CardSubtype.Construct).Should().BeTrue();
    }

    [Fact]
    public void Build_UnknownType_Throws()
    {
        var def = new CardDefinition
        {
            Name = "Mystery",
            Types = new List<string> { "Vehicle" }, // not in CardType enum
        };

        Action call = () => CardDefinitionFactory.Build(def, Alice);
        call.Should().Throw<ArgumentException>().WithMessage("*Unknown card type*");
    }

    [Fact]
    public void FromEmbeddedResource_LoadsWastewoodVerge()
    {
        // Sanity check the production wiring: the JSON file is bundled
        // and the loader resolves it via the canonical slug.
        var def = CardDefinitionLoader.FromEmbeddedResource("wastewood-verge");

        def.Name.Should().Be("Wastewood Verge");
        def.Types.Should().BeEquivalentTo(new[] { "Land" });
        def.Abilities.Should().HaveCount(2);
    }

    [Fact]
    public void FromEmbeddedResource_MissingSlug_Throws()
    {
        Action call = () => CardDefinitionLoader.FromEmbeddedResource("does-not-exist");
        call.Should().Throw<FileNotFoundException>();
    }
}
