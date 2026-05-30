using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Tarmogoyf — Creature — Lhurgoyf {1}{G},
/// "Tarmogoyf's power is equal to the number of card types among cards
/// in all graveyards, and its toughness is equal to that number plus 1."
/// CR 604.3 / 613.2 — Layer 7a characteristic-defining P/T.
///
/// Validates the layer ordering:
///   * 7a sets P/T from the live graveyard-type count on every Compute.
///   * 7c +1/+1 anthems / pump stack on top.
///   * 7c counter-postlude (CR 613.7) stacks last.
///
/// Tarmogoyf is the canonical CDA test card and drives the
/// <see cref="CdaPowerToughnessEffect"/> evaluator path end-to-end.
/// </summary>
public class TarmogoyfTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;

    public TarmogoyfTests()
    {
        // Wire the effects service to the bus so its CR-613 memoization cache
        // invalidates on game events (matches production GameDependencies).
        _effects = new ContinuousEffectsService(_bus);
        _zones = new ZoneService(_bus);
    }

    private Func<IEnumerable<ICard>> AllGraveyards => () =>
        _alice.Zones.Graveyard.GetCards()
            .Concat(_bob.Zones.Graveyard.GetCards());

    private Creature WireTarmogoyf(Player owner)
    {
        var goyf = TarmogoyfFactory.Create(owner, _effects, _bus, AllGraveyards);
        goyf.ActiveEffects = _effects;
        return goyf;
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Tarmogoyf_IsLhurgoyfCreature_AtCost1G()
    {
        var goyf = TarmogoyfFactory.Create(_alice);

        goyf.Name.Should().Be("Tarmogoyf");
        goyf.HasType(CardType.Creature).Should().BeTrue();
        goyf.HasSubtype(CardSubtype.Lhurgoyf).Should().BeTrue();
        goyf.ManaCost.Should().Be("{1}{G}");
        goyf.BasePower.Should().Be(0);
        goyf.BaseToughness.Should().Be(1);
        goyf.Owner.Should().BeSameAs(_alice);
        goyf.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Tarmogoyf()
    {
        var goyf = NamedCardFactory.Create("Tarmogoyf", _alice);

        goyf.Should().BeOfType<Creature>();
        goyf.Name.Should().Be("Tarmogoyf");
        goyf.HasSubtype(CardSubtype.Lhurgoyf).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Layer 7a — CDA P/T tracks graveyard contents live
    // -----------------------------------------------------------------------

    [Fact]
    public void Tarmogoyf_NoGraveyardCards_Is_0_1()
    {
        var goyf = WireTarmogoyf(_alice);
        _zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _alice);

        goyf.Power.Should().Be(0);
        goyf.Toughness.Should().Be(1);
    }

    [Fact]
    public void Tarmogoyf_OneInstantInGraveyard_Is_1_2()
    {
        var goyf = WireTarmogoyf(_alice);
        _zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _alice);

        var lightningBolt = new Card("Lightning Bolt", "R", new[] { CardType.Instant });
        lightningBolt.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(lightningBolt);

        goyf.Power.Should().Be(1);
        goyf.Toughness.Should().Be(2);
    }

    [Fact]
    public void Tarmogoyf_FiveCardTypesAcrossGraveyards_Is_5_6()
    {
        var goyf = WireTarmogoyf(_alice);
        _zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Distribute 5 distinct card types across both players' graveyards.
        // Distinctness is counted across the union, so duplicates in either
        // graveyard don't bump the count.
        var creatureCard = new Card("Grizzly Bears", "1G", new[] { CardType.Creature });
        var instantCard = new Card("Counterspell", "UU", new[] { CardType.Instant });
        var sorceryCard = new Card("Wrath of God", "2WW", new[] { CardType.Sorcery });
        var artifactCard = new Card("Sol Ring", "1", new[] { CardType.Artifact });
        var enchantmentCard = new Card("Pacifism", "1W", new[] { CardType.Enchantment });
        // Duplicate instant in opponent's graveyard — must NOT inflate count.
        var duplicateInstant = new Card("Bolt", "R", new[] { CardType.Instant });

        foreach (var c in new[] { creatureCard, instantCard, sorceryCard })
        {
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c);
        }
        foreach (var c in new[] { artifactCard, enchantmentCard, duplicateInstant })
        {
            c.SetOwner(_bob);
            _bob.Zones.Graveyard.AddCard(c);
        }

        goyf.Power.Should().Be(5);
        goyf.Toughness.Should().Be(6);
    }

    // -----------------------------------------------------------------------
    // Layer ordering — 7a is overwritten by nothing in 7a, but 7c stacks
    // -----------------------------------------------------------------------

    [Fact]
    public void Tarmogoyf_PlusOneCounter_Stacks_OnTopOf_Cda()
    {
        var goyf = WireTarmogoyf(_alice);
        _zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _alice);

        // One creature in graveyard → CDA = 1/2.
        var dead = new Card("Bear", "1G", new[] { CardType.Creature });
        dead.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(dead);

        // +1/+1 counter applied via CR 613.7 postlude — runs after 7a.
        goyf.Counters.Add(CounterType.PlusOnePlusOne);

        goyf.Power.Should().Be(2);
        goyf.Toughness.Should().Be(3);
    }

    [Fact]
    public void Tarmogoyf_AnthemPump_Stacks_OnTopOf_Cda()
    {
        var goyf = WireTarmogoyf(_alice);
        _zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Zero cards in graveyard → CDA = 0/1.
        // Register a Layer 7c +1/+1 anthem-style pump (e.g. Glorious
        // Anthem-style effect). 7a sets, 7c adds — result 1/2.
        _effects.Register(new TestAnthemPumpL7c(goyf, 1, 1));

        goyf.Power.Should().Be(1);
        goyf.Toughness.Should().Be(2);
    }

    /// <summary>
    /// Minimal Layer 7c pump test double — same shape as Glorious Anthem
    /// or Giant Growth. Lives inline so the test doesn't depend on any
    /// particular anthem factory.
    /// </summary>
    private sealed class TestAnthemPumpL7c : ContinuousEffect
    {
        private readonly Creature _target;
        private readonly int _p;
        private readonly int _t;

        public TestAnthemPumpL7c(Creature target, int p, int t)
        {
            _target = target;
            _p = p;
            _t = t;
        }

        public override Layer Layer => Layer.PT_Modify;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);

        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Power += _p;
            chars.Toughness += _t;
        }
    }
}
