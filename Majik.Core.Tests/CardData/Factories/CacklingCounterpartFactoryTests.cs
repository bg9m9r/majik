using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CacklingCounterpartFactory"/> (Innistrad, {1}{U}{U}).
///
/// Scryfall oracle (verbatim, verified 2026-06-14):
///   "Create a token that's a copy of target creature you control.
///    Flashback {5}{U}{U} (You may cast this card from your graveyard for its
///    flashback cost. Then exile it.)"
///
/// Mirrors <see cref="FireboltFactory"/> (flashback spell whose printed
/// flashback cost is an all-mana cost) but the resolve body creates a TOKEN
/// COPY of the target creature — same mechanism as
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Tokens.CreateCopyTokenTemplate"/>
/// (CR 707.2 — the copy token's controller is the controller of the effect
/// creating it).
///
/// Covers (the card's UNIQUE behaviour):
/// - Identity ({1}{U}{U} Instant).
/// - Spell definition shape: 1..1 "target creature you control".
/// - Resolve body spawns a token copy of the chosen creature under the
///   caster's control (name + P/T + keywords copied; CR 706.2 / CR 707.2).
/// - Flashback cost matches the printed {5}{U}{U} (CR 702.34).
/// </summary>
[Trait("Color", "U")]
public class CacklingCounterpartFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CacklingCounterpart_Identity_InstantAt1UU()
    {
        var card = CacklingCounterpartFactory.Create(_alice);

        card.Name.Should().Be("Cackling Counterpart");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{U}{U}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CacklingCounterpart_SpellDefinition_HasSingleTargetCreatureRequest()
    {
        var def = CacklingCounterpartFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature you control");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void CacklingCounterpart_Resolve_SpawnsTokenCopyUnderCasterControl()
    {
        // A creature Alice controls, with a keyword to verify copy fidelity.
        var source = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        source.SetOwner(_alice);
        source.SetController(_alice);
        source.SetZone(ZoneType.Battlefield);
        source.AddAbility(new KeywordAbility("flying", source, _alice));
        _alice.Zones.Battlefield.AddCard(source);

        var def = CacklingCounterpartFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { source } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.IsToken).ToList();
        tokens.Should().ContainSingle("Cackling Counterpart creates one token copy");
        var copy = tokens.Single();
        copy.Name.Should().Be("Grizzly Bears");
        copy.BasePower.Should().Be(2);
        copy.BaseToughness.Should().Be(2);
        copy.Controller.Should().BeSameAs(_alice, "CR 707.2 — copy token's controller is the effect's controller");
        copy.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword.ToLowerInvariant())
            .Should().Contain("flying");
    }

    [Fact]
    public void CacklingCounterpart_Resolve_NonCreatureTarget_NoOp()
    {
        // CR 608.2b — if the target is illegal (not a creature), the token
        // creation is a clean no-op rather than a crash.
        var def = CacklingCounterpartFactory.BuildSpellDefinition(_alice, resolver: _ => "not-a-creature");
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { new object() } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void CacklingCounterpart_FlashbackCost_IsFiveGenericPlusUU()
    {
        var cost = CacklingCounterpartFactory.BuildFlashbackCost();

        cost.AlternativeManaCost.IsZero.Should().BeFalse();
        cost.AlternativeManaCost.Should().Be(ManaCost.Parse("{5}{U}{U}"),
            "printed flashback cost is {5}{U}{U} (CR 702.34)");
    }
}
