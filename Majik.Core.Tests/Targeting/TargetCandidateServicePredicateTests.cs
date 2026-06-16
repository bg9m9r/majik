using System.Linq;
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Xunit;

namespace Majik.Core.Tests.TargetingPipeline;

public class TargetCandidateServicePredicateTests
{
    [Fact]
    public void Creature_predicate_accepts_creature_rejects_player()
    {
        var pred = TargetCandidateService.BuildLegalityPredicate("target creature");
        pred.Should().NotBeNull();
        var (ctx, _) = TargetingTestWorld.Build();
        var creature = ctx.AllPlayers
            .SelectMany(p => p.Zones.Battlefield.GetCards())
            .OfType<Creature>().First();
        pred!(creature).Should().BeTrue();
        pred(ctx.AllPlayers.First()).Should().BeFalse(); // a Player is not a creature
    }

    [Fact]
    public void AnyTarget_predicate_accepts_player()
    {
        var pred = TargetCandidateService.BuildLegalityPredicate("any target");
        pred.Should().NotBeNull();
        var (ctx, _) = TargetingTestWorld.Build();
        var player = ctx.AllPlayers.First();
        pred!(player).Should().BeTrue();
    }

    [Fact]
    public void Spell_predicate_accepts_stack_spell()
    {
        var pred = TargetCandidateService.BuildLegalityPredicate("target spell");
        pred.Should().NotBeNull();
        var (ctx, _) = TargetingTestWorld.Build();
        var spell = ctx.Stack.GetAll().OfType<Majik.Core.Spells.ISpell>().First();
        pred!(spell).Should().BeTrue();
    }

    [Fact]
    public void None_returns_null_predicate()
    {
        TargetCandidateService.BuildLegalityPredicate("no target").Should().BeNull();
    }
}
