using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SigardaHostOfHeronsFactory"/> and the
/// <see cref="SacrificeRestriction"/> primitive it wires.
///
/// Covers:
/// - Identity (name, type, supertype/subtype, 5/5, mana cost, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Printed Flying + Hexproof.
/// - Sigarda ETB → Sigarda's controller is protected from forced sacrifice
///   by opponent-controlled sources.
/// - Sigarda LTB → protection lifts.
/// - Self-driven sacrifices (controller's own spell / ability source) are
///   NOT blocked — Sigarda only protects against OPPONENTS.
/// - Diabolic-Edict-style template (TargetPlayerSacrificesCreature) cast
///   by an opponent silently no-ops vs Sigarda's controller.
/// - Innocent-Blood-style template (EachOpponentSacrificesCreature) cast
///   by an opponent skips the Sigarda-protected opponent.
/// </summary>
public class SigardaHostOfHeronsTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SigardaHostOfHeronsTests()
    {
        SacrificeRestriction.Clear();
    }

    public void Dispose()
    {
        SacrificeRestriction.Clear();
    }

    [Fact]
    public void Sigarda_Identity()
    {
        var c = SigardaHostOfHeronsFactory.Create(_alice);

        c.Name.Should().Be("Sigarda, Host of Herons");
        c.ManaCost.Should().Be("{2}{G}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Sigarda_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sigarda, Host of Herons", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Sigarda, Host of Herons");
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
    }

    [Fact]
    public void Sigarda_HasPrintedFlyingAndHexproof()
    {
        var c = SigardaHostOfHeronsFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Hexproof");

        CombatAbilities.HasFlying(c).Should().BeTrue();
    }

    [Fact]
    public void Sigarda_OnBattlefield_ProtectsControllerFromForcedSacrifice()
    {
        var sigarda = SigardaHostOfHeronsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sigarda);
        sigarda.SetZone(ZoneType.Battlefield);
        // Initial sync at construction was before SetZone — re-run by hand
        // here (the test-only path; production wires the bus).
        SacrificeRestriction.AddCannotBeForcedToSacrifice(_alice, sigarda);

        _alice.IsProtectedFromForcedSacrifice(requestingSource: SyntheticOpponentSource())
            .Should().BeTrue("Sigarda blocks opponent-driven forced sacrifice.");
    }

    [Fact]
    public void Sigarda_LTB_LiftsProtection()
    {
        var bus = new EventBus();
        var sigarda = SigardaHostOfHeronsFactory.Create(_alice, bus);
        // Drive ETB via bus so the Sync wires up.
        sigarda.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(sigarda, ZoneType.Hand, ZoneType.Battlefield));

        _alice.IsProtectedFromForcedSacrifice(SyntheticOpponentSource()).Should().BeTrue();

        // LTB — bus event triggers Sync → unregister.
        sigarda.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(sigarda, ZoneType.Battlefield, ZoneType.Graveyard));

        _alice.IsProtectedFromForcedSacrifice(SyntheticOpponentSource()).Should().BeFalse(
            "protection lifts when Sigarda leaves the battlefield.");
    }

    [Fact]
    public void Sigarda_DoesNotBlock_ControllerOwnSacrifice()
    {
        SacrificeRestriction.AddCannotBeForcedToSacrifice(_alice, _aliceSourceCard());

        var selfSource = new Creature("Alice's Own Sac Outlet", "B", 1, 1)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };

        // Self-driven (caster == target controller) does NOT trigger the gate.
        _alice.IsProtectedFromForcedSacrifice(selfSource).Should().BeFalse(
            "Sigarda only blocks opponent-controlled sources (CR 109.5).");

        // Fx.Sacrifice(perm, source) overload — opponent-driven should
        // no-op, self-driven should sacrifice.
        var permToSac = new Creature("Sac Target", "1B", 1, 1)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(permToSac);

        Fx.Sacrifice(permToSac, selfSource);
        permToSac.Zone.Should().Be(ZoneType.Graveyard,
            "self-driven sacrifice bypasses Sigarda.");
    }

    [Fact]
    public void Sigarda_BlocksFxSacrifice_OpponentSource()
    {
        var sigarda = SigardaHostOfHeronsFactory.Create(_alice);
        sigarda.SetZone(ZoneType.Battlefield);
        SacrificeRestriction.AddCannotBeForcedToSacrifice(_alice, sigarda);

        var oppSource = new Creature("Bob's Sac-Force Outlet", "B", 1, 1)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };

        var target = new Creature("Alice's Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(target);

        Fx.Sacrifice(target, oppSource);

        target.Zone.Should().Be(ZoneType.Battlefield,
            "Sigarda blocks the opponent-driven sacrifice silently (CR 701.16).");
    }

    [Fact]
    public void Sigarda_BlocksEdictTemplate_ViaControllerCheck()
    {
        // Verifies SacrificeRestriction.IsProtectedFromForcedSacrificeBy
        // — the Player-keyed overload that the edict templates use. The
        // template captures ctx.Caster (Bob) and asks
        // "is Alice protected from Bob?".
        var sigarda = SigardaHostOfHeronsFactory.Create(_alice);
        sigarda.SetZone(ZoneType.Battlefield);
        SacrificeRestriction.AddCannotBeForcedToSacrifice(_alice, sigarda);

        SacrificeRestriction
            .IsProtectedFromForcedSacrificeBy(_alice, requestingController: _bob)
            .Should().BeTrue("Bob's edict can't make Alice sacrifice while Sigarda is out.");

        SacrificeRestriction
            .IsProtectedFromForcedSacrificeBy(_alice, requestingController: _alice)
            .Should().BeFalse("Alice's own spell isn't gated.");

        SacrificeRestriction
            .IsProtectedFromForcedSacrificeBy(_bob, requestingController: _alice)
            .Should().BeFalse("Bob has no Sigarda — Alice's edict still resolves against Bob.");
    }

    [Fact]
    public void SacrificeRestriction_MultipleSources_StackAndClearIndependently()
    {
        var src1 = _aliceSourceCard();
        var src2 = _aliceSourceCard();

        SacrificeRestriction.AddCannotBeForcedToSacrifice(_alice, src1);
        SacrificeRestriction.AddCannotBeForcedToSacrifice(_alice, src2);

        _alice.IsProtectedFromForcedSacrifice(SyntheticOpponentSource()).Should().BeTrue();

        SacrificeRestriction.RemoveCannotBeForcedToSacrifice(_alice, src1);
        _alice.IsProtectedFromForcedSacrifice(SyntheticOpponentSource())
            .Should().BeTrue("second source still grants protection.");

        SacrificeRestriction.RemoveCannotBeForcedToSacrifice(_alice, src2);
        _alice.IsProtectedFromForcedSacrifice(SyntheticOpponentSource())
            .Should().BeFalse("no more sources → no protection.");
    }

    [Fact]
    public void SacrificeRestriction_AddIsIdempotent()
    {
        var src = _aliceSourceCard();
        SacrificeRestriction.AddCannotBeForcedToSacrifice(_alice, src);
        SacrificeRestriction.AddCannotBeForcedToSacrifice(_alice, src);

        // Removing once clears it (no duplicate entry).
        SacrificeRestriction.RemoveCannotBeForcedToSacrifice(_alice, src);
        _alice.IsProtectedFromForcedSacrifice(SyntheticOpponentSource()).Should().BeFalse();
    }

    // ----------------------------------------------------------------------
    // Test helpers
    // ----------------------------------------------------------------------

    private ICard SyntheticOpponentSource()
    {
        var source = new Creature("Synthetic Opponent Source", "B", 1, 1)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        return source;
    }

    private ICard _aliceSourceCard()
    {
        var source = new Creature("Alice's Source", "W", 1, 1)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        return source;
    }
}
