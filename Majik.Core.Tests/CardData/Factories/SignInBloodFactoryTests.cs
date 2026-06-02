using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SignInBloodFactory"/>.
///
/// Oracle text: "Target player draws two cards and loses 2 life."
/// ({B}{B} Sorcery, Tenth Edition / many reprints)
///
/// Covers:
/// - Card identity (Sorcery, {B}{B}, black, CMC 2, owner/controller).
/// - NamedCardFactory dispatch by name.
/// - SpellDefinition shape — one 1..1 "target player" request, no modes, no X.
/// - Resolve: TARGET player (not caster) draws exactly 2 cards; library shrinks by 2.
/// - Resolve: TARGET player loses exactly 2 life (CR 119.3).
/// - Targeting self: caster draws 2 and loses 2 (same player is both caster and target).
/// - Targeting opponent: opponent draws 2 and loses 2; caster unaffected.
/// - CR 608.2b: no-op when resolved target is not a Player.
/// </summary>
[Trait("Color", "B")]
public class SignInBloodFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SignInBlood_HasSorceryShape_Black_AtCostBB()
    {
        var card = SignInBloodFactory.Create(_alice);

        card.Name.Should().Be("Sign in Blood");
        card.ManaCost.Should().Be("{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SignInBlood_SpellDefinition_HasOneTargetPlayerRequest_NoModes_NoX()
    {
        var def = SignInBloodFactory.BuildSpellDefinition(resolver: x => x);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.Description.Should().Be("target player");
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Resolve — target opponent (Bob)
    // -----------------------------------------------------------------------

    [Fact]
    public void SignInBlood_Resolve_TargetOpponent_DrawsExactlyTwoCards_LibraryShrinksByTwo()
    {
        // Bob's library = [L1, L2, L3, L4]. Hand starts empty.
        // After resolve targeting Bob: hand = [L1, L2], library = [L3, L4].
        var l1 = NewLibraryCard("L1", _bob);
        var l2 = NewLibraryCard("L2", _bob);
        var l3 = NewLibraryCard("L3", _bob);
        var l4 = NewLibraryCard("L4", _bob);

        var def = SignInBloodFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { _bob },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        _bob.Zones.Hand.GetCards().Should().HaveCount(2);
        _bob.Zones.Hand.GetCards().Should().Equal(new[] { l1, l2 });
        _bob.Zones.Library.GetCards().Should().Equal(new[] { l3, l4 });
        _bob.TriedToDrawFromEmptyLibrary.Should().BeFalse();

        l1.Zone.Should().Be(ZoneType.Hand);
        l2.Zone.Should().Be(ZoneType.Hand);
        l3.Zone.Should().Be(ZoneType.Library);
        l4.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void SignInBlood_Resolve_TargetOpponent_LosesTwoLife_CR119_3()
    {
        NewLibraryCard("L1", _bob);
        NewLibraryCard("L2", _bob);

        var def = SignInBloodFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { _bob },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        _bob.LifeTotal.Should().Be(18,
            "target player loses 2 life (CR 119.3)");
        _alice.LifeTotal.Should().Be(20,
            "caster is unaffected when targeting the opponent");
    }

    // -----------------------------------------------------------------------
    // Resolve — targeting self (Alice casts and targets herself)
    // -----------------------------------------------------------------------

    [Fact]
    public void SignInBlood_Resolve_TargetSelf_CasterDrawsTwoAndLosesTwoLife()
    {
        // Alice targets herself — she draws 2 and loses 2 life.
        NewLibraryCard("A1", _alice);
        NewLibraryCard("A2", _alice);

        var def = SignInBloodFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { _alice },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2,
            "caster draws 2 when they target themselves");
        _alice.LifeTotal.Should().Be(18,
            "caster loses 2 life when they target themselves (CR 119.3)");
        _bob.LifeTotal.Should().Be(20,
            "opponent is unaffected");
    }

    // -----------------------------------------------------------------------
    // Resolve — CR 608.2b: illegal target no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void SignInBlood_Resolve_IllegalTarget_NoOp_CR608_2b()
    {
        // If the resolved object is not a Player (e.g. a creature slipped in
        // after targeting was declared), the effect should no-op per
        // CR 608.2b (do as much as possible — but non-player targets are
        // unaffected by draw/life-loss).
        var hippo = new Creature("Watchwolf", "{G}{W}", 3, 3);
        hippo.SetOwner(_bob);
        hippo.SetController(_bob);

        var def = SignInBloodFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { hippo },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        var act = () => { foreach (var effect in effects) effect.Execute(); };

        act.Should().NotThrow("CR 608.2b — illegal target resolves as no-op");
        _alice.LifeTotal.Should().Be(20, "caster unaffected on no-op");
        _bob.LifeTotal.Should().Be(20, "opponent unaffected on no-op");
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ICard NewLibraryCard(string name, Player owner)
    {
        var c = new Sorcery(name, "{0}") { Owner = owner, Controller = owner };
        c.SetZone(ZoneType.Library);
        owner.Zones.Library.AddCard(c);
        return c;
    }
}
