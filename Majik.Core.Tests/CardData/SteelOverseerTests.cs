using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SteelOverseerFactory"/>.
///
/// Card: Steel Overseer — Artifact Creature — Construct {2} 1/1
/// (Magic 2011).
///   "{T}: Put a +1/+1 counter on each artifact creature you control."
///
/// Covers:
///   - Identity, NamedCardFactory dispatch.
///   - Activated ability tap cost wiring.
///   - Activated resolve puts +1/+1 counter on every controlled artifact
///     creature (Steel Overseer included — it's its own target per CR
///     608.2 / printed wording).
///   - Non-artifact controlled creatures aren't touched.
///   - Opponent's artifact creatures aren't touched.
///   - Activation with no other artifact creatures still pumps Steel
///     Overseer itself (printed scope).
///   - Hardened-Scales-shaped replacement bus integration bumps each
///     placement (when supplied).
/// </summary>
public class SteelOverseerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    [Fact]
    public void SteelOverseer_Identity()
    {
        var c = SteelOverseerFactory.Create(_alice);

        c.Name.Should().Be("Steel Overseer");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue("Construct is the printed creature subtype");
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SteelOverseer_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Steel Overseer", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Steel Overseer");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {T}: +1/+1 counter activated ability is attached");
    }

    [Fact]
    public void ActivatedAbility_HasTapCost()
    {
        var overseer = SteelOverseerFactory.Create(_alice);

        var activated = overseer.Abilities.OfType<ActivatedAbility>().Single();
        activated.Costs.Should().ContainSingle(c => c is AdditionalCost,
            "the only printed cost on Steel Overseer's activated ability is the tap symbol");
    }

    [Fact]
    public void Activated_Resolve_PumpsSteelOverseerAndOtherArtifactCreatures()
    {
        var overseer = SteelOverseerFactory.Create(_alice);
        PutOnBattlefield(_alice, overseer);

        // Two other artifact creatures (Frogmite-shape).
        var frogmite = new Creature("Frogmite", "{4}", 2, 2);
        frogmite.AddCardType(CardType.Artifact);
        PutOnBattlefield(_alice, frogmite);

        var enforcer = new Creature("Myr Enforcer", "{7}", 4, 4);
        enforcer.AddCardType(CardType.Artifact);
        PutOnBattlefield(_alice, enforcer);

        // Pre-state: zero counters everywhere.
        overseer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        frogmite.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        enforcer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);

        var activated = overseer.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        overseer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Steel Overseer is an artifact creature you control — pumps itself");
        frogmite.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        enforcer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void Activated_Resolve_DoesNotTouchNonArtifactCreatures()
    {
        var overseer = SteelOverseerFactory.Create(_alice);
        PutOnBattlefield(_alice, overseer);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(_alice, bear);

        var activated = overseer.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        overseer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "Grizzly Bears is not an artifact creature — not pumped");
    }

    [Fact]
    public void Activated_Resolve_DoesNotTouchOpponentsArtifactCreatures()
    {
        var overseer = SteelOverseerFactory.Create(_alice);
        PutOnBattlefield(_alice, overseer);

        var opponentArtifactCreature = new Creature("Frogmite", "{4}", 2, 2);
        opponentArtifactCreature.AddCardType(CardType.Artifact);
        PutOnBattlefield(_bob, opponentArtifactCreature);

        var activated = overseer.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        overseer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        opponentArtifactCreature.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "Opponent's artifact creature isn't touched — printed text says 'you control'");
    }

    [Fact]
    public void Activated_Resolve_NoOtherArtifactCreatures_StillPumpsSelf()
    {
        var overseer = SteelOverseerFactory.Create(_alice);
        PutOnBattlefield(_alice, overseer);

        var activated = overseer.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        overseer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "with no other artifact creatures, Steel Overseer still pumps itself");
    }

    [Fact]
    public void Activated_Resolve_RoutesThroughReplacementBus_HonoursHardenedScalesShape()
    {
        // When a ReplacementBus is supplied, the activated ability routes
        // its counter placements through CountersService.Add. A Hardened
        // Scales-shaped replacement that bumps +1/+1 counter intents on
        // Alice-controlled creatures by 1 should turn every "+1" into "+2".
        var bus = new ReplacementBus();
        var overseer = SteelOverseerFactory.Create(_alice, bus);
        PutOnBattlefield(_alice, overseer);

        var hardenedScales = HardenedScalesFactory.Create(_alice, bus);
        PutOnBattlefield(_alice, hardenedScales);

        var frogmite = new Creature("Frogmite", "{4}", 2, 2);
        frogmite.AddCardType(CardType.Artifact);
        PutOnBattlefield(_alice, frogmite);

        var activated = overseer.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        overseer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "Hardened Scales bumps +1 → +2 on Steel Overseer itself");
        frogmite.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "Hardened Scales bumps +1 → +2 on Frogmite as well");
    }
}
