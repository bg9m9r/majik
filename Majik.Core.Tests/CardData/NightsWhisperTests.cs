using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="NightsWhisperFactory"/>.
///
/// Card: Night's Whisper — Sorcery {1}{B} (Fifth Dawn).
///   "You draw two cards and you lose 2 life."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - Resolve: caster draws 2 and loses 2 life.
///   - Empty library: draw short-circuits + SBA flag set; life loss
///     still applies (printed "and" is unconditional).
///   - Single-card library: draws the one available card, marks empty
///     on the second iteration, life loss runs.
/// </summary>
public class NightsWhisperTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void NightsWhisper_Identity_Sorcery_1B()
    {
        var c = NightsWhisperFactory.Create(_alice);

        c.Name.Should().Be("Night's Whisper");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_NightsWhisper()
    {
        var card = NamedCardFactory.Create("Night's Whisper", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Night's Whisper");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_DrawsTwoCards_AndLosesTwoLife()
    {
        SeedLibrary(_alice, 5);

        var def = NightsWhisperFactory.BuildSpellDefinition(_alice);
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();

        ResolveNoTargets(def);

        _alice.Zones.Hand.Count.Should().Be(NightsWhisperFactory.CardsDrawn);
        _alice.Zones.Library.Count.Should().Be(3);
        _alice.LifeTotal.Should().Be(20 - NightsWhisperFactory.LifePaid);
    }

    [Fact]
    public void Resolve_EmptyLibrary_StillLosesLife_AndFlagsDrawFromEmpty()
    {
        // No library seeding — library is empty from the start.
        var def = NightsWhisperFactory.BuildSpellDefinition(_alice);
        ResolveNoTargets(def);

        _alice.Zones.Hand.Count.Should().Be(0);
        _alice.LifeTotal.Should().Be(20 - NightsWhisperFactory.LifePaid);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    [Fact]
    public void Resolve_SingleCardLibrary_DrawsOne_FlagsEmpty_StillLosesLife()
    {
        SeedLibrary(_alice, 1);

        var def = NightsWhisperFactory.BuildSpellDefinition(_alice);
        ResolveNoTargets(def);

        _alice.Zones.Hand.Count.Should().Be(1);
        _alice.Zones.Library.Count.Should().Be(0);
        _alice.LifeTotal.Should().Be(20 - NightsWhisperFactory.LifePaid);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    // ---- Helpers ----

    private static void ResolveNoTargets(SpellDefinition def)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: System.Array.Empty<System.Collections.Generic.IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private static void SeedLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = new Card($"L{i}", "");
            c.SetOwner(p);
            c.SetZone(ZoneType.Library);
            p.Zones.Library.AddCard(c);
        }
    }
}
