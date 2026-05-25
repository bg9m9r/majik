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
/// Tests for Kodama's Reach (Champions of Kamigawa, {2}{G}, Sorcery — Arcane).
///
/// "Search your library for up to two basic land cards, reveal those
///  cards, put one onto the battlefield tapped and the other into your
///  hand, then shuffle." (CR 701.19a + CR 701.20a)
///
/// Kodama's Reach is a functional reprint of <see cref="CultivateFactory"/>;
/// the only printed difference is the Arcane subtype (CR 205.3k) which is
/// relevant for Splice onto Arcane (CR 702.46) targeting.
///
/// Coverage:
///  - Identity (name / type / mana cost / Arcane subtype) +
///    NamedCardFactory dispatch.
///  - Resolve: two basics → one to battlefield tapped + one to hand.
///  - Resolve: nonbasics ignored.
/// </summary>
public class KodamasReachTests
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
    public void Identity_NameTypeManaCostAndArcaneSubtype()
    {
        var owner = new Player("A", 20);
        var card = KodamasReachFactory.Create(owner);

        card.Name.Should().Be("Kodama's Reach");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        // Arcane subtype must be present so Splice onto Arcane riders
        // (CR 702.46) can target this spell on the stack.
        card.HasSubtype(CardSubtype.Arcane).Should().BeTrue("Kodama's Reach is Sorcery — Arcane");
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_KodamasReach()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Kodama's Reach", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Kodama's Reach");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasSubtype(CardSubtype.Arcane).Should().BeTrue();
    }

    [Fact]
    public void Resolve_TwoBasicsAvailable_OneToBattlefieldTappedOneToHand()
    {
        var caster = new Player("A", 20);
        var forest = MakeBasicLand("Forest", caster, CardSubtype.Forest);
        var island = MakeBasicLand("Island", caster, CardSubtype.Island);
        caster.Zones.Library.AddCard(forest);
        caster.Zones.Library.AddCard(island);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(KodamasReachFactory.BuildSpellDefinition(caster));

        caster.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Forest");
        var placed = caster.Zones.Battlefield.GetCards().First() as Permanent;
        placed!.IsTapped.Should().BeTrue();

        caster.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Island");
        caster.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NonbasicLandsAreIgnored()
    {
        var caster = new Player("A", 20);
        var tower = MakeNonbasicLand("Urza's Tower", caster);
        var forest = MakeBasicLand("Forest", caster, CardSubtype.Forest);
        caster.Zones.Library.AddCard(tower);
        caster.Zones.Library.AddCard(forest);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(KodamasReachFactory.BuildSpellDefinition(caster));

        // Only Forest qualifies as basic; it goes to battlefield tapped.
        caster.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Forest");
        caster.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Urza's Tower");
        caster.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
