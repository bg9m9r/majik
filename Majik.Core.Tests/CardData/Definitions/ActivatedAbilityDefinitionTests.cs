using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

public class ActivatedAbilityDefinitionTests
{
    private static readonly Player Alice = new("Alice", 20);

    [Fact]
    public void FromJson_ParsesActivatedAbility_WithManaCost_AndPutCounterEffect()
    {
        const string json = """
        {
            "name": "Test Card",
            "types": ["Creature"],
            "manaCost": "2",
            "power": 0,
            "toughness": 0,
            "abilities": [
                {
                    "kind": "activated",
                    "sorcerySpeed": true,
                    "costs": [ { "type": "mana", "amount": "4" } ],
                    "effects": [ { "type": "put_counter", "counter": "+1/+1", "amount": 1, "target": "self" } ]
                }
            ]
        }
        """;

        var def = CardDefinitionLoader.FromJson(json);

        def.Abilities.Should().HaveCount(1);
        var ability = def.Abilities[0].Should().BeOfType<ActivatedAbilityDefinition>().Subject;
        ability.SorcerySpeed.Should().BeTrue();
        ability.Costs.Should().HaveCount(1);
        ability.Costs[0].Should().BeOfType<ManaCostDef>().Subject.Amount.Should().Be("4");
        ability.Effects.Should().HaveCount(1);
        var effect = ability.Effects[0].Should().BeOfType<PutCounterEffectDef>().Subject;
        effect.Counter.Should().Be("+1/+1");
        effect.Amount.Should().Be(1);
        effect.Target.Should().Be("self");
    }

    [Fact]
    public void Build_ActivatedAbility_WithMana_AddsToCard()
    {
        var def = new CardDefinition
        {
            Name = "Test Card",
            Types = new List<string> { "Creature" },
            ManaCost = "2",
            Power = 1,
            Toughness = 1,
            Abilities = new List<AbilityDefinition>
            {
                new ActivatedAbilityDefinition
                {
                    Costs = new List<CostDefinition> { new ManaCostDef { Amount = "4" } },
                    Effects = new List<EffectDefinition>
                    {
                        new PutCounterEffectDef { Counter = "+1/+1", Amount = 1, Target = "self" },
                    },
                },
            },
        };

        var card = CardDefinitionFactory.Build(def, Alice);

        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
        var activated = card.Abilities.OfType<ActivatedAbility>().Single();
        activated.Costs.OfType<ManaCostCost>().Should().HaveCount(1);
    }

    [Fact]
    public void Build_PutCounterEffect_AddsCounterToCard_OnResolve()
    {
        var def = new CardDefinition
        {
            Name = "Test Card",
            Types = new List<string> { "Creature" },
            ManaCost = "2",
            Power = 1,
            Toughness = 1,
            Abilities = new List<AbilityDefinition>
            {
                new ActivatedAbilityDefinition
                {
                    Costs = new List<CostDefinition> { new ManaCostDef { Amount = "0" } },
                    Effects = new List<EffectDefinition>
                    {
                        new PutCounterEffectDef { Counter = "+1/+1", Amount = 2, Target = "self" },
                    },
                },
            },
        };

        var card = (Creature)CardDefinitionFactory.Build(def, Alice);
        var activated = card.Abilities.OfType<ActivatedAbility>().Single();

        // Directly invoke each effect's Execute to verify the closure
        // actually mutates this specific card's counters.
        foreach (var effect in activated.Effects)
        {
            effect.Execute();
        }

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    [Fact]
    public void Build_RemoveCounterCost_BindsToCorrectPermanent()
    {
        var def = new CardDefinition
        {
            Name = "Test Card",
            Types = new List<string> { "Creature" },
            ManaCost = "2",
            Power = 1,
            Toughness = 1,
            Abilities = new List<AbilityDefinition>
            {
                new ActivatedAbilityDefinition
                {
                    Costs = new List<CostDefinition>
                    {
                        new RemoveCounterCostDef { Counter = "+1/+1", Amount = 1, From = "self" },
                    },
                    Effects = new List<EffectDefinition>
                    {
                        new DealDamageEffectDef { Amount = 1, Target = "any" },
                    },
                },
            },
        };

        var card = (Creature)CardDefinitionFactory.Build(def, Alice);
        var activated = card.Abilities.OfType<ActivatedAbility>().Single();

        activated.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Should().HaveCount(1);
    }

    [Fact]
    public void Build_RemoveCounterCost_UnsupportedCounter_Throws()
    {
        var def = new CardDefinition
        {
            Name = "Test Card",
            Types = new List<string> { "Creature" },
            ManaCost = "2",
            Power = 1,
            Toughness = 1,
            Abilities = new List<AbilityDefinition>
            {
                new ActivatedAbilityDefinition
                {
                    Costs = new List<CostDefinition>
                    {
                        new RemoveCounterCostDef { Counter = "Loyalty", From = "self" },
                    },
                    Effects = new List<EffectDefinition>(),
                },
            },
        };

        Action call = () => CardDefinitionFactory.Build(def, Alice);
        call.Should().Throw<NotSupportedException>().WithMessage("*Loyalty*");
    }

    [Fact]
    public void Build_PutCounterEffect_NonSelfTarget_Throws()
    {
        var def = new CardDefinition
        {
            Name = "Test Card",
            Types = new List<string> { "Creature" },
            ManaCost = "2",
            Power = 1,
            Toughness = 1,
            Abilities = new List<AbilityDefinition>
            {
                new ActivatedAbilityDefinition
                {
                    Costs = new List<CostDefinition>(),
                    Effects = new List<EffectDefinition>
                    {
                        new PutCounterEffectDef { Counter = "+1/+1", Target = "target_creature" },
                    },
                },
            },
        };

        Action call = () => CardDefinitionFactory.Build(def, Alice);
        call.Should().Throw<NotSupportedException>().WithMessage("*target_creature*");
    }
}
