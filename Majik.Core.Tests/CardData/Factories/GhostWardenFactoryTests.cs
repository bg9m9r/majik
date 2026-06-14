using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GhostWardenFactory"/>.
///
/// Ghost Warden — Creature — Spirit 1/1, {1}{W}. Oracle (Scryfall):
///   "{T}: Target creature gets +1/+1 until end of turn."
///
/// Covers card identity, the single {T}-cost targeted-pump activated ability,
/// its 1..1 target-creature request, and the Layer-7c +1/+1 pump landing on the
/// chosen creature with CR 514.2 end-of-turn expiry.
/// </summary>
public class GhostWardenFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ActivatedAbility PumpAbility(Creature warden)
        => warden.Abilities.OfType<ActivatedAbility>().Single(a => a is not IManaAbility);

    [Fact]
    public void GhostWarden_IdentityIsCorrect()
    {
        var c = GhostWardenFactory.Create(_alice);

        c.Name.Should().Be("Ghost Warden");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
    }

    [Fact]
    public void GhostWarden_HasSingleTapPumpAbility_WithTargetCreatureRequest()
    {
        var c = GhostWardenFactory.Create(_alice);
        var ability = PumpAbility(c);

        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(cost => cost.CostType == AdditionalCostType.Tap,
                "the only cost is {T}");
        ability.TargetRequests.Should().ContainSingle();
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public async Task GhostWarden_Pump_GivesChosenCreaturePlusOnePlusOne_UntilEndOfTurn()
    {
        var alice = new Player("Alice", 20);
        var effects = new ContinuousEffectsService();

        var warden = GhostWardenFactory.Create(alice);
        warden.SetController(alice);
        warden.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(warden);
        warden.ClearSummoningSickness();

        var ally = new Creature("Ally Bear", "{1}{G}", 2, 2);
        ally.SetOwner(alice);
        ally.SetController(alice);
        ally.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = effects;

        var ability = PumpAbility(warden);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ally } });

        var powerBefore = ally.GetPower();
        var toughnessBefore = ally.GetToughness();
        await ability.ResolveAsync(agent: null, game: null);

        ally.GetPower().Should().Be(powerBefore + 1,
            "the targeted creature gets +1 power until end of turn (CR 611 Layer 7c)");
        ally.GetToughness().Should().Be(toughnessBefore + 1,
            "the targeted creature gets +1 toughness until end of turn");

        effects.ExpireEndOfTurn();
        ally.GetPower().Should().Be(powerBefore,
            "the +1/+1 expires in the cleanup step (CR 514.2)");
        ally.GetToughness().Should().Be(toughnessBefore);
    }
}
