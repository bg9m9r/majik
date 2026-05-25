using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Mizzix's Mastery — Sorcery {3}{R}{R}.
///   "Choose target instant or sorcery card in your graveyard. Copy that
///    card. You may cast the copy. Exile that card."
///
/// Validates:
///   * Card identity + dispatch.
///   * TargetRequest declares 1..1 instant/sorcery candidate pool from
///     controller's graveyard.
///   * Resolve body executes the chosen card's bound SpellDefinition's
///     effects in place (CR 707.10 lossy v1 copy) and exiles the original.
///   * Empty graveyard / no legal target → clean no-op.
///   * Original is exiled even when the copy lookup is null (shape path).
/// </summary>
public class MizzixsMasteryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ------------------------------------------------------------------
    // Identity + dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void MizzixsMastery_IsSorceryNamedMizzixsMastery_AtCost3RR()
    {
        var mm = MizzixsMasteryFactory.Create(_alice);

        mm.Name.Should().Be("Mizzix's Mastery");
        mm.HasType(CardType.Sorcery).Should().BeTrue();
        mm.ManaCost.Should().Be("{3}{R}{R}");
        mm.Owner.Should().BeSameAs(_alice);
        mm.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MizzixsMastery()
    {
        var mm = NamedCardFactory.Create("Mizzix's Mastery", _alice);

        mm.Should().BeOfType<Sorcery>();
        mm.Name.Should().Be("Mizzix's Mastery");
        mm.HasType(CardType.Sorcery).Should().BeTrue();
        mm.ManaCost.Should().Be("{3}{R}{R}");
    }

    // ------------------------------------------------------------------
    // TargetRequest shape — 1..1 instant/sorcery in your graveyard
    // ------------------------------------------------------------------

    [Fact]
    public void BuildDefinition_DeclaresOneTargetRequest_ForInstantOrSorceryInGraveyard()
    {
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var divination = new Sorcery("Divination", "{2}{U}") { Owner = _alice };
        divination.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(divination);

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        bears.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bears);

        var def = MizzixsMasteryFactory.BuildDefinition(_alice, raw => raw);

        def.TargetRequests.Should().HaveCount(1);
        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.LegalCandidates.Should().Contain(bolt, "Bolt is an instant in Alice's graveyard");
        tr.LegalCandidates.Should().Contain(divination, "Divination is a sorcery in Alice's graveyard");
        tr.LegalCandidates.Should().NotContain(bears, "creatures aren't valid targets");
    }

    // ------------------------------------------------------------------
    // Resolve — copy via lookup + exile original
    // ------------------------------------------------------------------

    [Fact]
    public void Resolve_CopiesChosenCardEffects_AndExilesOriginal()
    {
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        // Fake SpellDefinition lookup: any card's "copy" runs a sentinel
        // effect that bumps a local counter. Validates the copy actually
        // executes the bound effects.
        int copyExecutions = 0;
        SpellDefinition? Lookup(ICard card) =>
            new SpellDefinition(
                Modes: Array.Empty<string>(),
                HasVariableX: false,
                TargetRequests: Array.Empty<TargetRequest>(),
                EffectFactory: _ => new IEffect[]
                {
                    new Effect("test-copy-sentinel", () => copyExecutions++),
                });

        var def = MizzixsMasteryFactory.BuildDefinition(
            _alice,
            raw => raw,
            Lookup);

        // Caller sets the chosen target to Bolt.
        var p = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bolt } },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(p);
        foreach (var e in effects) e.Execute();

        copyExecutions.Should().Be(1,
            "Mizzix's Mastery copies the chosen card — its effects run once");

        bolt.Zone.Should().Be(ZoneType.Exile,
            "the original card is exiled after the copy");
        _alice.Zones.Exile.GetCards().Should().Contain(bolt);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bolt);
    }

    [Fact]
    public void Resolve_EmptyGraveyard_IsCleanNoOp()
    {
        int copyExecutions = 0;
        SpellDefinition? Lookup(ICard card) =>
            new SpellDefinition(
                Modes: Array.Empty<string>(),
                HasVariableX: false,
                TargetRequests: Array.Empty<TargetRequest>(),
                EffectFactory: _ => new IEffect[]
                {
                    new Effect("test-copy-sentinel", () => copyExecutions++),
                });

        var def = MizzixsMasteryFactory.BuildDefinition(_alice, raw => raw, Lookup);

        // No target supplied + empty graveyard.
        var p = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(p);
        foreach (var e in effects) e.Execute();

        copyExecutions.Should().Be(0, "no legal target — no copy fires");
    }

    [Fact]
    public void Resolve_NoLookup_StillExilesOriginal()
    {
        // Shape-only path — when no SpellDefinition lookup is wired the
        // factory still resolves the exile half. Tests can assert
        // structural behaviour without dragging the binder in.
        var divination = new Sorcery("Divination", "{2}{U}") { Owner = _alice };
        divination.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(divination);

        var def = MizzixsMasteryFactory.BuildDefinition(_alice, raw => raw);

        var p = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { divination } },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(p);
        foreach (var e in effects) e.Execute();

        divination.Zone.Should().Be(ZoneType.Exile,
            "the chosen card is exiled even without a copy lookup wired");
    }

    [Fact]
    public void Resolve_OpponentsGraveyardCard_IsFizzle()
    {
        var bobCard = new Instant("Bolt", "{R}") { Owner = _bob };
        bobCard.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bobCard);

        int copyExecutions = 0;
        SpellDefinition? Lookup(ICard card) =>
            new SpellDefinition(
                Modes: Array.Empty<string>(),
                HasVariableX: false,
                TargetRequests: Array.Empty<TargetRequest>(),
                EffectFactory: _ => new IEffect[]
                {
                    new Effect("test-copy-sentinel", () => copyExecutions++),
                });

        // Alice's Mizzix's Mastery — Bob's card shouldn't be a legal
        // target. If somehow passed through, the resolve-time owner check
        // fizzles it.
        var def = MizzixsMasteryFactory.BuildDefinition(_alice, raw => raw, Lookup);
        var p = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bobCard } },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(p);
        foreach (var e in effects) e.Execute();

        copyExecutions.Should().Be(0, "Mizzix's Mastery only sees the controller's graveyard");
        bobCard.Zone.Should().Be(ZoneType.Graveyard, "Bob's card is not moved");
    }
}
