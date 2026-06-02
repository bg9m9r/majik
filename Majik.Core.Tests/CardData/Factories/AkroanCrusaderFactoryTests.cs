using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AkroanCrusaderFactory"/>.
///
/// Covers:
/// - Identity (name, type Creature, P/T 1/1, Human + Soldier subtypes,
///   mana cost {R}, Haste keyword marker, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Heroic trigger fires on targeted spell from controller → creates a
///   1/1 red Soldier token with Haste.
/// - Untargeted spell from controller → no trigger.
/// - Opponent's targeted spell → no trigger (controller scope).
/// - Soldier-token shape: name, P/T, subtype, colour, Haste keyword.
/// </summary>
[Trait("Color", "R")]
public class AkroanCrusaderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public AkroanCrusaderFactoryTests()
    {
        _zones = new ZoneService(_bus);
    }

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Spark",
        params ITarget[] targets)
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller, targets);
    }

    [Fact]
    public void AkroanCrusader_Identity()
    {
        var ac = AkroanCrusaderFactory.Create(_alice);

        ac.Name.Should().Be("Akroan Crusader");
        ac.ManaCost.Should().Be("{R}");
        ac.HasType(CardType.Creature).Should().BeTrue();
        ac.HasSubtype(CardSubtype.Human).Should().BeTrue();
        ac.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        ac.BasePower.Should().Be(1);
        ac.BaseToughness.Should().Be(1);
        ac.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Haste",
                "Haste is wired as a KeywordAbility marker");
        ac.Owner.Should().BeSameAs(_alice);
        ac.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void HeroicTrigger_TargetedSpell_CreatesSoldierToken()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var ac = AkroanCrusaderFactory.Create(_alice, triggers, _zones);
        ac.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ac);

        var preCount = _alice.Zones.Battlefield.GetCards().Count();

        _bus.Publish(new SpellCastEvent(
            NewInstantSpell(_alice, "Boon", Target.Permanent(ac))));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Battlefield.GetCards().Count().Should().Be(preCount + 1,
            "exactly one Soldier token was created");

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Single(c => c.IsToken);
        token.Name.Should().Be("Soldier");
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        token.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Haste");
        CardColors.GetColors(token)
            .Should().BeEquivalentTo(new[] { ManaColor.Red },
                "1/1 RED Soldier creature token");
    }

    [Fact]
    public void HeroicTrigger_UntargetedSpell_DoesNotFire()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var ac = AkroanCrusaderFactory.Create(_alice, triggers, _zones);
        ac.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ac);

        _bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Tormenting Voice")));
        triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void HeroicTrigger_OpponentCastTargetingCrusader_DoesNotFire()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var ac = AkroanCrusaderFactory.Create(_alice, triggers, _zones);
        ac.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ac);

        var bobsSpell = new Instant("Shock", "R") { Owner = _bob };
        _bus.Publish(new SpellCastEvent(
            new Majik.Core.Spells.Spell(bobsSpell, _bob, new[] { Target.Permanent(ac) })));

        triggers.PendingCount.Should().Be(0,
            "Heroic only triggers on spells YOU cast");
    }

    [Fact]
    public void HeroicTrigger_SpellTargetingDifferentPermanent_DoesNotFire()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var ac = AkroanCrusaderFactory.Create(_alice, triggers, _zones);
        ac.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ac);

        var other = new Creature("Other", "", 2, 2) { Owner = _alice, Controller = _alice };
        _bus.Publish(new SpellCastEvent(
            NewInstantSpell(_alice, "Bolt", Target.Permanent(other))));

        triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void CreateSoldierToken_DirectHelper_HasCorrectShape()
    {
        var token = AkroanCrusaderFactory.CreateSoldierToken(_alice, _zones);

        token.IsToken.Should().BeTrue();
        token.Name.Should().Be("Soldier");
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        token.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Haste");
        CardColors.GetColors(token)
            .Should().BeEquivalentTo(new[] { ManaColor.Red });
        token.Controller.Should().BeSameAs(_alice);
    }
}
