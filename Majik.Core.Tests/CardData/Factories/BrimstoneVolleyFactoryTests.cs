using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Brimstone Volley (Innistrad / various reprints, {2}{R}, Instant).
///
/// Oracle text:
///   "Brimstone Volley deals 3 damage to any target.
///    Morbid — Brimstone Volley deals 5 damage instead if a creature died
///    this turn."
///
/// Lightning-Strike-shape burn (Fx.DealDamageAny, CR 115.3) with a
/// Tragic-Slip-style Morbid upgrade keyed off
/// <see cref="TurnState.CreaturesDiedThisTurn"/> (CR 700.6).
///
/// Covers:
///   - Card identity (Instant, {2}{R}, owner/controller) + dispatch.
///   - Spell definition shape: 1..1 "any target".
///   - Base clause: no creature died → 3 damage to player.
///   - Morbid clause: a creature died this turn → 5 damage to player.
///   - Morbid clause: 5 damage to creature target.
///   - No TurnState wired (shape / dispatcher tests) → base 3 damage.
///   - IsMorbidActive helpers track CreaturesDiedThisTurn.
/// </summary>
[Trait("Color", "R")]
public class BrimstoneVolleyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly TurnState _turnState = new();

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BrimstoneVolley_Identity_InstantAt2R()
    {
        var card = BrimstoneVolleyFactory.Create(_alice);

        card.Name.Should().Be("Brimstone Volley");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Spell definition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_SingleAnyTargetRequest()
    {
        var def = BrimstoneVolleyFactory.BuildSpellDefinition(() => null, t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.Description.Should().Be("any target");
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Base clause — no creature died → 3 damage.
    // -----------------------------------------------------------------------

    [Fact]
    public void Base_DealsThreeDamageToPlayer_WhenNoCreatureDied()
    {
        Resolve(_bob, morbidActive: false);

        _bob.LifeTotal.Should().Be(17, "Brimstone Volley deals 3 damage when Morbid inactive");
    }

    [Fact]
    public void Base_DealsThreeDamageToCreature_WhenNoCreatureDied()
    {
        var bear = NewCreature(_bob, "Grizzly Bears", 2, 2);

        ResolveCreature(bear, morbidActive: false);

        bear.Damage.Should().Be(3, "Brimstone Volley deals 3 damage to creature without Morbid");
    }

    // -----------------------------------------------------------------------
    // Morbid clause — a creature died this turn → 5 damage instead.
    // -----------------------------------------------------------------------

    [Fact]
    public void Morbid_DealsFiveDamageToPlayer_WhenCreatureDiedThisTurn()
    {
        Resolve(_bob, morbidActive: true);

        // CR 700.6 — Morbid: a creature died this turn → 5 damage.
        _bob.LifeTotal.Should().Be(15, "Brimstone Volley deals 5 damage with Morbid active");
    }

    [Fact]
    public void Morbid_DealsFiveDamageToCreature_WhenCreatureDiedThisTurn()
    {
        var bear = NewCreature(_bob, "Grizzly Bears", 2, 2);

        ResolveCreature(bear, morbidActive: true);

        bear.Damage.Should().Be(5, "Brimstone Volley deals 5 damage to creature with Morbid");
    }

    // -----------------------------------------------------------------------
    // No TurnState wired → base 3 damage.
    // -----------------------------------------------------------------------

    [Fact]
    public void NoTurnStateWired_FallsBackToThreeDamage()
    {
        var startingLife = _bob.LifeTotal;

        // Null TurnState resolver (shape / dispatcher path) → Morbid inactive.
        var def = BrimstoneVolleyFactory.BuildSpellDefinition(() => null, t => t);
        ExecuteDefinition(def, _bob);

        _bob.LifeTotal.Should().Be(startingLife - 3, "no TurnState wired → base 3 damage");
    }

    // -----------------------------------------------------------------------
    // IsMorbidActive helpers
    // -----------------------------------------------------------------------

    [Fact]
    public void IsMorbidActive_TracksCreaturesDiedThisTurn()
    {
        BrimstoneVolleyFactory.IsMorbidActive(() => _turnState).Should().BeFalse();

        _turnState.RecordCreatureDied(_alice);

        BrimstoneVolleyFactory.IsMorbidActive(() => _turnState).Should().BeTrue(
            "any creature dying this turn enables Morbid (CR 700.6)");
    }

    [Fact]
    public void IsMorbidActive_NoTurnStateWired_ReturnsFalse()
    {
        BrimstoneVolleyFactory.IsMorbidActive(() => null).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Creature NewCreature(Player owner, string name, int power, int toughness)
    {
        var c = new Creature(name, "{1}{G}", power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private void Resolve(Player target, bool morbidActive)
    {
        if (morbidActive)
            _turnState.RecordCreatureDied(_bob);

        var def = BrimstoneVolleyFactory.BuildSpellDefinition(
            turnStateResolver: () => _turnState,
            targetResolver: t => t);

        ExecuteDefinition(def, target);
    }

    private void ResolveCreature(Creature target, bool morbidActive)
    {
        if (morbidActive)
            _turnState.RecordCreatureDied(_bob);

        var def = BrimstoneVolleyFactory.BuildSpellDefinition(
            turnStateResolver: () => _turnState,
            targetResolver: t => t);

        ExecuteDefinition(def, target);
    }

    private static void ExecuteDefinition(SpellDefinition def, object target)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }
}
