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
    public void Pick_OppHasBigThreat_PrefersRemoval_WhenCtxPassed()
    {
        // Opp has a 4/4 in play. Library pick between a vanilla creature
        // and a removal-shaped instant should snap to removal once we
        // thread the GameContext (which exposes AllPlayers) through.
        var scn = new BotTestScenario();
        for (int i = 0; i < 3; i++) scn.AddLandToBattlefield(scn.Self, "Mountain");
        scn.AddCreatureToBattlefield(scn.Opponent, "Ogre", power: 4, toughness: 4);

        var bystander = new Creature("Goblin", "{R}", power: 2, toughness: 2);
        var removal = MakeInstant("Bolt", "{R}", "Destroy");

        var pick = LibraryPickPolicy.Pick(
            scn.Self,
            new ICard[] { bystander, removal },
            "card", ArchetypeWeights.Burn,
            ctx: scn.Context);
        pick.Should().BeSameAs(removal);
    }

    [Fact]
    public void Pick_NoOppThreat_PrefersDrawWhenLowHand_WhenCtxPassed()
    {
        // Opp board empty + own hand size 1 (just the BotTestScenario
        // default — Self has no cards in hand). Policy should reach for
        // the card-draw spell over a vanilla creature when LowHand fires.
        var scn = new BotTestScenario();
        for (int i = 0; i < 3; i++) scn.AddLandToBattlefield(scn.Self, "Mountain");
        // Opponent has nothing in play -> OppHasBigThreat = false.

        var bystander = new Creature("Goblin", "{R}", power: 2, toughness: 2);
        var draw = MakeInstant("Brainstorm", "{U}", "Draw");

        var pick = LibraryPickPolicy.Pick(
            scn.Self,
            new ICard[] { bystander, draw },
            "card", ArchetypeWeights.BorosEnergy,
            ctx: scn.Context);
        pick.Should().BeSameAs(draw);
    }

    [Fact]
    public void Pick_OppHasBigThreat_AndCtxNull_FallsBackToNeutral()
    {
        // Same opp board, but ctx == null -> policy can't read opponent
        // state and shouldn't snap to removal. Verifies the legacy
        // call path (LibrarySpellFactory before this PR) still gets the
        // pre-ctx neutral pick.
        var scn = new BotTestScenario();
        for (int i = 0; i < 3; i++) scn.AddLandToBattlefield(scn.Self, "Mountain");
        scn.AddCreatureToBattlefield(scn.Opponent, "Ogre", power: 4, toughness: 4);

        var creature = new Creature("Cub", "{G}", power: 2, toughness: 2);
        var removal = MakeInstant("Bolt", "{R}", "Destroy");

        // ctx == null -> ProbeOpponent returns (0, false) -> the OppHasBigThreat
        // bonus does NOT fire; Burn weights still rate the removal spell
        // ahead of the creature because of its archetype bias, so we use
        // BorosEnergy here where the bias is more even.
        var pick = LibraryPickPolicy.Pick(
            scn.Self,
            new ICard[] { creature, removal },
            "card", ArchetypeWeights.BorosEnergy,
            ctx: null);
        // Without opp signal, BorosEnergy weights pick the 2/2 creature
        // over a single-target removal instant.
        pick.Should().BeSameAs(creature);
    }

    [Fact]
    public void Pick_BoardBehind_PrefersStrongCreature_WhenCtxPassed()
    {
        // Self has no creatures; opp has a 3/3. Board-behind + ctx-aware
        // policy should pick the strong creature over a vanilla 1/1.
        var scn = new BotTestScenario();
        for (int i = 0; i < 4; i++) scn.AddLandToBattlefield(scn.Self, "Mountain");
        scn.AddCreatureToBattlefield(scn.Opponent, "Knight", power: 3, toughness: 3);

        var chump = new Creature("Squire", "{W}", power: 1, toughness: 1);
        var beater = new Creature("Ogre", "{2}{R}", power: 4, toughness: 4);

        var pick = LibraryPickPolicy.Pick(
            scn.Self,
            new ICard[] { chump, beater },
            "creature card", ArchetypeWeights.BorosEnergy,
            ctx: scn.Context);
        pick.Should().BeSameAs(beater);
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
