using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="UlamogsCrusherFactory"/>
/// (Rise of the Eldrazi, {8}).
///
/// Creature — Eldrazi 8/8. Oracle text:
///   "Annihilator 2
///    Ulamog's Crusher attacks each combat if able."
///
/// Covers:
///   - Identity (Creature — Eldrazi, {8}, 8/8, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Annihilator 2 marker + must-attack marker present.
///   - Annihilator <see cref="TriggeredAbility"/> is attached and fires
///     on self-attack (defending player sacrifices two permanents).
/// </summary>
public class UlamogsCrusherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void UlamogsCrusher_Identity()
    {
        var crusher = UlamogsCrusherFactory.Create(_alice);

        crusher.Name.Should().Be("Ulamog's Crusher");
        crusher.ManaCost.Should().Be("{8}");
        crusher.HasType(CardType.Creature).Should().BeTrue();
        crusher.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        crusher.BasePower.Should().Be(8);
        crusher.BaseToughness.Should().Be(8);
        crusher.Owner.Should().BeSameAs(_alice);
        crusher.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void UlamogsCrusher_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Ulamog's Crusher", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Ulamog's Crusher");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(8);
        ((Creature)card).BaseToughness.Should().Be(8);
    }

    [Fact]
    public void UlamogsCrusher_HasAnnihilator2Marker()
    {
        var crusher = UlamogsCrusherFactory.Create(_alice);
        var keywords = crusher.Abilities.OfType<KeywordAbility>().ToList();

        keywords.Should().Contain(k => k.Keyword == "Annihilator" && k.Arg == 2,
            "CR 702.86 — printed Annihilator 2");
        keywords.Should().Contain(k => k.Keyword == "AttacksEachCombat",
            "CR 702.43 — must-attack marker (combat-restriction wiring deferred)");
    }

    [Fact]
    public void UlamogsCrusher_HasAnnihilatorTrigger()
    {
        var crusher = UlamogsCrusherFactory.Create(_alice);

        var triggers = crusher.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "the Annihilator factory attaches a single attack trigger");

        // Trigger fires on self-attack.
        var trig = triggers[0];
        trig.Condition.Matches(new CreatureAttacksEvent(crusher, _bob), trig)
            .Should().BeTrue();
    }

    [Fact]
    public void UlamogsCrusher_AnnihilatorTrigger_SacrificesTwoOnAttack()
    {
        var crusher = UlamogsCrusherFactory.Create(_alice);
        // Park crusher on Alice's battlefield so the trigger's active-zone
        // check succeeds (defaults to Battlefield only).
        crusher.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(crusher);

        // Bob has 3 bears; deterministic fallback sacrifices the first two.
        var seeded = new List<Creature>();
        for (var i = 0; i < 3; i++)
        {
            var b = new Creature($"Bear{i}", "{1}{G}", 2, 2);
            b.SetOwner(_bob);
            b.SetController(_bob);
            b.SetZone(ZoneType.Battlefield);
            _bob.Zones.Battlefield.AddCard(b);
            seeded.Add(b);
        }

        var trig = crusher.Abilities.OfType<TriggeredAbility>().First();
        trig.Condition.Matches(new CreatureAttacksEvent(crusher, _bob), trig)
            .Should().BeTrue();
        foreach (var e in trig.Effects) e.Execute();

        seeded[0].Zone.Should().Be(ZoneType.Graveyard);
        seeded[1].Zone.Should().Be(ZoneType.Graveyard);
        seeded[2].Zone.Should().Be(ZoneType.Battlefield);
    }
}
