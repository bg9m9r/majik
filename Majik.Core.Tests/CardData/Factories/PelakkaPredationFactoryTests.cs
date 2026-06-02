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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="PelakkaPredationFactory"/> and
/// <see cref="PelakkaCavernsFactory"/> — the front + back faces of the
/// Zendikar Rising modal double-faced card Pelakka Predation // Pelakka
/// Caverns.
///
/// Front face (Pelakka Predation, {2}{B}):
///   Sorcery. "Target opponent reveals their hand. You choose a card from it
///   with mana value 3 or greater. That player discards that card."
///
/// Back face (Pelakka Caverns):
///   Land. "This land enters tapped." "{T}: Add {B}."
///
/// The front face blends <see cref="DespiseFactory"/>'s "target opponent"
/// reveal-and-choose-discard shape with <see cref="InquisitionOfKozilekFactory"/>'s
/// mana-value gate (here, mana value 3 OR GREATER, all card types, no life
/// cost). The MDFC structure mirrors <see cref="MalakirRebirthFactory"/> /
/// <see cref="MalakirMireFactory"/>.
/// </summary>
[Trait("Color", "B")]
public class PelakkaPredationFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ChosenSpellParams MakeChosen(Player targetPlayer) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetPlayer } },
            Mana: ManaPayment.Empty);

    private static ICard SeedHandCard(Player p, string name, string manaCost)
    {
        var c = new Card(name, manaCost);
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static ICard SeedLandHandCard(Player p, string name)
    {
        var c = new Land(name);
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void PelakkaPredation_Identity_2B_Sorcery()
    {
        var card = PelakkaPredationFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Pelakka Predation");
        card.ManaCost.Should().Be("{2}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PelakkaPredation_IsBlack()
    {
        var card = PelakkaPredationFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Black, "the {B} pip makes it black");
        colors.Should().NotContain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Blue);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PelakkaPredation()
    {
        var card = NamedCardFactory.Create("Pelakka Predation", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Pelakka Predation");
        card.ManaCost.Should().Be("{2}{B}");
    }

    [Fact]
    public void PelakkaPredation_CarriesMdfcState_FrontFace()
    {
        var card = PelakkaPredationFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull("Pelakka Predation is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Pelakka Predation");
        card.MdfcState!.BackFaceName.Should().Be("Pelakka Caverns");
        card.MdfcState!.IsBackFace.Should().BeFalse("front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Pelakka Predation");
    }

    // =========================================================================
    // Front face — SpellDefinition shape
    // =========================================================================

    [Fact]
    public void BuildSpellDefinition_SingleTargetOpponent_NoVariableX()
    {
        var def = PelakkaPredationFactory.BuildSpellDefinition(o => o!, agent: null, eventBus: null);

        def.HasVariableX.Should().BeFalse("Pelakka Predation is not an X-spell");
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1, "Target opponent — exactly one");
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // =========================================================================
    // Front face — resolution: reveal → pick mv>=3 → discard
    // =========================================================================

    [Fact]
    public void Resolve_AgentPicksHeavyCard_DiscardsThatCard()
    {
        // Bob's hand: Tarmogoyf ({1}{G}, mv 2), a 3-drop ({2}{B}, mv 3), and a
        // 5-drop ({4}{B}, mv 5). Only the mv>=3 cards are legal; agent picks
        // the 5-drop.
        var goyf = SeedHandCard(_bob, "Tarmogoyf", "{1}{G}");
        var threeDrop = SeedHandCard(_bob, "Hero's Downfall", "{1}{B}{B}");
        var fiveDrop = SeedHandCard(_bob, "Grave Titan", "{4}{B}{B}");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(candidates => candidates.First(c => c.Name == "Grave Titan"));

        var def = PelakkaPredationFactory.BuildSpellDefinition(
            resolver: o => o!, agent: agent, eventBus: null);

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        // Grave Titan moved to graveyard; goyf (mv 2) + 3-drop still in hand.
        fiveDrop.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(fiveDrop);
        _bob.Zones.Hand.GetCards().Should().Contain(new[] { goyf, threeDrop });
    }

    [Fact]
    public void Resolve_NoAgent_FirstLegalFallback_ManaValue3OrGreater()
    {
        // Bob's hand: Bolt (mv 1), Mountain (land mv 0), Hero's Downfall (mv 3).
        // With no agent, deterministic fallback = first card with mv>=3 in
        // hand order = Hero's Downfall. Bolt and the land are below the gate.
        var bolt = SeedHandCard(_bob, "Lightning Bolt", "{R}");
        var mountain = SeedLandHandCard(_bob, "Mountain");
        var downfall = SeedHandCard(_bob, "Hero's Downfall", "{1}{B}{B}");

        var def = PelakkaPredationFactory.BuildSpellDefinition(
            resolver: o => o!, agent: null, eventBus: null);

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        downfall.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(downfall);
        _bob.Zones.Hand.GetCards().Should().Contain(new[] { bolt, mountain });
    }

    [Fact]
    public void Resolve_AnyCardTypeWithManaValue3OrGreater_IsLegal()
    {
        // The filter is mana value only — NO type restriction. A mv-3
        // noncreature (e.g. a 3-mana enchantment-style card) is a legal pick.
        // Modelled with a plain card at {1}{1}{1} (mv 3).
        var threeDrop = SeedHandCard(_bob, "Some Enchantment", "{2}{W}");

        var def = PelakkaPredationFactory.BuildSpellDefinition(
            resolver: o => o!, agent: null, eventBus: null);

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        threeDrop.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_AllCardsBelowManaValue3_NoDiscard()
    {
        // No card has mv>=3 → nothing legal → no discard. No life cost on
        // this card, so the resolution is simply a no-op discard.
        var bolt = SeedHandCard(_bob, "Lightning Bolt", "{R}");
        var goyf = SeedHandCard(_bob, "Tarmogoyf", "{1}{G}");
        var land = SeedLandHandCard(_bob, "Mountain");

        var def = PelakkaPredationFactory.BuildSpellDefinition(
            resolver: o => o!, agent: null, eventBus: null);

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Hand.GetCards().Should().Contain(new[] { bolt, goyf, land });
    }

    [Fact]
    public void Resolve_EmptyHand_NoDiscard()
    {
        var def = PelakkaPredationFactory.BuildSpellDefinition(
            resolver: o => o!, agent: null, eventBus: null);

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_PublishesCardRevealedEventPerHandCard_ReasonIsPelakkaPredation()
    {
        var h1 = SeedHandCard(_bob, "Lightning Bolt", "{R}");
        var h2 = SeedHandCard(_bob, "Hero's Downfall", "{1}{B}{B}");
        var h3 = SeedLandHandCard(_bob, "Mountain");

        var bus = new EventBus();
        var reveals = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(r => reveals.Add(r));

        var def = PelakkaPredationFactory.BuildSpellDefinition(
            resolver: o => o!, agent: null, eventBus: bus);

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        reveals.Should().HaveCount(3);
        reveals.Select(r => r.Card).Should().Contain(new[] { h1, h2, h3 });
        reveals.Select(r => r.Reason).Should().AllBe("Pelakka Predation");
    }

    [Fact]
    public void Resolve_IllegalTarget_DoesNothing()
    {
        // CR 608.2b — single illegal target → spell does nothing.
        var def = PelakkaPredationFactory.BuildSpellDefinition(
            resolver: o => "not-a-player", agent: null, eventBus: null);

        var bolt = SeedHandCard(_bob, "Hero's Downfall", "{1}{B}{B}");

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        bolt.Zone.Should().Be(ZoneType.Hand, "nothing is discarded on an illegal target");
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void PelakkaCaverns_Identity_Land()
    {
        var land = PelakkaCavernsFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Pelakka Caverns");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Pelakka Caverns is non-basic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PelakkaCaverns_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = PelakkaCavernsFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull("Pelakka Caverns is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Pelakka Predation");
        land.MdfcState!.BackFaceName.Should().Be("Pelakka Caverns");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Pelakka Caverns");
    }

    [Fact]
    public void PelakkaCaverns_HasSingleManaAbility_AddingBlack()
    {
        var land = PelakkaCavernsFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {B} ability");
        manaAbilities[0].ManaGenerated.Black.Should().BeGreaterThan(0, "produces black mana");
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
    }
}
