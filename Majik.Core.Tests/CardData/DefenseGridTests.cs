using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Defense Grid (Urza's Legacy, Artifact {2}).
///
/// Oracle (verified against Scryfall):
///   "Each spell costs {3} more to cast except during its controller's turn."
///
/// "Its controller" = the controller of the spell being cast. A spell is
/// exempt from the +{3} only when it is cast during its own controller's
/// turn — i.e. when the spell's caster is the active player. On any other
/// player's turn the spell costs {3} more (CR 117.7 / CR 601.2f).
///
/// Coverage:
///   * Identity: Artifact {2} named "Defense Grid" with the spell
///     cost-increase rider attached.
///   * Dispatch through <see cref="NamedCardFactory"/>.
///   * Shape-only Create(Player): no active-player context, so the rider taxes
///     every spell (+{3}) — the conservative null-context fallback that mirrors
///     Damping Sphere's null-TurnState path.
///   * Wired Create(Player, activePlayerProvider): a spell cast during its
///     controller's turn (caster == active player) is NOT taxed; a spell cast
///     during any other player's turn IS taxed +{3}.
///   * Coloured pips are untouched (CR 117.7c).
///   * Defense Grid leaves the battlefield → cost increase is inert.
/// </summary>
public class DefenseGridTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasCorrectCardShape()
    {
        var grid = DefenseGridFactory.Create(_alice);

        grid.Name.Should().Be("Defense Grid");
        grid.HasType(CardType.Artifact).Should().BeTrue();
        grid.ManaCost.Should().Be("{2}");
        grid.ManaCostValue.Generic.Should().Be(2);
        grid.Owner.Should().BeSameAs(_alice);
        grid.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasSpellCostIncreaseAbility()
    {
        var grid = DefenseGridFactory.Create(_alice);

        grid.Abilities.OfType<SpellCostIncreaseAbility>()
            .Should().HaveCount(1,
                "the spell cost-increase rider must be attached");
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsDefenseGridShape()
    {
        var card = NamedCardFactory.Create("Defense Grid", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Defense Grid");
        card.Abilities.OfType<SpellCostIncreaseAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Create_NullOwner_Throws()
    {
        Action act = () => DefenseGridFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Shape-only Create(Player) — no active-player context → tax every spell.
    // -----------------------------------------------------------------------

    [Fact]
    public void ShapeOnly_TaxesEverySpell_WhenNoActivePlayerContext()
    {
        var grid = DefenseGridFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(grid);
        grid.SetZone(ZoneType.Battlefield);

        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(_bob);
        counterspell.SetController(_bob);

        var effective = CostReduction.GetEffectiveCost(
            counterspell, _bob, new[] { _alice, _bob });

        effective.Generic.Should().Be(3,
            "shape-only Create has no active-player context, so the +{3} always applies");
        effective.Blue.Should().Be(2, "coloured pips are untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(5);
    }

    // -----------------------------------------------------------------------
    // Wired Create(Player, activePlayerProvider) — "except during its
    // controller's turn" (CR 117.7 / CR 601.2f).
    // -----------------------------------------------------------------------

    private (Player alice, Player bob, Artifact grid) SetupWiredGrid(Func<Player?> activePlayer)
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var grid = DefenseGridFactory.Create(alice, activePlayer);
        alice.Zones.Battlefield.AddCard(grid);
        grid.SetZone(ZoneType.Battlefield);

        return (alice, bob, grid);
    }

    [Fact]
    public void SpellCastDuringOwnControllersTurn_NotTaxed()
    {
        Player? active = null;
        var (alice, bob, _) = SetupWiredGrid(() => active);

        // Bob casts on Bob's own turn — exempt.
        active = bob;
        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });

        effective.Generic.Should().Be(0,
            "the spell is cast during its controller's (Bob's) turn — exempt from the +{3}");
        effective.Blue.Should().Be(2);
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void SpellCastDuringAnotherPlayersTurn_TaxedThreeMore()
    {
        Player? active = null;
        var (alice, bob, _) = SetupWiredGrid(() => active);

        // Bob casts during Alice's turn — taxed +{3}.
        active = alice;
        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });

        effective.Generic.Should().Be(3,
            "the spell is cast during another player's (Alice's) turn — +{3} applies");
        effective.Blue.Should().Be(2, "coloured pips are untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(5);
    }

    [Fact]
    public void ControllerOwnSpell_DuringControllersTurn_NotTaxed_Symmetric()
    {
        Player? active = null;
        var (alice, _, _) = SetupWiredGrid(() => active);

        // Defense Grid's own controller casts on their own turn — exempt too.
        active = alice;
        var wrath = new Sorcery("Wrath of God", "{2}{W}{W}");
        wrath.SetOwner(alice);
        wrath.SetController(alice);

        var effective = CostReduction.GetEffectiveCost(
            wrath, alice, new[] { alice });

        effective.Generic.Should().Be(2,
            "Alice casts on her own turn — exempt; printed {2} generic unchanged");
        effective.White.Should().Be(2, "coloured pips untouched");
        effective.TotalValue.Should().Be(4);
    }

    [Fact]
    public void ControllerSpell_DuringOpponentsTurn_TaxedThreeMore()
    {
        Player? active = null;
        var (alice, bob, _) = SetupWiredGrid(() => active);

        // Defense Grid's controller (Alice) casts an instant during Bob's
        // turn — Defense Grid is symmetric, so Alice's spell is taxed too.
        active = bob;
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(alice);
        bolt.SetController(alice);

        var effective = CostReduction.GetEffectiveCost(
            bolt, alice, new[] { alice, bob });

        effective.Generic.Should().Be(3,
            "Alice casts during Bob's turn — not Alice's turn, so +{3} applies");
        effective.Red.Should().Be(1, "coloured pip untouched");
        effective.TotalValue.Should().Be(4);
    }

    [Fact]
    public void GridLeavesBattlefield_CostIncreaseBecomesInert()
    {
        Player? active = null;
        var (alice, bob, grid) = SetupWiredGrid(() => active);

        alice.Zones.Battlefield.RemoveCard(grid);
        alice.Zones.Graveyard.AddCard(grid);
        grid.SetZone(ZoneType.Graveyard);

        // Bob casts during Alice's turn — would be taxed if Grid were out.
        active = alice;
        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(bob);
        counterspell.SetController(bob);

        var effective = CostReduction.GetEffectiveCost(
            counterspell, bob, new[] { alice, bob });

        effective.Generic.Should().Be(0,
            "Defense Grid is no longer on the battlefield — rider must be inert");
        effective.TotalValue.Should().Be(2);
    }
}
