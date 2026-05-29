using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Wish (Mystery Booster Playtest / "The List", {2}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-05-29):
///   "You may play a card you own from outside the game this turn."
///
/// Sibling of the Judgment / Future Sight wish cycle (Burning / Cunning /
/// Glittering / Living / Death Wish). Like Mastermind's Acquisition mode 2
/// and Death Wish, Wish has no type / colour filter — its predicate is
/// <see cref="Majik.Core.Effects.WishTutorEffect.Predicates.AnyCard"/>.
///
/// v1 posture (same as every wish-cycle factory — see
/// <see cref="BurningWishFactory"/> for the shared deferral notes):
/// "play a card ... from outside the game this turn" is modelled as the
/// supported wishboard → hand tutor primitive. The engine has no
/// "grant permission to play from outside the game for the rest of the
/// turn" duration-permission hook, so the observationally-equivalent
/// fetch-to-hand path is used (CR 408 — wishboard aliases the sideboard).
///
/// Coverage mirrors the cycle's four checks:
///   1. Identity — name, cost, type, controller.
///   2. <see cref="NamedCardFactory"/> dispatch (source-gen via
///      <see cref="CardNameAttribute"/>).
///   3. <c>BuildDefinition</c> shape — no modes, no target requests.
///   4. Resolve produces the expected any-card wishboard pick.
///
/// AgentRegistry is process-global; tests Clear() on dispose so they don't
/// leak agents into neighbouring suites.
/// </summary>
public class WishFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    public WishFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    private static ChosenSpellParams Empty(Player[] all) => new(
        ModeIndex: null,
        X: null,
        Targets: Array.Empty<IReadOnlyList<object>>(),
        Mana: ManaPayment.Empty,
        AllPlayers: all);

    [Fact]
    public void Wish_HasSorceryShape_At2R()
    {
        var card = WishFactory.Create(_alice);

        card.Name.Should().Be("Wish");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(3);
        Majik.Core.Cards.CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Wish_NamedCardFactory_Dispatches()
    {
        var dispatched = NamedCardFactory.Create("Wish", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Wish");
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void Wish_BuildDefinition_Shape()
    {
        var def = WishFactory.BuildDefinition(_alice);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty(
            "wish-tutor resolves via the wishboard pile, not a cast-time target");
    }

    [Fact]
    public void Wish_ResolvesAnyPick_FromWishboard()
    {
        // No type filter — any card you own from outside the game is a
        // candidate. Deterministic first-pick with no agent registered.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Wishboard.AddCard(bolt);

        var def = WishFactory.BuildDefinition(_alice);
        var effects = def.EffectFactory(Empty(new[] { _alice, _bob }));
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(bolt,
            "any-card predicate picks the only wishboard candidate");
        _alice.Wishboard.GetCards().Should().NotContain(bolt);
    }

    [Fact]
    public void Wish_EmptyWishboard_ResolvesAsNoOp()
    {
        var def = WishFactory.BuildDefinition(_alice);
        foreach (var e in def.EffectFactory(Empty(new[] { _alice, _bob }))) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no candidates → CR 408 wish resolves as a clean no-op");
    }
}
