using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// GAP 2 — per-activation variable-X ledger on <see cref="ActivatedAbility"/>.
/// <see cref="ActivatedAbility.ChosenX"/> mirrors the
/// <see cref="ActivatedAbility.ChosenTargets"/> lifecycle: null until the
/// activating player's agent chooses an X during activation, then read by the
/// resolution effect (via <see cref="ResolutionContext.ChosenX"/>). Reset per
/// activation — a re-activation that doesn't choose X must not reuse a stale
/// value.
/// </summary>
public class ActivatedAbilityChosenXTests
{
    private readonly Player _alice = new("Alice", 20);

    private ActivatedAbility Build()
    {
        var src = new Creature("Pinger", "{2}", 1, 1) { Owner = _alice, Controller = _alice };
        return new ActivatedAbility(src, _alice);
    }

    [Fact]
    public void ChosenX_DefaultsToNull()
    {
        Build().ChosenX.Should().BeNull("no X chosen until SetChosenX is called");
    }

    [Fact]
    public void SetChosenX_SetGetRoundTrip()
    {
        var ability = Build();
        ability.SetChosenX(3);
        ability.ChosenX.Should().Be(3);
    }

    [Fact]
    public void SetChosenX_Overwrites_PerActivation()
    {
        var ability = Build();
        ability.SetChosenX(3);
        ability.SetChosenX(5);
        ability.ChosenX.Should().Be(5,
            "a fresh activation overwrites the previously chosen X");
    }

    [Fact]
    public async System.Threading.Tasks.Task ResolveAsync_ThreadsChosenX_OntoResolutionContext()
    {
        var src = new Creature("Pinger", "{2}", 1, 1) { Owner = _alice, Controller = _alice };
        int? seenX = null;
        var effect = new Effect("read X", rc =>
        {
            seenX = rc.ChosenX;
            return System.Threading.Tasks.ValueTask.CompletedTask;
        });
        var ability = new ActivatedAbility(
            source: src, controller: _alice, effects: new[] { effect });
        ability.SetChosenX(4);

        await ability.ResolveAsync(agent: null, game: null);

        seenX.Should().Be(4, "ResolveAsync threads ChosenX onto the ResolutionContext");
    }
}
