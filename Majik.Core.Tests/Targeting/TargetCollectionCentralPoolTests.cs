using System;
using System.Linq;
using FluentAssertions;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Targeting;
using Xunit;

namespace Majik.Core.Tests.TargetingPipeline;

public class TargetCollectionCentralPoolTests
{
    [Fact]
    public void ResolveLivePool_EmptyPool_AnyTarget_falls_back_to_central_pool_including_player()
    {
        var (ctx, _) = TargetingTestWorld.Build();
        var req = new TargetRequest("any target", 1, 1, Array.Empty<object>());

        var pool = TargetCollection.ResolveLivePool(req, ctx);

        pool.OfType<Player>().Should().NotBeEmpty();
        pool.OfType<Majik.Core.Cards.Creature>().Should().NotBeEmpty();
    }

    [Fact]
    public void ResolveLivePool_card_supplied_pool_is_NOT_overridden()
    {
        var (ctx, _) = TargetingTestWorld.Build();
        var onlyCard = new Majik.Core.Cards.Creature("Provided", "G", 1, 1);
        var req = new TargetRequest("any target", 1, 1, new object[] { onlyCard });

        var pool = TargetCollection.ResolveLivePool(req, ctx);

        // Card already supplied a pool — central fallback must not touch it.
        pool.Should().ContainSingle().Which.Should().BeSameAs(onlyCard);
    }

    [Fact]
    public void ResolveLivePool_empty_None_category_stays_empty()
    {
        var (ctx, _) = TargetingTestWorld.Build();
        var req = new TargetRequest("no target", 0, 0, Array.Empty<object>());

        TargetCollection.ResolveLivePool(req, ctx).Should().BeEmpty();
    }
}
