using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Nethergoyf — Creature — Lhurgoyf {B},
/// "Nethergoyf's power is equal to the number of card types among cards
/// in your graveyard and its toughness is equal to that number plus 1."
/// Escape—{2}{B}, exile any number of other cards with four or more
/// card types among them (v1 fixed-4 stub).
/// CR 604.3 / 613.2 — Layer 7a characteristic-defining P/T.
///
/// Mirrors <see cref="TarmogoyfTests"/> but scoped to the controller's
/// own graveyard (Tarmogoyf scans every graveyard; Nethergoyf only the
/// controller's). The CDA evaluator is sampled live on every Compute via
/// <see cref="Card.Controller"/>, so a control swap re-points the scan.
/// </summary>
public class NethergoyfTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public NethergoyfTests()
    {
        _zones = new ZoneService(_bus);
    }

    private Creature WireNethergoyf(Player owner)
    {
        var goyf = NethergoyfFactory.Create(owner, _effects, _bus);
        goyf.ActiveEffects = _effects;
        return goyf;
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Nethergoyf_IsLhurgoyfCreature_AtCostB()
    {
        var goyf = NethergoyfFactory.Create(_alice);

        goyf.Name.Should().Be("Nethergoyf");
        goyf.HasType(CardType.Creature).Should().BeTrue();
        goyf.HasSubtype(CardSubtype.Lhurgoyf).Should().BeTrue();
        goyf.ManaCost.Should().Be("{B}");
        goyf.BasePower.Should().Be(0);
        goyf.BaseToughness.Should().Be(1);
        goyf.Owner.Should().BeSameAs(_alice);
        goyf.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Nethergoyf()
    {
        var goyf = NamedCardFactory.Create("Nethergoyf", _alice);

        goyf.Should().BeOfType<Creature>();
        goyf.Name.Should().Be("Nethergoyf");
        goyf.HasSubtype(CardSubtype.Lhurgoyf).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Layer 7a — CDA P/T tracks controller's graveyard contents live
    // -----------------------------------------------------------------------

    [Fact]
    public void Nethergoyf_EmptyGraveyard_Is_0_1()
    {
        var goyf = WireNethergoyf(_alice);
        _zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _alice);

        goyf.Power.Should().Be(0);
        goyf.Toughness.Should().Be(1);
    }

    [Fact]
    public void Nethergoyf_FiveCardTypesInControllerGraveyard_Is_5_6()
    {
        var goyf = WireNethergoyf(_alice);
        _zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Five distinct card types in Alice's (the controller's) graveyard.
        var creatureCard = new Card("Grizzly Bears", "1G", new[] { CardType.Creature });
        var instantCard = new Card("Counterspell", "UU", new[] { CardType.Instant });
        var sorceryCard = new Card("Wrath of God", "2WW", new[] { CardType.Sorcery });
        var artifactCard = new Card("Sol Ring", "1", new[] { CardType.Artifact });
        var enchantmentCard = new Card("Pacifism", "1W", new[] { CardType.Enchantment });

        foreach (var c in new[] { creatureCard, instantCard, sorceryCard, artifactCard, enchantmentCard })
        {
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c);
        }

        goyf.Power.Should().Be(5);
        goyf.Toughness.Should().Be(6);
    }

    [Fact]
    public void Nethergoyf_OpponentGraveyardTypes_DoNotCount()
    {
        // Distinguishes Nethergoyf (controller-only) from Tarmogoyf
        // (every graveyard) — CR 109.5: "your" means the controller's.
        var goyf = WireNethergoyf(_alice);
        _zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Pile opponent's graveyard with multiple card types — should not
        // bump Nethergoyf's count.
        var oppInstant = new Card("Lightning Bolt", "R", new[] { CardType.Instant });
        var oppSorcery = new Card("Anger of the Gods", "2R", new[] { CardType.Sorcery });
        var oppCreature = new Card("Grizzly Bears", "1G", new[] { CardType.Creature });
        foreach (var c in new[] { oppInstant, oppSorcery, oppCreature })
        {
            c.SetOwner(_bob);
            _bob.Zones.Graveyard.AddCard(c);
        }

        goyf.Power.Should().Be(0, "opponent's graveyard types are excluded");
        goyf.Toughness.Should().Be(1);
    }

    [Fact]
    public void Nethergoyf_DuplicateTypesInOwnGraveyard_DoNotInflateCount()
    {
        var goyf = WireNethergoyf(_alice);
        _zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Two instants + one sorcery in Alice's graveyard → 2 distinct types.
        var bolt = new Card("Lightning Bolt", "R", new[] { CardType.Instant });
        var counter = new Card("Counterspell", "UU", new[] { CardType.Instant });
        var wrath = new Card("Wrath of God", "2WW", new[] { CardType.Sorcery });
        foreach (var c in new[] { bolt, counter, wrath })
        {
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c);
        }

        goyf.Power.Should().Be(2);
        goyf.Toughness.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Escape — printed cost surface
    // -----------------------------------------------------------------------

    [Fact]
    public void Nethergoyf_BuildAlternativeCost_ReturnsEscapeAltCost_WithPrintedShape()
    {
        var cost = NethergoyfFactory.BuildAlternativeCost();

        // v1 fixed-4 exile stub (see factory xmldoc — printed text is
        // "any number of other cards with four or more card types among
        // them"; richer predicate deferred).
        cost.ExileFromGraveyardCount.Should().Be(NethergoyfFactory.EscapeExileCount);

        // {2}{B} = 2 generic + 1 black.
        cost.AlternativeManaCost.Generic.Should().Be(2);
        cost.AlternativeManaCost.Black.Should().Be(1);
    }

    [Fact]
    public void Nethergoyf_CountDistinctCardTypesInControllerGraveyard_PureHelper()
    {
        // Pure helper — does not require a live CDA, just the count
        // helper exposed for tests / bot policies.
        var inst = new Card("Lightning Bolt", "R", new[] { CardType.Instant });
        var sorc = new Card("Wrath of God", "2WW", new[] { CardType.Sorcery });
        var art = new Card("Sol Ring", "1", new[] { CardType.Artifact });
        var ench = new Card("Pacifism", "1W", new[] { CardType.Enchantment });
        foreach (var c in new[] { inst, sorc, art, ench })
        {
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c);
        }

        NethergoyfFactory
            .CountDistinctCardTypesInControllerGraveyard(_alice)
            .Should().Be(4);
        NethergoyfFactory
            .CountDistinctCardTypesInControllerGraveyard(_bob)
            .Should().Be(0);
    }
}
