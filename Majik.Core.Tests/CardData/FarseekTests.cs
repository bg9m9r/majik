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
/// Tests for Farseek (Champions of Kamigawa, {1}{G}, Sorcery).
///
/// "Search your library for a Plains, Island, Swamp, or Mountain card, put
///  it onto the battlefield tapped, then shuffle." (CR 701.19a + CR 701.20a)
///
/// Distinguishing feature vs. <c>RampantGrowth</c> (the analogue): Farseek
/// matches by basic LAND TYPE (the Plains/Island/Swamp/Mountain subtypes,
/// CR 305.6), not by basic-land NAME. So it can fetch a nonbasic dual /
/// shock / triome that has one of those four land types — but it can NOT
/// fetch a Forest (the fifth basic land type is excluded by the oracle).
/// Like Rampant Growth the land enters the battlefield <b>tapped</b>.
///
/// Coverage:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Resolve places a matching land onto the battlefield <b>tapped</b>.
///  - Resolve can fetch a NONBASIC land carrying one of the four types
///    (e.g. a Plains/Island dual) — broader than Rampant Growth.
///  - Resolve refuses a Forest (the excluded fifth basic land type).
///  - Resolve no-ops when no qualifying land is in the library.
/// </summary>
public class FarseekTests
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

    private static Land MakeNonbasicDual(string name, Player owner, params CardSubtype[] subtypes)
    {
        var land = new Land(name, supertypes: null, subtypes: subtypes);
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("A", 20);
        var card = FarseekFactory.Create(owner);

        card.Name.Should().Be("Farseek");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Farseek()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Farseek", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Farseek");
        card.ManaCost.Should().Be("{1}{G}");
    }

    [Fact]
    public void Resolve_BasicLandWithMatchingType_EntersBattlefieldTapped()
    {
        var caster = new Player("A", 20);
        var island = MakeBasicLand("Island", caster, CardSubtype.Island);
        caster.Zones.Library.AddCard(island);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(FarseekFactory.BuildSpellDefinition(caster));

        caster.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Island");
        caster.Zones.Library.GetCards().Should().BeEmpty();

        var placed = caster.Zones.Battlefield.GetCards().First() as Permanent;
        placed.Should().NotBeNull();
        placed!.IsTapped.Should().BeTrue("Farseek puts the land onto the battlefield tapped");
    }

    [Fact]
    public void Resolve_FetchesNonbasicDualCarryingAType()
    {
        var caster = new Player("A", 20);
        // A nonbasic "Hallowed Fountain"-style dual carrying Plains + Island.
        var dual = MakeNonbasicDual("Hallowed Fountain", caster,
            CardSubtype.Plains, CardSubtype.Island);
        caster.Zones.Library.AddCard(dual);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(FarseekFactory.BuildSpellDefinition(caster));

        // Broader than Rampant Growth: Farseek matches by land TYPE, so a
        // nonbasic dual that has one of the four types is a legal target.
        caster.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Hallowed Fountain");
        caster.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_DoesNotPickForest()
    {
        var caster = new Player("A", 20);
        // Forest is the fifth basic land type — NOT in Farseek's list.
        var forest = MakeBasicLand("Forest", caster, CardSubtype.Forest);
        var island = MakeBasicLand("Island", caster, CardSubtype.Island);
        // Put Forest first so a buggy "any basic" predicate would pick it.
        caster.Zones.Library.AddCard(forest);
        caster.Zones.Library.AddCard(island);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(FarseekFactory.BuildSpellDefinition(caster));

        caster.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Island");
        caster.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Forest");
    }

    [Fact]
    public void Resolve_NoQualifyingLandInLibrary_IsNoOp()
    {
        var caster = new Player("A", 20);
        // Only a Forest (excluded) and a creature — nothing Farseek can take.
        var forest = MakeBasicLand("Forest", caster, CardSubtype.Forest);
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2);
        grizzly.SetOwner(caster);
        grizzly.SetController(caster);
        caster.Zones.Library.AddCard(forest);
        caster.Zones.Library.AddCard(grizzly);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(FarseekFactory.BuildSpellDefinition(caster));

        caster.Zones.Battlefield.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(2);
    }
}
