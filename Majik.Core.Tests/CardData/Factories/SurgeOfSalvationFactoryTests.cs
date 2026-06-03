using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using TargetLegality = Majik.Core.Targeting.TargetLegality;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SurgeOfSalvationFactory"/>.
///
/// Surge of Salvation (March of the Machine, {W}) — Instant. Oracle text
/// (verified against Scryfall 2026-06-02):
///   "You and permanents you control gain hexproof until end of turn. Prevent
///    all damage that black and/or red sources would deal to creatures you
///    control this turn."
/// </summary>
[Trait("Color", "W")]
public class SurgeOfSalvationFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly IDisposable _scope = PlayerStaticAbilities.PushScope();

    public void Dispose() => _scope.Dispose();

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void SurgeOfSalvation_Identity()
    {
        var c = SurgeOfSalvationFactory.Create(_alice);
        c.Name.Should().Be("Surge of Salvation");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SurgeOfSalvation_DispatchesThroughNamedFactory()
    {
        var card = NamedCardFactory.Create("Surge of Salvation", _alice);
        card.Should().BeAssignableTo<Instant>();
        card.Name.Should().Be("Surge of Salvation");
    }

    // ── Player + creature hexproof ─────────────────────────────────────────

    [Fact]
    public void SurgeOfSalvation_GrantsCasterPlayerHexproof_UntilEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var bus = new ReplacementBus();

        SurgeOfSalvationFactory.Resolve(_alice, svc, bus);

        // CR 702.11 — the caster can't be targeted by opponents.
        PlayerStaticAbilities.HasHexproof(_alice).Should().BeTrue();
        var spec = new TargetSpec("any").AnyCreatureOrPlayer();
        TargetLegality.IsLegal(spec, _alice, _bob).Should().BeFalse();
        TargetLegality.IsLegal(spec, _alice, _alice).Should().BeTrue();

        // EOT cleanup ends the grant (CR 514.2).
        svc.ExpireEndOfTurn();
        PlayerStaticAbilities.HasHexproof(_alice).Should().BeFalse();
    }

    [Fact]
    public void SurgeOfSalvation_GrantsControlledCreaturesHexproof()
    {
        var svc = new ContinuousEffectsService();
        var bus = new ReplacementBus();

        var mine = new Creature("Mine", "{W}", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        _alice.Zones.Battlefield.AddCard(mine);

        SurgeOfSalvationFactory.Resolve(_alice, svc, bus);

        var spec = new TargetSpec("creature").Creatures();
        TargetLegality.IsLegal(spec, mine, _bob).Should().BeFalse("creatures you control gain hexproof");
        TargetLegality.IsLegal(spec, mine, _alice).Should().BeTrue("hexproof only restricts opponents");
    }

    // ── Black/red damage prevention to creatures you control ───────────────

    [Fact]
    public void SurgeOfSalvation_PreventsRedDamageToControlledCreature()
    {
        var bus = new ReplacementBus();
        var svc = new ContinuousEffectsService();

        var mine = new Creature("Mine", "{W}", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var redBolt = new Instant("Bolt", "{R}") { Owner = _bob };

        SurgeOfSalvationFactory.Resolve(_alice, svc, bus);

        var prevented = bus.Apply(new DamageIntent(redBolt, 3, TargetCreature: mine));
        prevented.Should().BeNull("a red source can't damage creatures you control this turn");
    }

    [Fact]
    public void SurgeOfSalvation_PreventsBlackDamageToControlledCreature()
    {
        var bus = new ReplacementBus();
        var svc = new ContinuousEffectsService();

        var mine = new Creature("Mine", "{W}", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var blackBite = new Instant("Bite", "{B}") { Owner = _bob };

        SurgeOfSalvationFactory.Resolve(_alice, svc, bus);

        var prevented = bus.Apply(new DamageIntent(blackBite, 2, TargetCreature: mine));
        prevented.Should().BeNull("a black source can't damage creatures you control this turn");
    }

    [Fact]
    public void SurgeOfSalvation_DoesNotPreventGreenDamage()
    {
        var bus = new ReplacementBus();
        var svc = new ContinuousEffectsService();

        var mine = new Creature("Mine", "{W}", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var greenStomp = new Instant("Stomp", "{G}") { Owner = _bob };

        SurgeOfSalvationFactory.Resolve(_alice, svc, bus);

        var notPrevented = bus.Apply(new DamageIntent(greenStomp, 4, TargetCreature: mine));
        notPrevented.Should().NotBeNull("only black and/or red sources are prevented");
        notPrevented!.Amount.Should().Be(4);
    }

    [Fact]
    public void SurgeOfSalvation_DoesNotPreventRedDamageToOpponentCreature()
    {
        var bus = new ReplacementBus();
        var svc = new ContinuousEffectsService();

        var theirs = new Creature("Theirs", "{U}", 2, 2)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        var redBolt = new Instant("Bolt", "{R}") { Owner = _alice };

        SurgeOfSalvationFactory.Resolve(_alice, svc, bus);

        var notPrevented = bus.Apply(new DamageIntent(redBolt, 3, TargetCreature: theirs));
        notPrevented.Should().NotBeNull("the shield only protects creatures the caster controls");
    }

    [Fact]
    public void SurgeOfSalvation_DamageShield_ExpiresAtEndOfTurn()
    {
        var bus = new ReplacementBus();
        var svc = new ContinuousEffectsService();

        var mine = new Creature("Mine", "{W}", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var redBolt = new Instant("Bolt", "{R}") { Owner = _bob };

        SurgeOfSalvationFactory.Resolve(_alice, svc, bus);
        bus.ExpireEndOfTurn();

        var notPrevented = bus.Apply(new DamageIntent(redBolt, 3, TargetCreature: mine));
        notPrevented.Should().NotBeNull("the shield expires at end of turn (CR 514.2)");
    }
}
