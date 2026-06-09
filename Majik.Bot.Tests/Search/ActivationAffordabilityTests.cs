using FluentAssertions;
using Majik.Bot.Search;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Verifies that activated-ability mana-affordability in
/// <see cref="LegalActionEnumerator.ForPriority"/> is symmetric with spell
/// casting: the mana portion of an activation cost is checked against
/// <see cref="LegalActionEnumerator.UntappedManaSources"/> (floating pool +
/// untapped tappable sources), not only the floating pool.
///
/// <para>
/// Bug: before the fix, <c>ManaCostCost.CanPay(player)</c> was called for
/// ALL costs uniformly, and <c>ManaCostCost.CanPay</c> only checks
/// <c>player.ManaPool.CanPay</c> — the FLOATING pool only. A {3},{T} ability
/// on an artifact was never enumerated unless {3} was already floating, even
/// though the engine's <c>ManaPaymentResolver</c> would auto-tap lands to pay
/// it at resolution.
/// </para>
/// </summary>
public class ActivationAffordabilityTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal {3},{T} activated ability on an artifact — the same
    /// cost shape as Goblin Charbelcher — so we can test enumeration without
    /// driving the full factory pipeline.
    /// </summary>
    private static ActivatedAbility BuildThreeTapAbility(Artifact source, Majik.Core.Players.Player owner)
    {
        return new ActivatedAbility(
            source: source,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{3}"),
                AdditionalCost.Tap(source),
            },
            effects: Array.Empty<IEffect>(),
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });
    }

    // ── Positive: 3 untapped lands, 0 floating → ability IS enumerated ───────

    /// <summary>
    /// A permanent with a {3},{T} activated ability is on the battlefield
    /// (untapped), and the player has 3 untapped lands but ZERO floating mana.
    ///
    /// After the fix: the ability IS enumerated because UntappedManaSources
    /// returns 3 ≥ CMC 3.
    ///
    /// Before the fix: NOT enumerated because ManaCostCost.CanPay checks only
    /// the floating pool (which is empty).
    /// </summary>
    [Fact]
    public void ForPriority_EnumeratesActivation_WhenManaCostAffordableByTapping_NotOnlyFloating()
    {
        var s = new BotTestScenario();

        // The permanent with the {3},{T} ability — an artifact (no summoning
        // sickness on tap, so AdditionalCost.Tap.CanPay = true immediately).
        var belcher = new Artifact("Goblin Charbelcher", "{4}");
        belcher.ChangeOwner(s.Self);
        belcher.ChangeController(s.Self);
        belcher.AddAbility(BuildThreeTapAbility(belcher, s.Self));
        s.Self.Zones.Battlefield.AddCard(belcher);

        // Three untapped lands — no mana ability attached, so the bare-Land
        // fallback in UntappedManaSources contributes 1 each.
        s.AddLandToBattlefield(s.Self, "Mountain1");
        s.AddLandToBattlefield(s.Self, "Mountain2");
        s.AddLandToBattlefield(s.Self, "Mountain3");

        // Confirm zero floating mana — the pre-fix bug hinges on this.
        s.Self.ManaPool.Total.Should().Be(0,
            because: "no mana should be floating; mana comes only from lands");

        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        actions.Should().Contain(a => a is PriorityAction.ActivateAbility,
            because: "3 untapped lands can cover the {3} mana cost — " +
                     "enumeration must be symmetric with casting (affordable-by-tapping)");
    }

    // ── Negative: 0 lands, 0 floating → ability is NOT enumerated ───────────

    /// <summary>
    /// With zero mana sources (no lands, no floating), the {3},{T} ability must
    /// NOT appear — cost 3 &gt; available 0.
    /// </summary>
    [Fact]
    public void ForPriority_DoesNotEnumerateActivation_WhenInsufficientMana()
    {
        var s = new BotTestScenario();

        // Permanent with {3},{T} ability — untapped.
        var belcher = new Artifact("Goblin Charbelcher", "{4}");
        belcher.ChangeOwner(s.Self);
        belcher.ChangeController(s.Self);
        belcher.AddAbility(BuildThreeTapAbility(belcher, s.Self));
        s.Self.Zones.Battlefield.AddCard(belcher);

        // Zero lands, zero floating mana.
        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        actions.Should().NotContain(a => a is PriorityAction.ActivateAbility,
            because: "no mana available — {3} activation cost cannot be covered");
    }

    // ── Tap-cost gate still enforced ─────────────────────────────────────────

    /// <summary>
    /// Even with sufficient mana (3 untapped lands), a TAPPED artifact must
    /// NOT be enumerated — the {T} non-mana cost blocks it.
    /// </summary>
    [Fact]
    public void ForPriority_DoesNotEnumerateActivation_WhenSourceIsTapped()
    {
        var s = new BotTestScenario();

        var belcher = new Artifact("Goblin Charbelcher", "{4}");
        belcher.ChangeOwner(s.Self);
        belcher.ChangeController(s.Self);
        belcher.AddAbility(BuildThreeTapAbility(belcher, s.Self));
        s.Self.Zones.Battlefield.AddCard(belcher);

        // Tap the Charbelcher — {T} cost becomes unsatisfiable.
        belcher.Tap();

        s.AddLandToBattlefield(s.Self, "Mountain1");
        s.AddLandToBattlefield(s.Self, "Mountain2");
        s.AddLandToBattlefield(s.Self, "Mountain3");

        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        actions.Should().NotContain(a => a is PriorityAction.ActivateAbility,
            because: "the permanent is tapped — {T} cost cannot be paid regardless of mana");
    }

    // ── Zero-cost mana ability still excluded ────────────────────────────────

    /// <summary>
    /// A {0}-mana, {T} ability (e.g. "tap to produce a Treasure token")
    /// IS enumerable even with zero floating and no lands, because CMC 0 ≤ 0.
    /// Non-mana cost check (tap = not tapped) must still pass.
    /// </summary>
    [Fact]
    public void ForPriority_EnumeratesZeroManaCostActivation_WhenUntapped()
    {
        var s = new BotTestScenario();

        // Build an artifact with a {0},{T} ability — no mana cost at all.
        var artifact = new Artifact("Zero-Tap-Artifact", "{1}");
        artifact.ChangeOwner(s.Self);
        artifact.ChangeController(s.Self);
        var zeroTapAbility = new ActivatedAbility(
            source: artifact,
            controller: s.Self,
            costs: new ICost[]
            {
                new ManaCostCost("{0}"),
                AdditionalCost.Tap(artifact),
            },
            effects: Array.Empty<IEffect>());
        artifact.AddAbility(zeroTapAbility);
        s.Self.Zones.Battlefield.AddCard(artifact);

        // No lands, no floating mana — but mana cost is {0}.
        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        actions.Should().Contain(a => a is PriorityAction.ActivateAbility,
            because: "{0},{T} ability is always affordable when the artifact is untapped");
    }
}
