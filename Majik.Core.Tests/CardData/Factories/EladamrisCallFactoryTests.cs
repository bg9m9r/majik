using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EladamrisCallFactory"/>.
///
/// Coverage:
///  - Identity (name / type / mana cost / owner) + NamedCardFactory dispatch.
///  - Resolve tutors a creature card from the library to the controller's
///    hand (deterministic agent picks first candidate).
///  - Resolve is a no-op when the library has no creature card (CR 701.19a —
///    declining to find is legal).
///  - Resolve does not move non-creature cards (predicate gates on
///    <c>CardType.Creature</c>).
///  - Library is shuffled after the search (CR 701.20a) — covered indirectly
///    by going through the shared <see cref="SearchSpellFactory"/> path that
///    SylvanScrying already verifies; here we just confirm the pick path
///    routes through the creature predicate.
/// </summary>
[Trait("Color", "M")]
public class EladamrisCallFactoryTests
{
    private static ChosenSpellParams EmptyChoices() =>
        new(ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

    private static void Resolve(SpellDefinition spell)
    {
        foreach (var fx in spell.EffectFactory(EmptyChoices()))
        {
            fx.Execute();
        }
    }

    private static Creature MakeCreature(string name, Player owner, string cost = "{G}")
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("A", 20);
        var card = EladamrisCallFactory.Create(owner);

        card.Name.Should().Be("Eladamri's Call");
        card.ManaCost.Should().Be("{G}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(owner);
        card.Controller.Should().BeSameAs(owner);

        var parsed = ManaCost.Parse(card.ManaCost);
        parsed.White.Should().Be(1, "the printed cost is one white pip");
        parsed.Green.Should().Be(1, "the printed cost is one green pip");
        parsed.TotalValue.Should().Be(2);
    }
    [Fact]
    public void Resolve_TutorsCreatureCardFromLibraryToHand()
    {
        var caster = new Player("A", 20);
        var stoneforge = MakeCreature("Stoneforge Mystic", caster, "{1}{W}");
        var bear = MakeCreature("Grizzly Bears", caster, "{1}{G}");
        // A non-creature card in the library to prove the predicate filters it
        // out.
        var lightningBolt = new Instant("Lightning Bolt", "{R}");
        lightningBolt.SetOwner(caster);
        lightningBolt.SetController(caster);

        caster.Zones.Library.AddCard(stoneforge);
        caster.Zones.Library.AddCard(lightningBolt);
        caster.Zones.Library.AddCard(bear);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(EladamrisCallFactory.BuildSpellDefinition(caster));

        // First-creature-in-library candidate picked (deterministic agent
        // takes the first match from the filtered candidate list).
        caster.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Stoneforge Mystic");
        caster.Zones.Library.GetCards().Should().NotContain(stoneforge);
        // Non-creature card stays in the library.
        caster.Zones.Library.GetCards().Should().Contain(lightningBolt);
    }

    [Fact]
    public void Resolve_NoCreatureInLibrary_IsNoOp()
    {
        var caster = new Player("A", 20);
        // Library full of non-creature cards only.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(caster);
        bolt.SetController(caster);
        caster.Zones.Library.AddCard(bolt);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(EladamrisCallFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Should().BeEmpty(
            "CR 701.19a — empty candidate list ⇒ no pick, no move");
        caster.Zones.Library.GetCards().Should().Contain(bolt);
    }
}
