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
/// Tests for Rampant Growth (Tempest, {1}{G}, Sorcery).
///
/// "Search your library for a basic land card, put that card onto the
///  battlefield tapped, then shuffle." (CR 701.19a + CR 701.20a)
///
/// Distinguishing feature vs.
///  - <c>SearchForTomorrow</c> (untapped) — Rampant Growth's land enters
///    tapped.
///  - <c>Cultivate</c> / <c>Kodama's Reach</c> (two basics, one to BF
///    one to hand) — Rampant Growth fetches a single basic.
///
/// Coverage:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Resolve places a basic land onto the battlefield <b>tapped</b>.
///  - Resolve refuses nonbasic lands (Tron lands stay in library).
///  - Resolve no-ops when no basic land is in the library.
///  - Resolve no-ops when the agent declines the find (CR 701.19a).
/// </summary>
public class RampantGrowthTests
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
        var card = RampantGrowthFactory.Create(owner);

        card.Name.Should().Be("Rampant Growth");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RampantGrowth()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Rampant Growth", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Rampant Growth");
        card.ManaCost.Should().Be("{1}{G}");
    }

    [Fact]
    public void Resolve_BasicLand_EntersBattlefieldTapped()
    {
        var caster = new Player("A", 20);
        var forest = MakeBasicLand("Forest", caster, CardSubtype.Forest);
        caster.Zones.Library.AddCard(forest);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(RampantGrowthFactory.BuildSpellDefinition(caster));

        caster.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Forest");
        caster.Zones.Library.GetCards().Should().BeEmpty();

        // Rampant Growth puts the land onto the battlefield tapped
        // (printed-"tapped" rider) — distinct from Search for Tomorrow.
        var placed = caster.Zones.Battlefield.GetCards().First() as Permanent;
        placed.Should().NotBeNull();
        placed!.IsTapped.Should().BeTrue("Rampant Growth puts the land onto the battlefield tapped");
    }

    [Fact]
    public void Resolve_DoesNotPickNonbasicLand()
    {
        var caster = new Player("A", 20);
        var mine = MakeNonbasicLand("Urza's Mine", caster);
        var forest = MakeBasicLand("Forest", caster, CardSubtype.Forest);
        // Put nonbasic first so a buggy "any land" predicate would pick it.
        caster.Zones.Library.AddCard(mine);
        caster.Zones.Library.AddCard(forest);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(RampantGrowthFactory.BuildSpellDefinition(caster));

        caster.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Forest");
        caster.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Urza's Mine");
    }

    [Fact]
    public void Resolve_NoBasicLandInLibrary_IsNoOp()
    {
        var caster = new Player("A", 20);
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2);
        grizzly.SetOwner(caster);
        grizzly.SetController(caster);
        caster.Zones.Library.AddCard(grizzly);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(RampantGrowthFactory.BuildSpellDefinition(caster));

        caster.Zones.Battlefield.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(1);
    }
}
