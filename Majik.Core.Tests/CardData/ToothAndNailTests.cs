using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Tooth and Nail ({5}{G}{G}{G}, Sorcery — Mirrodin).
///
/// "Choose one —
///   • Search your library for up to two creature cards, reveal them,
///     put them into your hand, then shuffle.
///   • Search your library for up to two creature cards, put them onto
///     the battlefield, then shuffle.
///  Entwine {2}{R} (Choose both if you pay the entwine cost.)"
/// (CR 700.2d / CR 701.19a / CR 701.20a)
///
/// Coverage:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Entwine marker constants are exposed.
///  - Mode 0 — tutors two creatures into hand.
///  - Mode 1 — tutors two creatures onto the battlefield.
///  - Entwine simulation — both modes resolve when ModeIndexes = {0, 1}.
///  - Up-to-two semantics: library with one creature resolves with just
///    that creature (CR 701.19a — "up to two" allows fewer finds).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class ToothAndNailTests
{
    private static ChosenSpellParams Choose(int mode) =>
        new(ModeIndex: mode, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

    private static ChosenSpellParams ChooseBoth() =>
        new(ModeIndex: 0, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            ModeIndexes: new[] { 0, 1 });

    private static void Resolve(SpellDefinition spell, ChosenSpellParams p)
    {
        foreach (var fx in spell.EffectFactory(p))
        {
            fx.Execute();
        }
    }

    private static Creature MakeCreature(string name, Player owner) =>
        new Creature(name, "1G", 2, 2) { Owner = owner, Controller = owner };

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("A", 20);
        var card = ToothAndNailFactory.Create(owner);

        card.Name.Should().Be("Tooth and Nail");
        card.ManaCost.Should().Be("{5}{G}{G}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ToothAndNail()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Tooth and Nail", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Tooth and Nail");
        card.ManaCost.Should().Be("{5}{G}{G}{G}");
    }

    [Fact]
    public void EntwineMarker_IsExposed()
    {
        // v1 ships Entwine as a marker only — no live cost-layering.
        ToothAndNailFactory.HasEntwine.Should().BeTrue();
        ToothAndNailFactory.EntwineCost.Should().Be("{2}{R}");
    }

    [Fact]
    public void Mode0_TutorsTwoCreaturesIntoHand()
    {
        var caster = new Player("A", 20);
        var ravager = MakeCreature("Arcbound Ravager", caster);
        var goyf = MakeCreature("Tarmogoyf", caster);
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(caster); bolt.SetController(caster);
        // Library order: creature, creature, instant (filtered).
        caster.Zones.Library.AddCard(ravager);
        caster.Zones.Library.AddCard(goyf);
        caster.Zones.Library.AddCard(bolt);

        AgentRegistry.Set(caster, new DeterministicBotAgent());
        GameRandomRegistry.Set(caster, new GameRandom(seed: 1));
        try
        {
            Resolve(
                ToothAndNailFactory.BuildSpellDefinition(caster),
                Choose(ToothAndNailFactory.ModeTutorToHand));

            // Both creatures landed in hand.
            caster.Zones.Hand.GetCards().Select(c => c.Name)
                .Should().BeEquivalentTo(new[] { "Arcbound Ravager", "Tarmogoyf" });
            // Library only has the filtered-out instant.
            caster.Zones.Library.GetCards().Should().ContainSingle()
                .Which.Name.Should().Be("Lightning Bolt");
        }
        finally
        {
            GameRandomRegistry.Clear();
        }
    }

    [Fact]
    public void Mode1_TutorsTwoCreaturesOntoBattlefield()
    {
        var caster = new Player("A", 20);
        var ravager = MakeCreature("Arcbound Ravager", caster);
        var goyf = MakeCreature("Tarmogoyf", caster);
        caster.Zones.Library.AddCard(ravager);
        caster.Zones.Library.AddCard(goyf);

        AgentRegistry.Set(caster, new DeterministicBotAgent());
        GameRandomRegistry.Set(caster, new GameRandom(seed: 1));
        try
        {
            Resolve(
                ToothAndNailFactory.BuildSpellDefinition(caster),
                Choose(ToothAndNailFactory.ModeTutorToBattlefield));

            // Both creatures on the battlefield, library + hand empty.
            caster.Zones.Battlefield.GetCards().Select(c => c.Name)
                .Should().BeEquivalentTo(new[] { "Arcbound Ravager", "Tarmogoyf" });
            caster.Zones.Hand.GetCards().Should().BeEmpty();
            caster.Zones.Library.GetCards().Should().BeEmpty();
        }
        finally
        {
            GameRandomRegistry.Clear();
        }
    }

    [Fact]
    public void EntwineSimulation_BothModes_Resolve()
    {
        // Entwine primitive doesn't exist yet; tests can simulate the
        // "Choose both if you pay the entwine cost" branch by passing
        // ModeIndexes = {0, 1} directly. Mode 0 puts 2 into hand, Mode 1
        // puts 2 onto the battlefield.
        var caster = new Player("A", 20);
        var c1 = MakeCreature("C1", caster);
        var c2 = MakeCreature("C2", caster);
        var c3 = MakeCreature("C3", caster);
        var c4 = MakeCreature("C4", caster);
        caster.Zones.Library.AddCard(c1);
        caster.Zones.Library.AddCard(c2);
        caster.Zones.Library.AddCard(c3);
        caster.Zones.Library.AddCard(c4);

        AgentRegistry.Set(caster, new DeterministicBotAgent());
        GameRandomRegistry.Set(caster, new GameRandom(seed: 1));
        try
        {
            Resolve(
                ToothAndNailFactory.BuildSpellDefinition(caster),
                ChooseBoth());

            // Two went to hand, two to battlefield.
            caster.Zones.Hand.GetCards().Should().HaveCount(2);
            caster.Zones.Battlefield.GetCards().Should().HaveCount(2);
            caster.Zones.Library.GetCards().Should().BeEmpty();
        }
        finally
        {
            GameRandomRegistry.Clear();
        }
    }

    [Fact]
    public void Mode0_UpToTwo_AllowsFewerFinds()
    {
        // CR 701.19a — "up to two" allows zero, one, or two finds. With
        // only one creature in the library the effect finds that one and
        // stops cleanly.
        var caster = new Player("A", 20);
        var goyf = MakeCreature("Tarmogoyf", caster);
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        forest.SetOwner(caster); forest.SetController(caster);
        caster.Zones.Library.AddCard(goyf);
        caster.Zones.Library.AddCard(forest);

        AgentRegistry.Set(caster, new DeterministicBotAgent());
        GameRandomRegistry.Set(caster, new GameRandom(seed: 1));
        try
        {
            Resolve(
                ToothAndNailFactory.BuildSpellDefinition(caster),
                Choose(ToothAndNailFactory.ModeTutorToHand));

            caster.Zones.Hand.GetCards().Should().ContainSingle()
                .Which.Name.Should().Be("Tarmogoyf");
            // The Forest stays in the library (not a creature).
            caster.Zones.Library.GetCards().Should().ContainSingle()
                .Which.Name.Should().Be("Forest");
        }
        finally
        {
            GameRandomRegistry.Clear();
        }
    }
}
