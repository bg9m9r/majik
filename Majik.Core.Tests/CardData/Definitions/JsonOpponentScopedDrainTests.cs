using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Engine-level coverage for the two declarative opponent-scoped, UNTARGETED
/// rider verbs that pay down the
/// <c>opponent-scoped-deal-damage-and-life-loss-rider-shapes</c> deferral:
/// <list type="bullet">
///   <item><c>deal_damage_each_opponent</c> — "[source] deals N damage to each
///   opponent" (Impact Tremors / Witty Roastmaster), the untargeted sibling of
///   the any-target <c>deal_damage</c> verb.</item>
///   <item><c>lose_life_each_opponent</c> — "each opponent loses N life"
///   (Corpse Knight / Bastion of Remembrance), the untargeted sibling of the
///   targeted <c>lose_life_target</c> verb.</item>
/// </list>
///
/// Both are group effects (CR 608.2, no target slot); the opponent set (CR
/// 109.5) is enumerated live off <c>ctx.Game</c> at resolution, mirroring
/// <c>damage_and_tap_each_flyer_opponents_control</c>. Each test builds the
/// effect closure directly and resolves it against a live
/// <see cref="GameContext"/>, then asserts: opponents are hit, the controller
/// is NEVER hit (CR 109.5), and every opponent in a multi-opponent set is hit.
/// </summary>
public class JsonOpponentScopedDrainTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    private GameContext NewContext(params Player[] players) =>
        new(_alice, players, _alice, 1, PhaseStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(_bus));

    private Creature OnBattlefield(Player owner, string name)
    {
        var c = new Creature(name, "{B}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private async Task ResolveEffectAsync(IEffect effect, GameContext game)
    {
        var ctx = new ResolutionContext(
            Controller: _alice,
            Agent: null,
            Game: game,
            ChosenTargets: System.Array.Empty<IReadOnlyList<object>>());
        await effect.ExecuteAsync(ctx);
    }

    // ------------------------------------------------------------------
    // deal_damage_each_opponent
    // ------------------------------------------------------------------

    [Fact]
    public void DealDamageEachOpponentDef_DeclaresNoTarget()
    {
        var def = new DealDamageEachOpponentEffectDef { Amount = 1 };

        def.ToTargetRequest().Should().BeNull(
            "an each-opponent group effect (CR 608.2) announces no target");
    }

    [Fact]
    public async Task DealDamageEachOpponent_HitsOpponent_NotController()
    {
        var source = OnBattlefield(_alice, "Impact Tremors Source");
        var def = new DealDamageEachOpponentEffectDef { Amount = 1 };
        var effect = def.ToResolveEffect()(source, _alice, null, -1);

        await ResolveEffectAsync(effect, NewContext(_alice, _bob));

        _bob.LifeTotal.Should().Be(19, "the opponent takes 1 damage (CR 109.5)");
        _alice.LifeTotal.Should().Be(20, "the controller is not an opponent");
    }

    [Fact]
    public async Task DealDamageEachOpponent_HitsEveryOpponent()
    {
        var source = OnBattlefield(_alice, "Impact Tremors Source");
        var def = new DealDamageEachOpponentEffectDef { Amount = 2 };
        var effect = def.ToResolveEffect()(source, _alice, null, -1);

        await ResolveEffectAsync(effect, NewContext(_alice, _bob, _carol));

        _bob.LifeTotal.Should().Be(18);
        _carol.LifeTotal.Should().Be(18);
        _alice.LifeTotal.Should().Be(20);
    }

    // ------------------------------------------------------------------
    // lose_life_each_opponent
    // ------------------------------------------------------------------

    [Fact]
    public void LoseLifeEachOpponentDef_DeclaresNoTarget()
    {
        var def = new LoseLifeEachOpponentEffectDef { Amount = 1 };

        def.ToTargetRequest().Should().BeNull(
            "an each-opponent group effect (CR 608.2) announces no target");
    }

    [Fact]
    public async Task LoseLifeEachOpponent_DrainsOpponent_NotController()
    {
        var source = OnBattlefield(_alice, "Corpse Knight Source");
        var def = new LoseLifeEachOpponentEffectDef { Amount = 1 };
        var effect = def.ToResolveEffect()(source, _alice, null, -1);

        await ResolveEffectAsync(effect, NewContext(_alice, _bob));

        _bob.LifeTotal.Should().Be(19, "the opponent loses 1 life (CR 109.5 / 119.3)");
        _alice.LifeTotal.Should().Be(20, "the controller is not an opponent");
    }

    [Fact]
    public async Task LoseLifeEachOpponent_DrainsEveryOpponent()
    {
        var source = OnBattlefield(_alice, "Corpse Knight Source");
        var def = new LoseLifeEachOpponentEffectDef { Amount = 3 };
        var effect = def.ToResolveEffect()(source, _alice, null, -1);

        await ResolveEffectAsync(effect, NewContext(_alice, _bob, _carol));

        _bob.LifeTotal.Should().Be(17);
        _carol.LifeTotal.Should().Be(17);
        _alice.LifeTotal.Should().Be(20);
    }

    // ------------------------------------------------------------------
    // Factories — the cards the verbs unblock.
    // ------------------------------------------------------------------

    [Fact]
    public void ImpactTremors_Factory_BuildsEnchantmentWithEnterTrigger()
    {
        var tremors = ImpactTremorsFactory.Create(_alice);

        tremors.Name.Should().Be("Impact Tremors");
        tremors.HasType(CardType.Enchantment).Should().BeTrue();
        tremors.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the JSON shell carries the creature-enters trigger");
        tremors.Abilities.OfType<TriggeredAbility>().Single()
            .TargetRequests.Should().BeEmpty(
                "deal_damage_each_opponent is untargeted (CR 608.2)");
    }

    [Fact]
    public void CorpseKnight_Factory_BuildsCreatureWithEnterTrigger()
    {
        var knight = CorpseKnightFactory.Create(_alice);

        knight.Name.Should().Be("Corpse Knight");
        knight.HasType(CardType.Creature).Should().BeTrue();
        knight.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        knight.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        knight.GetPower().Should().Be(2);
        knight.GetToughness().Should().Be(2);
        knight.Abilities.OfType<TriggeredAbility>().Should().ContainSingle();
        knight.Abilities.OfType<TriggeredAbility>().Single()
            .TargetRequests.Should().BeEmpty(
                "lose_life_each_opponent is untargeted (CR 608.2)");
    }
}
