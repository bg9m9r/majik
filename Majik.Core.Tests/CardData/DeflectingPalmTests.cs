using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="DeflectingPalmFactory"/> — Instant {R}{W}.
///
/// "The next time a source of your choice would deal damage to you this turn,
///  prevent that damage. If damage is prevented this way, Deflecting Palm
///  deals that much damage to that source's controller."
///
/// Covers:
///   - Identity (Instant, {R}{W}, red+white, owner / controller) +
///     NamedCardFactory dispatch (built from the embedded JSON definition).
///   - SpellDefinition shape: no modes, no X, no targets (CR 615 prevention
///     shield resolves with no chosen target at v1).
///   - Resolve: registers a one-shot prevention shield on the caster's
///     replacement bus — the next damage aimed at the caster is prevented
///     (CR 615.1).
///   - Redirect rider: the prevented amount is dealt to the source's
///     controller (CR 119) — a creature source's controller loses that life;
///     a player source bounces back to itself.
///   - One-shot: the shield fires at most once (CR 615 "the next time").
/// </summary>
public class DeflectingPalmTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = DeflectingPalmFactory.Create(_alice);

        card.Name.Should().Be("Deflecting Palm");
        card.ManaCost.Should().Be("{R}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DeflectingPalm_IsRedAndWhite()
    {
        var card = DeflectingPalmFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red, "the {R} pip makes it red");
        colors.Should().Contain(ManaColor.White, "the {W} pip makes it white");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DeflectingPalm()
    {
        var card = NamedCardFactory.Create("Deflecting Palm", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Deflecting Palm");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{R}{W}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_NoModes_NoX_NoTargets_NoAdditionalCosts()
    {
        var def = DeflectingPalmFactory.BuildSpellDefinition(_alice, new ReplacementBus());

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty(
            "the 'source of your choice' prompt is lossy at v1 — no formal target request");
        def.AdditionalCostsOrEmpty.Should().BeEmpty();
    }

    [Fact]
    public void BuildSpellDefinition_RequiresReplacementBus()
    {
        var act = () => DeflectingPalmFactory.BuildSpellDefinition(_alice, replacements: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Resolve — prevention shield + redirect rider
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_PreventsNextDamageToCaster()
    {
        var bus = new ReplacementBus();

        foreach (var e in DeflectingPalmFactory.BuildResolveEffect(_alice, bus)) e.Execute();

        // A creature source aims 3 damage at Alice (the caster) — prevented.
        var src = new Creature("Lightning-stand-in", "{R}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        bus.Apply(new DamageIntent(src, 3, TargetPlayer: _alice))
            .Should().BeNull("CR 615.1 — the next damage to the caster is prevented");
    }

    [Fact]
    public void Resolve_RedirectsPreventedDamageToSourceController()
    {
        var bus = new ReplacementBus();
        foreach (var e in DeflectingPalmFactory.BuildResolveEffect(_alice, bus)) e.Execute();

        // Bob controls the damage source — he takes the prevented amount.
        var src = new Creature("Burn-stand-in", "{R}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
        };

        bus.Apply(new DamageIntent(src, 4, TargetPlayer: _alice)).Should().BeNull();

        _bob.LifeTotal.Should().Be(16,
            "CR 119 — Deflecting Palm deals the prevented 4 to the source's controller");
        _alice.LifeTotal.Should().Be(20, "Alice took no damage — it was prevented");
    }

    [Fact]
    public void Resolve_PlayerSourceBouncesBackToItself()
    {
        var bus = new ReplacementBus();
        foreach (var e in DeflectingPalmFactory.BuildResolveEffect(_alice, bus)) e.Execute();

        // The source is Bob himself (a player source) — the redirect bounces
        // the prevented damage straight back to Bob (CR 119).
        bus.Apply(new DamageIntent(_bob, 5, TargetPlayer: _alice)).Should().BeNull();

        _bob.LifeTotal.Should().Be(15, "the prevented 5 is dealt back to the player source");
        _alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Resolve_OneShot_OnlyPreventsTheNextDamage()
    {
        var bus = new ReplacementBus();
        foreach (var e in DeflectingPalmFactory.BuildResolveEffect(_alice, bus)) e.Execute();

        var src = new Creature("Burn-stand-in", "{R}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
        };

        var first = bus.Apply(new DamageIntent(src, 3, TargetPlayer: _alice));
        var second = bus.Apply(new DamageIntent(src, 2, TargetPlayer: _alice));

        first.Should().BeNull("the FIRST qualifying damage is prevented");
        second.Should().NotBeNull("CR 615 'the next time' — the shield is one-shot");
        second!.Amount.Should().Be(2);
        _bob.LifeTotal.Should().Be(17, "only the first (3) was redirected to Bob");
    }

    [Fact]
    public void Resolve_DoesNotPreventDamageToOtherPlayers()
    {
        var bus = new ReplacementBus();
        foreach (var e in DeflectingPalmFactory.BuildResolveEffect(_alice, bus)) e.Execute();

        // Damage aimed at Bob, not the caster — shield does not engage.
        var src = new Creature("Burn-stand-in", "{R}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        var passed = bus.Apply(new DamageIntent(src, 6, TargetPlayer: _bob));

        passed.Should().NotBeNull("only damage to the caster is prevented");
        passed!.Amount.Should().Be(6);
    }
}
