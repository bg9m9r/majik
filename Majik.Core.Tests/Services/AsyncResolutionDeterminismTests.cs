using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Services;

/// <summary>
/// PLAN 01 (Slice B) — determinism guard. Making resolution async must not
/// introduce nondeterminism: resolving a scripted stack object twice yields
/// the same ordered effect output, and effects still run in declaration order.
/// </summary>
public class AsyncResolutionDeterminismTests
{
    private static (List<string> log, Majik.Core.Spells.Spell spell) BuildSpell(Player p)
    {
        var log = new List<string>();
        var card = new Instant("Scripted Bolt", "R");
        card.ChangeOwner(p);
        var spell = new Majik.Core.Spells.Spell(card, p, effects: new IEffect[]
        {
            new Effect("a", () => log.Add("a")),
            new Effect("b", () => log.Add("b")),
            new Effect("c", () => log.Add("c")),
        });
        return (log, spell);
    }

    [Fact]
    public async Task ResolveAsync_RunsEffectsInDeclarationOrder()
    {
        var p = new Player("P", 20);
        var (log, spell) = BuildSpell(p);

        await spell.ResolveAsync(agent: null, game: null);

        log.Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task ResolveAsync_TwoScriptedStacks_ProduceIdenticalOutput()
    {
        var p1 = new Player("P", 20);
        var (log1, spell1) = BuildSpell(p1);
        var p2 = new Player("P", 20);
        var (log2, spell2) = BuildSpell(p2);

        await spell1.ResolveAsync(agent: null, game: null);
        await spell2.ResolveAsync(agent: null, game: null);

        log1.Should().Equal(log2);
    }

    [Fact]
    public void SyncResolveShim_MatchesAsyncOrder()
    {
        var p = new Player("P", 20);
        var (log, spell) = BuildSpell(p);

        spell.Resolve(); // sync shim over ResolveAsync

        log.Should().Equal("a", "b", "c");
        spell.IsResolving.Should().BeFalse();
    }

    [Fact]
    public async Task ActivatedAbility_ResolveAsync_AwaitsAsyncEffectBody()
    {
        var p = new Player("P", 20);
        var ran = false;
        var src = new Creature("Src", "G", 1, 1) { Owner = p, Zone = ZoneType.Battlefield };
        var ability = new ActivatedAbility(
            source: src,
            controller: p,
            effects: new IEffect[]
            {
                new Effect("async", async ctx => { await Task.Yield(); ran = true; }),
            });

        await ability.ResolveAsync(agent: null, game: null);

        ran.Should().BeTrue();
    }
}
