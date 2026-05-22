using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Xunit;

namespace Majik.Bot.Tests;

public class LibraryPickPolicyTests
{
    private static Instant MakeInstant(string name, string manaCost, params string[] keywords)
    {
        var s = new Instant(name, manaCost);
        foreach (var k in keywords) s.AddAbility(new KeywordAbility(k));
        return s;
    }

    [Fact]
    public void Pick_EmptyCandidates_ReturnsNull()
    {
        var scn = new BotTestScenario();
        LibraryPickPolicy.Pick(scn.Self, Array.Empty<ICard>(), "creature card", ArchetypeWeights.Burn)
            .Should().BeNull();
    }

    [Fact]
    public void Pick_SingleCandidate_ReturnsIt()
    {
        var scn = new BotTestScenario();
        var only = MakeInstant("Solo", "{R}");
        LibraryPickPolicy.Pick(scn.Self, new ICard[] { only }, "card", ArchetypeWeights.Burn)
            .Should().BeSameAs(only);
    }

    [Fact]
    public void Pick_ManaScrewed_PrefersLand()
    {
        // Self has 0 lands in play, 0 in hand -> ManaScrewed.
        var scn = new BotTestScenario();
        var creature = new Creature("Wurm", "{4}{G}", power: 5, toughness: 5);
        var land = new Land("Forest");
        var pick = LibraryPickPolicy.Pick(scn.Self,
            new ICard[] { creature, land },
            "card", ArchetypeWeights.BorosEnergy);
        pick.Should().BeSameAs(land);
    }

    [Fact]
    public void Pick_HealthyMana_PrefersBurnSpellOverLand_ForBurnArchetype()
    {
        // Plenty of lands -> mana fixing should NOT dominate.
        var scn = new BotTestScenario();
        scn.AddLandToBattlefield(scn.Self, "Mountain");
        scn.AddLandToBattlefield(scn.Self, "Mountain");
        scn.AddLandToBattlefield(scn.Self, "Mountain");

        var burn = MakeInstant("Bolt", "{R}");
        var land = new Land("Mountain");
        var pick = LibraryPickPolicy.Pick(scn.Self,
            new ICard[] { land, burn },
            "card", ArchetypeWeights.Burn);
        // Burn archetype prefers the spell, not an extra land in a healthy game.
        pick.Should().BeSameAs(burn);
    }

    [Fact]
    public void Pick_RespectsCurveCeiling_PrefersCastableOverOversized()
    {
        // 1 land in play, 0 in hand -> ceiling ~2 mana.
        var scn = new BotTestScenario();
        scn.AddLandToBattlefield(scn.Self, "Mountain");

        var cheap = new Creature("Cub", "{G}", power: 2, toughness: 2);
        var bomb  = new Creature("Eldrazi", "{10}", power: 10, toughness: 10);

        var pick = LibraryPickPolicy.Pick(scn.Self,
            new ICard[] { bomb, cheap },
            "creature card", ArchetypeWeights.Prowess);
        pick.Should().BeSameAs(cheap);
    }

    [Fact]
    public void Pick_PrefersHigherPowerCreature_AmongCastable()
    {
        // Plenty of lands so curve is open.
        var scn = new BotTestScenario();
        for (int i = 0; i < 5; i++) scn.AddLandToBattlefield(scn.Self, "Mountain");

        var weak = new Creature("Goblin", "{R}", power: 1, toughness: 1);
        var strong = new Creature("Ogre", "{2}{R}", power: 4, toughness: 4);

        var pick = LibraryPickPolicy.Pick(scn.Self,
            new ICard[] { weak, strong },
            "creature card", ArchetypeWeights.Prowess);
        pick.Should().BeSameAs(strong);
    }

    [Fact]
    public void Pick_TieBreaks_ReturnsFirstCandidate()
    {
        // Two identical creatures -> stable tie-break to first.
        var scn = new BotTestScenario();
        for (int i = 0; i < 3; i++) scn.AddLandToBattlefield(scn.Self, "Mountain");

        var a = new Creature("Twin", "{R}", power: 2, toughness: 2);
        var b = new Creature("Twin", "{R}", power: 2, toughness: 2);

        var pick = LibraryPickPolicy.Pick(scn.Self,
            new ICard[] { a, b },
            "creature card", ArchetypeWeights.Burn);
        pick.Should().BeSameAs(a);
    }
}
