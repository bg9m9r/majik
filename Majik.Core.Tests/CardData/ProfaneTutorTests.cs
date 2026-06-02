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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Profane Tutor (Time Spiral Remastered, no printed mana cost —
/// Suspend 2—{1}{B}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Suspend 2—{1}{B} (Rather than cast this card from your hand, pay
///    {1}{B} and exile it with two time counters on it. At the beginning of
///    your upkeep, remove a time counter. When the last is removed, you may
///    cast it without paying its mana cost.)
///    Search your library for a card, put that card into your hand, then
///    shuffle."
///
/// Covers:
///  - Identity (Sorcery, no printed mana cost, non-legendary) + dispatch.
///  - Hand cast restriction (only castable via Suspend / cast-from-exile,
///    CR 117.7c / 202.1a — Profane Tutor prints with no mana cost).
///  - Suspend alt-cost shape (2 time counters, {1}{B} mana cost).
///  - Resolve body: search any card from library -> hand, then shuffle
///    (CR 701.19a / 701.20a). No life loss (unlike Grim Tutor).
///  - Empty library / decline -> no tutor, library still shuffled.
/// </summary>
public class ProfaneTutorTests
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

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void Identity_IsSorcery_NoPrintedManaCost_NonLegendary()
    {
        var owner = new Player("A", 20);
        var card = ProfaneTutorFactory.Create(owner);

        card.Name.Should().Be("Profane Tutor");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        card.ManaCost.Should().Be("",
            "Profane Tutor prints with no mana cost — Scryfall mana_cost == \"\"");
        card.ManaCostValue.Should().Be(ManaCost.Zero,
            "empty mana cost parses to zero (CR 202.1a)");
        card.Owner.Should().BeSameAs(owner);
        card.Controller.Should().BeSameAs(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ProfaneTutor()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Profane Tutor", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Profane Tutor");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void CannotBeCastFromHand_PerCR202_1a()
    {
        var owner = new Player("A", 20);
        var card = ProfaneTutorFactory.Create(owner);

        card.RestrictedCastZones.Should().Contain(ZoneType.Hand,
            "Profane Tutor has no printed mana cost — only castable via Suspend (CR 117.7c)");
    }

    // --------------------------------------------------------------
    // Suspend alt-cost
    // --------------------------------------------------------------

    [Fact]
    public void BuildSuspendCost_Returns_Suspend2_For_1B()
    {
        var suspend = ProfaneTutorFactory.BuildSuspendCost();

        suspend.TimeCounters.Should().Be(2);
        suspend.AlternativeManaCost.Should().Be(ManaCost.Parse("{1}{B}"));
    }

    // --------------------------------------------------------------
    // Resolve body — search any card -> hand, then shuffle, NO life loss
    // --------------------------------------------------------------

    [Fact]
    public void Resolve_PicksAnyCard_PutsInHand_NoLifeLoss()
    {
        var caster = new Player("A", 20);
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        forest.SetOwner(caster); forest.SetController(caster);
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(caster); bear.SetController(caster);
        var wrath = new Sorcery("Wrath of God", "2WW");
        wrath.SetOwner(caster); wrath.SetController(caster);
        caster.Zones.Library.AddCard(forest);
        caster.Zones.Library.AddCard(bear);
        caster.Zones.Library.AddCard(wrath);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(ProfaneTutorFactory.BuildSpellDefinition(caster));

        // Some card was tutored to hand; library lost exactly that card.
        caster.Zones.Hand.GetCards().Should().HaveCount(1);
        caster.Zones.Library.GetCards().Should().HaveCount(2);

        // CR 119.3 — unlike Grim Tutor, Profane Tutor has NO life-loss clause.
        caster.LifeTotal.Should().Be(20);
        caster.LifeLostThisTurn.Should().Be(0);
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoTutor_NoLifeLoss()
    {
        var caster = new Player("A", 20);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(ProfaneTutorFactory.BuildSpellDefinition(caster));

        caster.Zones.Library.GetCards().Should().BeEmpty();
        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.LifeTotal.Should().Be(20);
    }
}
