using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SignInBloodFactory"/>.
///
/// Card: Sign in Blood — Sorcery {B}{B} (Magic 2010).
///   "Target player draws two cards and loses 2 life."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - Spell shape: one 1..1 "target player" request, no modes.
///   - Resolve targeting opponent → opponent draws 2, loses 2.
///   - Resolve targeting self → self draws 2, loses 2 (legal —
///     CR 115.6, "target player" with no restriction).
///   - Empty library on target: draw short-circuits + SBA flag;
///     life loss still applies.
///   - Illegal target (non-Player resolver result): no-op.
/// </summary>
public class SignInBloodTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SignInBlood_Identity_Sorcery_BB()
    {
        var c = SignInBloodFactory.Create(_alice);

        c.Name.Should().Be("Sign in Blood");
        c.ManaCost.Should().Be("{B}{B}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SignInBlood()
    {
        var card = NamedCardFactory.Create("Sign in Blood", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Sign in Blood");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{B}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpellDefinition_HasSinglePlayerTarget_NoModes()
    {
        var def = SignInBloodFactory.BuildSpellDefinition(t => t);

        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("target player");
    }

    [Fact]
    public void Resolve_TargetingOpponent_OpponentDrawsTwoAndLosesTwo()
    {
        SeedLibrary(_bob, 5);

        var def = SignInBloodFactory.BuildSpellDefinition(t => t);
        Resolve(def, _bob);

        _bob.Zones.Hand.Count.Should().Be(SignInBloodFactory.CardsDrawn);
        _bob.Zones.Library.Count.Should().Be(3);
        _bob.LifeTotal.Should().Be(20 - SignInBloodFactory.LifeLost);

        // Caster untouched.
        _alice.Zones.Hand.Count.Should().Be(0);
        _alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Resolve_TargetingSelf_SelfDrawsTwoAndLosesTwo()
    {
        SeedLibrary(_alice, 5);

        var def = SignInBloodFactory.BuildSpellDefinition(t => t);
        Resolve(def, _alice);

        _alice.Zones.Hand.Count.Should().Be(SignInBloodFactory.CardsDrawn);
        _alice.LifeTotal.Should().Be(20 - SignInBloodFactory.LifeLost);
    }

    [Fact]
    public void Resolve_EmptyLibrary_StillLosesLife_AndFlagsDrawFromEmpty()
    {
        // No library seeding for Bob.
        var def = SignInBloodFactory.BuildSpellDefinition(t => t);
        Resolve(def, _bob);

        _bob.Zones.Hand.Count.Should().Be(0);
        _bob.LifeTotal.Should().Be(20 - SignInBloodFactory.LifeLost);
        _bob.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    [Fact]
    public void Resolve_NonPlayerResolverResult_NoOp()
    {
        // Resolver returns the literal raw object — a string sentinel
        // simulates a stale handle that no longer resolves to a Player.
        var def = SignInBloodFactory.BuildSpellDefinition(_ => "not a player");
        Resolve(def, "stale-handle");

        _alice.LifeTotal.Should().Be(20);
        _bob.LifeTotal.Should().Be(20);
        _alice.Zones.Hand.Count.Should().Be(0);
        _bob.Zones.Hand.Count.Should().Be(0);
    }

    // ---- Helpers ----

    private static void Resolve(SpellDefinition def, object target)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new System.Collections.Generic.IReadOnlyList<object>[] { new[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private static void SeedLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = new Card($"L{i}", "");
            c.SetOwner(p);
            c.SetZone(ZoneType.Library);
            p.Zones.Library.AddCard(c);
        }
    }
}
