using System.Reflection;
using FluentAssertions;
using Majik.Core.Api.BotReplay;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Api.Tests.BotReplay;

/// <summary>
/// Coverage tripwire (the <c>MutableFieldTripwireTests</c> pattern): the
/// bot-decision codec registry must cover every PRIMITIVE
/// <see cref="IPlayerAgent"/> decision method — the 15 methods
/// <c>BotPlayerAgent</c> overrides (12 abstract interface methods + the 3
/// default methods it overrides: ChooseAsync / ChooseLibraryPickAsync /
/// ChooseYesNoAsync). Interface DEFAULT methods funnel into these primitives,
/// so codec coverage of the primitives covers the whole prompt surface.
/// Adding a decision method without registering a codec fails here, naming
/// the method.
/// </summary>
public class CodecCoverageTripwireTests
{
    /// <summary>
    /// The pinned primitive surface. If BotPlayerAgent gains an override (or
    /// IPlayerAgent gains an abstract method) this list — and the codec
    /// registry — must be extended together.
    /// </summary>
    private static readonly string[] PinnedPrimitives =
    {
        nameof(IPlayerAgent.ChoosePriorityActionAsync),
        nameof(IPlayerAgent.ChooseMulliganAsync),
        nameof(IPlayerAgent.ChooseCardsToBottomAsync),
        nameof(IPlayerAgent.ChooseTargetsAsync),
        nameof(IPlayerAgent.ChooseXAsync),
        nameof(IPlayerAgent.ChooseModeAsync),
        nameof(IPlayerAgent.OrderTriggersAsync),
        nameof(IPlayerAgent.ChooseManaSourcesAsync),
        nameof(IPlayerAgent.DeclareAttackersAsync),
        nameof(IPlayerAgent.DeclareBlockersAsync),
        nameof(IPlayerAgent.ChooseScryDecisionAsync),
        nameof(IPlayerAgent.ChooseSurveilDecisionAsync),
        nameof(IPlayerAgent.ChooseLibraryPickAsync),
        nameof(IPlayerAgent.ChooseYesNoAsync),
        nameof(IPlayerAgent.ChooseAsync),
    };

    [Fact]
    public void EveryPinnedPrimitive_HasACodecRegistryEntry()
    {
        foreach (var method in PinnedPrimitives)
        {
            BotDecisionCodec.PrimitiveMethodKinds.Should().ContainKey(method,
                $"primitive IPlayerAgent decision method '{method}' must have a " +
                "registered bot-decision codec — recording a bot match would " +
                "silently skip this decision otherwise");
        }
    }

    [Fact]
    public void EveryAbstractIPlayerAgentMethod_IsInThePinnedPrimitiveList()
    {
        // Abstract interface methods have no default implementation — every
        // agent must implement them, so every one of them MUST be a recorded
        // primitive. A new abstract method on IPlayerAgent lands here first.
        var abstractMethods = typeof(IPlayerAgent)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.IsAbstract)
            .Select(m => m.Name)
            .Distinct()
            .ToList();

        abstractMethods.Should().NotBeEmpty();
        foreach (var name in abstractMethods)
        {
            PinnedPrimitives.Should().Contain(name,
                $"abstract IPlayerAgent method '{name}' has no default implementation, " +
                "so it is part of the primitive decision surface and needs a codec + " +
                "Recording/Scripted agent override");
        }
    }

    [Fact]
    public void RegistryCoversExactlyTheFifteenKinds()
    {
        BotDecisionCodec.PrimitiveMethodKinds.Should().HaveCount(PinnedPrimitives.Length);
        BotDecisionCodec.PrimitiveMethodKinds.Values.Should()
            .OnlyHaveUniqueItems("each primitive maps to its own decision kind");
        Enum.GetValues<BotDecisionKind>().Should().HaveCount(PinnedPrimitives.Length,
            "BotDecisionKind enumerates exactly the primitive decision surface");
    }
}
