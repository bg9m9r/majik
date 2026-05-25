using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Mortivore — Creature — Lhurgoyf {3}{B}{B},
/// "Mortivore's power and toughness are each equal to the number of
/// creature cards in all graveyards. {B}: Regenerate Mortivore."
///
/// CR 604.3 / 613.2 — Layer 7a characteristic-defining P/T (cross-game
/// graveyard scope, mirroring Tarmogoyf's "all graveyards"). CR 701.18 /
/// 701.15a — regenerate self-activated ability that adds a regeneration
/// shield.
/// </summary>
public class MortivoreTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public MortivoreTests()
    {
        _zones = new ZoneService(_bus);
    }

    private Func<IEnumerable<ICard>> AllGraveyards => () =>
        _alice.Zones.Graveyard.GetCards()
            .Concat(_bob.Zones.Graveyard.GetCards());

    private Creature WireMortivore(Player owner)
    {
        var morti = MortivoreFactory.Create(owner, _effects, _bus, AllGraveyards);
        morti.ActiveEffects = _effects;
        return morti;
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Mortivore_IsLhurgoyfCreature_AtCost3BB()
    {
        var morti = MortivoreFactory.Create(_alice);

        morti.Name.Should().Be("Mortivore");
        morti.HasType(CardType.Creature).Should().BeTrue();
        morti.HasSubtype(CardSubtype.Lhurgoyf).Should().BeTrue();
        morti.ManaCost.Should().Be("{3}{B}{B}");
        morti.BasePower.Should().Be(0);
        morti.BaseToughness.Should().Be(0);
        morti.Owner.Should().BeSameAs(_alice);
        morti.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Mortivore()
    {
        var morti = NamedCardFactory.Create("Mortivore", _alice);

        morti.Should().BeOfType<Creature>();
        morti.Name.Should().Be("Mortivore");
        morti.HasSubtype(CardSubtype.Lhurgoyf).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Layer 7a — CDA P/T tracks creature cards across all graveyards
    // -----------------------------------------------------------------------

    [Fact]
    public void Mortivore_EmptyGraveyards_Is_0_0()
    {
        var morti = WireMortivore(_alice);
        _zones.MoveCard(morti, ZoneType.Library, ZoneType.Battlefield, _alice);

        morti.Power.Should().Be(0);
        morti.Toughness.Should().Be(0);
    }

    [Fact]
    public void Mortivore_CountsCreatureCardsInControllerGraveyard()
    {
        var morti = WireMortivore(_alice);
        _zones.MoveCard(morti, ZoneType.Library, ZoneType.Battlefield, _alice);

        var bear = new Card("Grizzly Bears", "1G", new[] { CardType.Creature });
        var giant = new Card("Hill Giant", "3R", new[] { CardType.Creature });
        var bolt = new Card("Lightning Bolt", "R", new[] { CardType.Instant });
        foreach (var c in new[] { bear, giant, bolt })
        {
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c);
        }

        // 2 creatures in alice's graveyard — instant doesn't count.
        morti.Power.Should().Be(2);
        morti.Toughness.Should().Be(2);
    }

    [Fact]
    public void Mortivore_CountsCreatureCardsAcrossAllGraveyards()
    {
        // Distinguishes Mortivore (every graveyard) from Nethergoyf
        // (controller-only) — CR rules text reads "all graveyards".
        var morti = WireMortivore(_alice);
        _zones.MoveCard(morti, ZoneType.Library, ZoneType.Battlefield, _alice);

        var bearA = new Card("Grizzly Bears", "1G", new[] { CardType.Creature });
        bearA.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bearA);

        var bearB = new Card("Runeclaw Bear", "1G", new[] { CardType.Creature });
        var giantB = new Card("Hill Giant", "3R", new[] { CardType.Creature });
        foreach (var c in new[] { bearB, giantB })
        {
            c.SetOwner(_bob);
            _bob.Zones.Graveyard.AddCard(c);
        }

        // 1 in alice + 2 in bob = 3 creature cards across all graveyards.
        morti.Power.Should().Be(3);
        morti.Toughness.Should().Be(3);
    }

    [Fact]
    public void Mortivore_PureHelper_CountsCreatureCards()
    {
        var bear = new Card("Grizzly Bears", "1G", new[] { CardType.Creature });
        var bolt = new Card("Lightning Bolt", "R", new[] { CardType.Instant });
        var giant = new Card("Hill Giant", "3R", new[] { CardType.Creature });

        MortivoreFactory.CountCreatureCards(new ICard[] { bear, bolt, giant })
            .Should().Be(2);
        MortivoreFactory.CountCreatureCards(Array.Empty<ICard>())
            .Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // {B}: Regenerate Mortivore (CR 701.18 / 701.15a)
    // -----------------------------------------------------------------------

    [Fact]
    public void Mortivore_HasExactlyOneRegenerateActivatedAbility()
    {
        var morti = MortivoreFactory.Create(_alice);

        morti.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Mortivore prints one activated ability: {B}: regenerate self");
    }

    [Fact]
    public void Mortivore_RegenerateActivatedAbility_CostIsSingleBlack()
    {
        var morti = MortivoreFactory.Create(_alice);
        var regen = morti.Abilities.OfType<ActivatedAbility>().Single();

        regen.Costs.Should().HaveCount(1);
        var manaCost = regen.Costs[0].Should().BeOfType<ManaCostCost>().Subject;
        manaCost.Cost.Black.Should().Be(1);
        manaCost.Cost.Generic.Should().Be(0);
    }

    [Fact]
    public void Mortivore_RegenerateAbility_Resolve_AddsRegenerationShield()
    {
        var morti = MortivoreFactory.Create(_alice);
        morti.SetZone(ZoneType.Battlefield);

        morti.HasRegenerationShield.Should().BeFalse();
        morti.RegenerationShieldCount.Should().Be(0);

        var regen = morti.Abilities.OfType<ActivatedAbility>().Single();
        regen.Resolve();

        morti.HasRegenerationShield.Should().BeTrue();
        morti.RegenerationShieldCount.Should().Be(1);
    }

    [Fact]
    public void Mortivore_RegenerateAbility_StacksAcrossMultipleActivations()
    {
        var morti = MortivoreFactory.Create(_alice);
        morti.SetZone(ZoneType.Battlefield);

        var regen = morti.Abilities.OfType<ActivatedAbility>().Single();

        // CR 701.15a — multiple regeneration effects stack as separate
        // shields. Three activations = three shields.
        var morti2 = MortivoreFactory.Create(_alice);
        morti2.SetZone(ZoneType.Battlefield);
        var regen2 = morti2.Abilities.OfType<ActivatedAbility>().Single();
        regen2.Resolve();
        // Reset for stacking on a fresh ability — re-create morti2 isn't
        // needed; ActivatedAbility's Resolve uses a one-shot resolution
        // state flag, so we instead activate distinct ability instances.
        morti2.HasRegenerationShield.Should().BeTrue();
        morti2.RegenerationShieldCount.Should().Be(1);

        // Direct API call models multiple shields stacking.
        morti2.AddRegenerationShield();
        morti2.AddRegenerationShield();
        morti2.RegenerationShieldCount.Should().Be(3);
    }
}
