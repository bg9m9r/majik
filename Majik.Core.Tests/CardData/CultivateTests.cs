using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Cultivate (Magic 2010, {2}{G}, Sorcery).
///
/// "Search your library for up to two basic land cards, reveal those
///  cards, put one onto the battlefield tapped and the other into your
///  hand, then shuffle." (CR 701.19a + CR 701.20a)
///
/// Coverage:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Resolve: two basics in library → first to battlefield <b>tapped</b>,
///    second to hand, library empty afterward.
///  - Resolve: only one basic available → goes to battlefield tapped.
///  - Resolve: zero basics → no-op.
///  - Resolve: ignores nonbasic lands.
/// </summary>
public class CultivateTests
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

    private static Land MakeBasicLand(string name, Player owner, CardSubtype subtype)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { subtype });
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    private static Land MakeNonbasicLand(string name, Player owner)
    {
        var land = new Land(name, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("A", 20);
        var card = CultivateFactory.Create(owner);

        card.Name.Should().Be("Cultivate");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Cultivate()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Cultivate", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Cultivate");
        card.ManaCost.Should().Be("{2}{G}");
    }

    [Fact]
    public void Resolve_TwoBasicsAvailable_OneToBattlefieldTappedOneToHand()
    {
        var caster = new Player("A", 20);
        var forest = MakeBasicLand("Forest", caster, CardSubtype.Forest);
        var mountain = MakeBasicLand("Mountain", caster, CardSubtype.Mountain);
        caster.Zones.Library.AddCard(forest);
        caster.Zones.Library.AddCard(mountain);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(CultivateFactory.BuildSpellDefinition(caster));

        // First pick (Forest in iteration order) -> battlefield tapped.
        caster.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Forest");
        var placed = caster.Zones.Battlefield.GetCards().First() as Permanent;
        placed.Should().NotBeNull();
        placed!.IsTapped.Should().BeTrue("Cultivate puts the first basic onto the battlefield tapped");

        // Second pick (Mountain) -> hand.
        caster.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Mountain");

        caster.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_OneBasicAvailable_GoesToBattlefieldTapped()
    {
        // "Up to two" — the agent finds the one basic that exists.
        // Helper routes the single pick to battlefield tapped (see
        // SearchUpToTwoBasicsBattlefieldAndHandSpell docs).
        var caster = new Player("A", 20);
        var forest = MakeBasicLand("Forest", caster, CardSubtype.Forest);
        caster.Zones.Library.AddCard(forest);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(CultivateFactory.BuildSpellDefinition(caster));

        caster.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Forest");
        var placed = caster.Zones.Battlefield.GetCards().First() as Permanent;
        placed!.IsTapped.Should().BeTrue();
        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NoBasicsInLibrary_IsNoOp()
    {
        var caster = new Player("A", 20);
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2);
        grizzly.SetOwner(caster);
        grizzly.SetController(caster);
        caster.Zones.Library.AddCard(grizzly);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(CultivateFactory.BuildSpellDefinition(caster));

        caster.Zones.Battlefield.GetCards().Should().BeEmpty();
        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void Resolve_NonbasicLandsAreIgnored()
    {
        // Predicate is "basic land" — Tron lands stay home.
        var caster = new Player("A", 20);
        var mine = MakeNonbasicLand("Urza's Mine", caster);
        var forest = MakeBasicLand("Forest", caster, CardSubtype.Forest);
        caster.Zones.Library.AddCard(mine);
        caster.Zones.Library.AddCard(forest);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(CultivateFactory.BuildSpellDefinition(caster));

        // Only Forest is eligible — it goes to battlefield tapped, no
        // second pick available, Mine stays in library.
        caster.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Forest");
        caster.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Urza's Mine");
        caster.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
