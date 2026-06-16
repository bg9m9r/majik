using System.Linq;
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Xunit;

namespace Majik.Core.Tests.TargetingPipeline;

public class TargetCandidateServiceGatherTests
{
    [Fact]
    public void AnyTarget_includes_both_players_creatures_and_planeswalkers()
    {
        var (ctx, caster) = TargetingTestWorld.Build();
        var pool = TargetCandidateService.GatherCandidates("any target", ctx, caster);
        pool.OfType<Player>().Should().HaveCount(2);
        pool.OfType<Creature>().Should().NotBeEmpty();
        pool.OfType<Planeswalker>().Should().NotBeEmpty();
    }

    [Fact]
    public void TargetCreature_returns_only_creatures()
    {
        var (ctx, caster) = TargetingTestWorld.Build();
        var pool = TargetCandidateService.GatherCandidates("target creature", ctx, caster);
        pool.Should().OnlyContain(o => o is Creature);
        pool.Should().NotBeEmpty();
    }

    [Fact]
    public void TargetPlayer_returns_only_players()
    {
        var (ctx, caster) = TargetingTestWorld.Build();
        var pool = TargetCandidateService.GatherCandidates("target player", ctx, caster);
        pool.Should().OnlyContain(o => o is Player);
        pool.Should().HaveCount(2);
    }

    [Fact]
    public void TargetSpell_returns_the_stack_spell()
    {
        var (ctx, caster) = TargetingTestWorld.Build();
        var pool = TargetCandidateService.GatherCandidates("target spell", ctx, caster);
        pool.Should().OnlyContain(o => o is Majik.Core.Spells.ISpell);
        pool.Should().HaveCount(1);
    }

    [Fact]
    public void TargetOpponent_excludes_the_caster()
    {
        var (ctx, caster) = TargetingTestWorld.Build();
        var pool = TargetCandidateService.GatherCandidates("target opponent", ctx, caster);
        pool.Should().OnlyContain(o => o is Player);
        pool.Should().HaveCount(1);
        pool.Should().NotContain(caster);
    }

    [Fact]
    public void None_category_returns_empty()
    {
        var (ctx, caster) = TargetingTestWorld.Build();
        TargetCandidateService.GatherCandidates("no target", ctx, caster).Should().BeEmpty();
    }
}
