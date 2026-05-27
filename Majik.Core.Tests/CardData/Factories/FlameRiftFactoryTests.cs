using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FlameRiftFactory"/>.
///
/// Card: Flame Rift — Sorcery {1}{R} (Nemesis and reprints).
///   "Flame Rift deals 4 damage to each player."
///
/// Symmetrical burn: like <see cref="PyroclasmFactory"/>'s "to each creature"
/// sweep, but routed at players via <see cref="Majik.Core.Primitives.Fx.DealDamageAny"/>
/// (CR 119.3 — damage to a player reduces that player's life). The resolve
/// effect takes the player list positionally, matching Pyroclasm's
/// <c>BuildResolveEffect(allPlayers)</c> shape.
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller) + dispatch.
///   - Resolve deals 4 to every supplied player, including the caster.
///   - No-player / empty list is a clean no-op.
/// </summary>
public class FlameRiftFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void FlameRift_Identity()
    {
        var c = FlameRiftFactory.Create(_alice);

        c.Name.Should().Be("Flame Rift");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FlameRift()
    {
        var card = NamedCardFactory.Create("Flame Rift", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Flame Rift");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_Deals4Damage_ToEveryPlayer_IncludingCaster()
    {
        var effects = FlameRiftFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // CR 119.3 — 4 damage to a player reduces their life by 4.
        _alice.LifeTotal.Should().Be(16, "Flame Rift is symmetrical — the caster takes 4 too");
        _bob.LifeTotal.Should().Be(16);
    }

    [Fact]
    public void Resolve_EmptyPlayerList_IsCleanNoOp()
    {
        var effects = FlameRiftFactory.BuildResolveEffect(System.Array.Empty<Player>());
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.LifeTotal.Should().Be(20);
        _bob.LifeTotal.Should().Be(20);
    }
}
