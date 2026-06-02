using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Selfless Spirit (Eldritch Moon, {1}{W}).
///
/// Covers:
///   - Card shape: name, type, Spirit subtype, P/T 2/1, mana cost.
///   - Flying keyword marker.
///   - Single activated ability (sacrifice self → team indestructible EOT).
///   - Resolve semantics: every controller's creature on the battlefield
///     gains "Indestructible" via a registered
///     <see cref="GrantKeywordUntilEndOfTurnEffect"/>.
///   - Opponent creatures are NOT granted indestructible.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "W")]
public class SelflessSpiritFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SelflessSpirit_IsCreature_Spirit_2_1_AtCost1W()
    {
        var c = SelflessSpiritFactory.Create(_alice);

        c.Name.Should().Be("Selfless Spirit");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SelflessSpirit_HasFlying()
    {
        var c = SelflessSpiritFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
    }

    [Fact]
    public void SelflessSpirit_HasOneActivatedAbility()
    {
        var c = SelflessSpiritFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void SelflessSpirit_Activate_SacrificesSelf()
    {
        var c = SelflessSpiritFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        c.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(c);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(c);
    }

    [Fact]
    public void SelflessSpirit_Activate_GrantsIndestructibleToControllersCreaturesEOT()
    {
        var svc = new ContinuousEffectsService();
        var c = SelflessSpiritFactory.Create(_alice, svc);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        // Friendly creature that should get indestructible.
        var ally = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = svc;

        // Opponent creature that should NOT get indestructible.
        var enemy = new Creature("Lightning Bolt Target", "{G}", 1, 1);
        enemy.SetOwner(_bob);
        enemy.SetController(_bob);
        enemy.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enemy);
        enemy.ActiveEffects = svc;

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // Selfless Spirit was sacrificed and is in the graveyard — it should
        // NOT have indestructible (and the grant should never have targeted it).
        c.Zone.Should().Be(ZoneType.Graveyard);

        // Friendly creature has Indestructible from the registered EOT grant.
        svc.Compute(ally).Keywords.Should().Contain("Indestructible");

        // Enemy creature is untouched — Selfless Spirit's anthem keys on
        // "creatures YOU control" (the activator's battlefield only).
        svc.Compute(enemy).Keywords.Should().NotContain("Indestructible");
    }

    [Fact]
    public void SelflessSpirit_Activate_NoServiceSupplied_StillSacrificesNoGrants()
    {
        // Shape-path Create — no continuous-effects service.
        var c = SelflessSpiritFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var ally = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // Sacrifice still happens (closure performs the zone move directly).
        c.Zone.Should().Be(ZoneType.Graveyard);
        // No grants register (no layers service) — ally's keyword set is
        // whatever its base abilities give it (none here).
        ally.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().NotContain("Indestructible");
    }
}
