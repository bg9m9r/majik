using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Search;

public class LibraryPickAgentTests
{
    private static Creature MakeCreature(Player owner, string name)
    {
        var c = new Creature(name, "", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    [Fact]
    public void SearchLibrarySpell_PicksFirstCandidateByDefault()
    {
        var caster = new Player("A", 20);
        var a = MakeCreature(caster, "Ant");
        var b = MakeCreature(caster, "Bee");
        var c = MakeCreature(caster, "Cat");
        caster.Zones.Library.AddCard(a);
        caster.Zones.Library.AddCard(b);
        caster.Zones.Library.AddCard(c);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        var spell = SearchSpellFactory.SearchLibrarySpell(caster, "creature");
        foreach (var fx in spell.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty)))
        {
            fx.Execute();
        }

        caster.Zones.Hand.GetCards().Select(x => x.Name)
            .Should().ContainSingle().Which.Should().Be("Ant");
    }

    [Fact]
    public void SearchLibrarySpell_NoMatchingCandidates_LeavesHandEmpty()
    {
        var caster = new Player("A", 20);
        var land = new Land("Forest");
        land.SetOwner(caster);
        land.SetController(caster);
        caster.Zones.Library.AddCard(land);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        // "creature" predicate; library has only a Land — no candidates.
        var spell = SearchSpellFactory.SearchLibrarySpell(caster, "creature");
        foreach (var fx in spell.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty)))
        {
            fx.Execute();
        }

        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(1);
    }
}
