using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AgathasSoulCauldronFactory"/>.
///
/// Covers:
/// - Card identity (name, Legendary Artifact type)
/// - Owner and controller assignment
/// - Activated ability shape: single Tap cost
/// - Exile effect: exiles the CHOSEN target from whichever graveyard holds it
///   (any player's graveyard)
/// - Counter effect: when exiled card is a creature, +1/+1 counter on the
///   CHOSEN "creature you control"
/// - Counter effect: no counter when exiled card is not a creature
/// - Imprint storage (CR 702.49)
/// - Mana-colour substitution static (CR 609.4b)
/// </summary>
public class AgathasSoulCauldronTests
{
    private readonly Player _alice = new("Alice", 20);

    /// <summary>Resolve the Cauldron's {T} ability with explicit chosen
    /// targets, mirroring what the activation flow does before the effect
    /// runs: request 0 = the card to exile, request 1 = the optional counter
    /// recipient.</summary>
    private static void Resolve(ActivatedAbility ability, ICard exileTarget, Creature? recipient = null)
    {
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { exileTarget },
            recipient != null ? new object[] { recipient } : System.Array.Empty<object>(),
        });
        foreach (var effect in ability.Effects) effect.Execute();
    }

    private static ActivatedAbility TapAbility(Artifact cauldron)
        => cauldron.Abilities.OfType<ActivatedAbility>().Single();

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void AgathasSoulCauldron_NameIsCorrect()
    {
        var c = AgathasSoulCauldronFactory.Create(_alice);

        c.Name.Should().Be("Agatha's Soul Cauldron");
    }

    [Fact]
    public void AgathasSoulCauldron_IsArtifact()
    {
        var c = AgathasSoulCauldronFactory.Create(_alice);

        c.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void AgathasSoulCauldron_IsLegendary()
    {
        var c = AgathasSoulCauldronFactory.Create(_alice);

        c.Supertypes.Should().Contain(CardSupertype.Legendary,
            "Agatha's Soul Cauldron is a Legendary Artifact (legend rule, CR 704.5j)");
    }

    [Fact]
    public void AgathasSoulCauldron_OwnerAndControllerAreSet()
    {
        var c = AgathasSoulCauldronFactory.Create(_alice);

        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void AgathasSoulCauldron_HasExactlyOneActivatedAbility()
    {
        var c = AgathasSoulCauldronFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "only the {T}: exile ability is an activated ability");
    }

    [Fact]
    public void AgathasSoulCauldron_TapAbility_HasSingleTapCost()
    {
        var c = AgathasSoulCauldronFactory.Create(_alice);
        var ability = TapAbility(c);

        ability.Costs.Should().HaveCount(1, "only a tap cost");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(cost => cost.CostType == AdditionalCostType.Tap,
                "the {T} cost");
    }

    [Fact]
    public void AgathasSoulCauldron_TapAbility_DeclaresGraveyardAndCreatureTargets()
    {
        var c = AgathasSoulCauldronFactory.Create(_alice);
        var ability = TapAbility(c);

        ability.TargetRequests.Should().HaveCount(2);
        ability.TargetRequests[0].Description.Should().Contain("graveyard");
        ability.TargetRequests[0].MinTargets.Should().Be(1, "must exile a card");
        ability.TargetRequests[1].Description.Should().Contain("creature you control");
        ability.TargetRequests[1].MinTargets.Should().Be(0,
            "the counter recipient is optional — non-creature exile / no creatures");
    }

    // -----------------------------------------------------------------------
    // Exile effect — card movement
    // -----------------------------------------------------------------------

    [Fact]
    public void AgathasSoulCauldron_ExileEffect_MovesChosenGraveyardCardToExile()
    {
        var alice = new Player("Alice", 20);
        var card = new Card("Dead Card", "");
        card.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);

        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        Resolve(TapAbility(cauldron), card);

        alice.Zones.Exile.GetCards().Should().Contain(card,
            "the exile effect moves the chosen graveyard card to exile");
        alice.Zones.Graveyard.GetCards().Should().NotContain(card,
            "the card is removed from the graveyard");
        card.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void AgathasSoulCauldron_ExileEffect_NoTargetChosen_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        var ability = TapAbility(cauldron);

        // No targets set (e.g. empty graveyard — nothing legal to choose).
        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow("no chosen target is a no-op");
        alice.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void AgathasSoulCauldron_ExileEffect_CanExileFromAnyGraveyard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // The card to exile sits in BOB's graveyard — "a graveyard", not just
        // the controller's own.
        var bobsCard = new Creature("Bob's Bear", "1G", 2, 2);
        bobsCard.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(bobsCard);
        bobsCard.SetZone(ZoneType.Graveyard);

        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        Resolve(TapAbility(cauldron), bobsCard);

        bob.Zones.Exile.GetCards().Should().Contain(bobsCard,
            "the card is exiled from its owner's graveyard, even an opponent's");
        bob.Zones.Graveyard.GetCards().Should().NotContain(bobsCard);
        cauldron.ImprintedCards.Should().Contain(bobsCard,
            "an exiled creature card is imprinted regardless of whose graveyard it came from");
    }

    // -----------------------------------------------------------------------
    // +1/+1 counter placement
    // -----------------------------------------------------------------------

    [Fact]
    public void AgathasSoulCauldron_ExileEffect_CreatureCard_AddsCounterToChosenCreature()
    {
        var alice = new Player("Alice", 20);

        var deadCreature = new Creature("Dead Bear", "1G", 2, 2);
        deadCreature.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(deadCreature);
        deadCreature.SetZone(ZoneType.Graveyard);

        // Two creatures on the battlefield; the counter must go to the CHOSEN
        // one, not just "the first".
        var bystander = new Creature("Bystander", "1G", 2, 2);
        bystander.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(bystander);
        bystander.SetZone(ZoneType.Battlefield);

        var chosen = new Creature("Chosen Bear", "1G", 2, 2);
        chosen.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(chosen);
        chosen.SetZone(ZoneType.Battlefield);

        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        Resolve(TapAbility(cauldron), deadCreature, recipient: chosen);

        chosen.Counters.Count(CounterType.PlusOnePlusOne)
            .Should().Be(1, "the chosen creature-you-control gains the +1/+1 counter");
        bystander.Counters.Count(CounterType.PlusOnePlusOne)
            .Should().Be(0, "the unchosen creature is untouched");
    }

    [Fact]
    public void AgathasSoulCauldron_ExileEffect_NonCreatureCard_DoesNotAddCounter()
    {
        var alice = new Player("Alice", 20);

        var instant = new Instant("Shock", "R");
        instant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(instant);
        instant.SetZone(ZoneType.Graveyard);

        var liveCreature = new Creature("Live Bear", "1G", 2, 2);
        liveCreature.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(liveCreature);
        liveCreature.SetZone(ZoneType.Battlefield);

        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        // Even if a recipient is offered, a non-creature exile places no counter.
        Resolve(TapAbility(cauldron), instant, recipient: liveCreature);

        liveCreature.Counters.Count(CounterType.PlusOnePlusOne)
            .Should().Be(0, "a non-creature card was exiled — no counter placed");
    }

    [Fact]
    public void AgathasSoulCauldron_ExileEffect_CreatureCard_NoRecipientChosen_DoesNotThrow()
    {
        var alice = new Player("Alice", 20);

        var deadCreature = new Creature("Dead Bear", "1G", 2, 2);
        deadCreature.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(deadCreature);
        deadCreature.SetZone(ZoneType.Graveyard);

        var cauldron = AgathasSoulCauldronFactory.Create(alice);

        var act = () => Resolve(TapAbility(cauldron), deadCreature);

        act.Should().NotThrow("no recipient to buff is silently handled");
        cauldron.ImprintedCards.Should().Contain(deadCreature,
            "the creature is still exiled + imprinted even without a counter recipient");
    }

    // -----------------------------------------------------------------------
    // CR 702.49 — Imprint storage
    // -----------------------------------------------------------------------

    [Fact]
    public void AgathasSoulCauldron_ExilingCreatureCard_ImprintsItOnCauldron()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Dead Bear", "1G", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        Resolve(TapAbility(cauldron), bear);

        cauldron.ImprintedCards.Should().Contain(bear,
            "exiling a creature card via the Cauldron imprints it (CR 702.49)");
    }

    [Fact]
    public void AgathasSoulCauldron_ExilingNonCreatureCard_DoesNotImprintIt()
    {
        var alice = new Player("Alice", 20);
        var land = new Land("Forest");
        land.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(land);
        land.SetZone(ZoneType.Graveyard);

        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        Resolve(TapAbility(cauldron), land);

        cauldron.ImprintedCards.Should().NotContain(land,
            "only creature cards are imprinted; non-creature cards are not");
    }

    [Fact]
    public void AgathasSoulCauldron_ExilingMultipleCreatures_ImprintsAll()
    {
        var alice = new Player("Alice", 20);

        var bear1 = new Creature("Bear 1", "1G", 2, 2);
        bear1.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear1);
        bear1.SetZone(ZoneType.Graveyard);

        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        var ability = TapAbility(cauldron);

        Resolve(ability, bear1);

        var bear2 = new Creature("Bear 2", "1G", 2, 2);
        bear2.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear2);
        bear2.SetZone(ZoneType.Graveyard);

        Resolve(ability, bear2);

        cauldron.ImprintedCards.Should().HaveCount(2)
            .And.Contain(bear1)
            .And.Contain(bear2,
                "each creature card exiled via the Cauldron is independently imprinted");
    }

    // -----------------------------------------------------------------------
    // CR 609.4b — mana-colour-substitution permission
    // -----------------------------------------------------------------------

    [Fact]
    public void AgathasSoulCauldron_ContributesAnyColorForCreatureAbilities()
    {
        var alice = new Player("Alice", 20);
        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        cauldron.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(cauldron);

        cauldron.Abilities.OfType<ManaColorSubstitutionPermission>()
            .Should().ContainSingle(p => p.Purpose == ManaSpendPurpose.ActivateCreatureAbilities,
                "Agatha's Soul Cauldron lets you spend mana as though any color to activate creature abilities");

        ManaColorSubstitutionPermission
            .PlayerMaySpendAnyColorFor(alice, ManaSpendPurpose.ActivateCreatureAbilities)
            .Should().BeTrue("the Cauldron is on Alice's battlefield");
    }

    [Fact]
    public void AgathasSoulCauldron_LetsCreatureAbilityBePaidWithOffColorMana()
    {
        var alice = new Player("Alice", 20);
        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        cauldron.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(cauldron);

        // A creature ability that costs {G}. Alice floats only red mana.
        alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("R"));
        var creatureAbilityCost = new ManaColorSubstitutableManaCost(
            Majik.Core.ValueObjects.ManaCost.Parse("G"),
            alice,
            ManaSpendPurpose.ActivateCreatureAbilities);

        creatureAbilityCost.CanPay(alice).Should().BeTrue(
            "the Cauldron lets red mana pay a green pip on a creature ability (CR 609.4b)");

        creatureAbilityCost.Pay(alice);
        alice.ManaPool.Total.Should().Be(0, "the red mana was spent on the {G} pip");
    }

    [Fact]
    public void AgathasSoulCauldron_PermissionGoneWhenCauldronLeavesBattlefield()
    {
        var alice = new Player("Alice", 20);
        var cauldron = AgathasSoulCauldronFactory.Create(alice);

        // Cauldron in hand, not on the battlefield.
        cauldron.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(cauldron);

        ManaColorSubstitutionPermission
            .PlayerMaySpendAnyColorFor(alice, ManaSpendPurpose.ActivateCreatureAbilities)
            .Should().BeFalse("a static ability only applies from the battlefield (CR 604.1)");
    }

    // -----------------------------------------------------------------------
    // CR 613.1f / 702.49 — ability-grant static (MANA-ability slice)
    //
    // "Creatures you control with +1/+1 counters on them have all activated
    //  abilities of all creature cards exiled with Agatha's Soul Cauldron."
    //
    // The sound, implemented portion: an imprinted creature's "{T}: Add …"
    // mana ability is RE-HOMED to each qualifying bearer — built fresh against
    // the bearer as source, so it taps the bearer (never the exiled card) and
    // adds to the bearer-controller's pool. Re-homing is done by parsing the
    // imprinted card's oracle text; tests inject the oracle lookup so a
    // synthetic creature can carry "{T}: Add {G}" without the full seed.
    // -----------------------------------------------------------------------

    /// <summary>
    /// A stub oracle lookup mapping a card name → a CardEntity with the given
    /// oracle text. Lets the grant re-home a synthetic imprinted creature's
    /// mana ability without loading the embedded seed.
    /// </summary>
    private static System.Func<string, Majik.Core.CardData.CardEntity?> OracleStub(
        params (string name, string oracle)[] entries)
    {
        var map = entries.ToDictionary(
            e => e.name,
            e => new Majik.Core.CardData.CardEntity { Name = e.name, OracleText = e.oracle },
            System.StringComparer.OrdinalIgnoreCase);
        return name => map.TryGetValue(name, out var e) ? e : null;
    }

    private static bool ProducesGreen(IManaAbility a) => a.ManaGenerated.Green == 1;

    /// <summary>Build a fully-wired Cauldron with an injected oracle lookup.</summary>
    private static Artifact GrantingCauldron(
        Player owner,
        Majik.Core.Effects.ContinuousEffectsService effects,
        Majik.Core.Events.IEventBus bus,
        System.Func<string, Majik.Core.CardData.CardEntity?> oracleLookup,
        System.Func<System.Collections.Generic.IEnumerable<Player>?>? roster = null)
        => AgathasSoulCauldronFactory.Create(owner, effects, bus, roster, oracleLookup);

    [Fact]
    public void Grant_BearerWithCounter_GainsImprintedCreaturesManaAbility_HomedToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature card: "{T}: Add {G}." (in Alice's graveyard).
        var manaDork = new Creature("Llanowar Stub", "G", 1, 1);
        manaDork.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(manaDork);
        manaDork.SetZone(ZoneType.Graveyard);

        // A creature with a +1/+1 counter already on the battlefield.
        var bearer = new Creature("Counter Bear", "1G", 2, 2);
        bearer.SetOwner(alice);
        bearer.ChangeController(alice);
        bearer.Counters.Add(CounterType.PlusOnePlusOne, 1);
        bearer.ClearSummoningSickness();
        alice.Zones.Library.AddCard(bearer);
        zones.MoveCard(bearer, ZoneType.Library, ZoneType.Battlefield, alice);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Llanowar Stub", "{T}: Add {G}.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        // Imprint the mana dork.
        Resolve(TapAbility(cauldron), manaDork);

        // The bearer now has a re-homed mana ability.
        var granted = bearer.Abilities.OfType<IManaAbility>().Where(ProducesGreen).ToList();
        granted.Should().NotBeEmpty(
            "a bearer with a +1/+1 counter gains the imprinted creature's {T}: Add {G} ability");

        var ability = granted[0];
        ability.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card (closure source)");

        // Activating it taps the BEARER and adds {G} to Alice's pool; the exiled
        // card is untouched.
        ability.CanActivate().Should().BeTrue("the bearer is untapped + not summoning sick");
        var produced = ability.Activate();
        produced.Green.Should().Be(1, "activating the re-homed ability adds {G}");
        bearer.IsTapped.Should().BeTrue("the re-homed {T} ability taps the BEARER");
        manaDork.IsTapped.Should().BeFalse(
            "the exiled imprinted card is never tapped — re-home is sound");
    }

    [Fact]
    public void Grant_CreatureWithoutCounter_DoesNotGainAbilities()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var manaDork = new Creature("Llanowar Stub", "G", 1, 1);
        manaDork.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(manaDork);
        manaDork.SetZone(ZoneType.Graveyard);

        // No +1/+1 counter on this creature.
        var plain = new Creature("Plain Bear", "1G", 2, 2);
        plain.SetOwner(alice);
        plain.ChangeController(alice);
        alice.Zones.Library.AddCard(plain);
        zones.MoveCard(plain, ZoneType.Library, ZoneType.Battlefield, alice);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Llanowar Stub", "{T}: Add {G}.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), manaDork);

        plain.Abilities.OfType<IManaAbility>().Where(ProducesGreen)
            .Should().BeEmpty(
                "the grant only applies to creatures you control WITH +1/+1 counters");
    }

    [Fact]
    public void Grant_CreatureGainsCounterAfterImprint_PicksUpAbility()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var manaDork = new Creature("Llanowar Stub", "G", 1, 1);
        manaDork.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(manaDork);
        manaDork.SetZone(ZoneType.Graveyard);

        // A creature that will RECEIVE the +1/+1 counter from the Cauldron's
        // own {T} ability — it starts with no counter.
        var recipient = new Creature("Future Bearer", "1G", 2, 2);
        recipient.SetOwner(alice);
        recipient.ChangeController(alice);
        recipient.ClearSummoningSickness();
        alice.Zones.Library.AddCard(recipient);
        zones.MoveCard(recipient, ZoneType.Library, ZoneType.Battlefield, alice);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Llanowar Stub", "{T}: Add {G}.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        // Imprint the dork AND drop the +1/+1 counter on the recipient in one
        // resolution — exactly the card's own play pattern.
        Resolve(TapAbility(cauldron), manaDork, recipient: recipient);

        recipient.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        recipient.Abilities.OfType<IManaAbility>().Where(ProducesGreen)
            .Should().NotBeEmpty(
                "after gaining a +1/+1 counter the creature joins the group and gains the ability (CR 611.2c)");
    }

    [Fact]
    public void Grant_CauldronLeavesBattlefield_RevokesGrant()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var manaDork = new Creature("Llanowar Stub", "G", 1, 1);
        manaDork.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(manaDork);
        manaDork.SetZone(ZoneType.Graveyard);

        var bearer = new Creature("Counter Bear", "1G", 2, 2);
        bearer.SetOwner(alice);
        bearer.ChangeController(alice);
        bearer.Counters.Add(CounterType.PlusOnePlusOne, 1);
        alice.Zones.Library.AddCard(bearer);
        zones.MoveCard(bearer, ZoneType.Library, ZoneType.Battlefield, alice);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Llanowar Stub", "{T}: Add {G}.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);
        Resolve(TapAbility(cauldron), manaDork);

        // Sanity: granted while on the battlefield.
        bearer.Abilities.OfType<IManaAbility>().Where(ProducesGreen)
            .Should().NotBeEmpty();

        // Cauldron leaves play — the grant is revoked (CR 613.6e).
        zones.MoveCard(cauldron, ZoneType.Battlefield, ZoneType.Graveyard, alice);
        effects.Prune();

        bearer.Abilities.OfType<IManaAbility>().Where(ProducesGreen)
            .Should().BeEmpty("with the Cauldron gone the granted ability is lost (CR 613.6e)");
    }

    [Fact]
    public void Grant_NonCreatureImprintContributesNothing()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // An instant in the graveyard (not a creature) with mana-ability-shaped
        // text — it must NOT be imprinted, so nothing is granted.
        var notACreature = new Instant("Manamorphose Stub", "1G");
        notACreature.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(notACreature);
        notACreature.SetZone(ZoneType.Graveyard);

        var bearer = new Creature("Counter Bear", "1G", 2, 2);
        bearer.SetOwner(alice);
        bearer.ChangeController(alice);
        bearer.Counters.Add(CounterType.PlusOnePlusOne, 1);
        alice.Zones.Library.AddCard(bearer);
        zones.MoveCard(bearer, ZoneType.Library, ZoneType.Battlefield, alice);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Manamorphose Stub", "{T}: Add {G}.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), notACreature);

        cauldron.ImprintedCards.Should().NotContain(notACreature,
            "only creature cards are imprinted");
        bearer.Abilities.OfType<IManaAbility>().Where(ProducesGreen)
            .Should().BeEmpty("a non-creature imprint grants nothing");
    }

    [Fact]
    public void Grant_ImprintedJoragaTreespeaker_ReHomesOnlyOwnManaAbility_NotQuotedAnthem()
    {
        // Joraga Treespeaker's LEVEL 5+ line is an anthem that GRANTS a quoted
        // "{T}: Add {G}{G}." to OTHER Elves — that quoted ability is not Joraga's
        // OWN activated ability, so Agatha must re-home Joraga's LEVEL 1-4
        // "{T}: Add {G}{G}." mana ability EXACTLY ONCE, never twice (CR 613.1f /
        // 702.49). This is the mana-ability re-source-able shape the Joraga
        // deferral closes.
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var joraga = new Creature(
            "Joraga Treespeaker", "G", 1, 1,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Druid });
        joraga.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(joraga);
        joraga.SetZone(ZoneType.Graveyard);

        var bearer = new Creature("Counter Bear", "1G", 2, 2);
        bearer.SetOwner(alice);
        bearer.ChangeController(alice);
        bearer.Counters.Add(CounterType.PlusOnePlusOne, 1);
        bearer.ClearSummoningSickness();
        alice.Zones.Library.AddCard(bearer);
        zones.MoveCard(bearer, ZoneType.Library, ZoneType.Battlefield, alice);

        const string joragaOracle =
            "Level up {1}{G} ({1}{G}: Put a level counter on this. "
            + "Level up only as a sorcery.)\n"
            + "LEVEL 1-4\n1/2\n{T}: Add {G}{G}.\n"
            + "LEVEL 5+\n1/4\n"
            + "Elves you control have \"{T}: Add {G}{G}.\"";

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Joraga Treespeaker", joragaOracle)));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), joraga);

        var granted = bearer.Abilities.OfType<IManaAbility>()
            .Where(a => a.ManaGenerated.Green == 2)
            .ToList();
        granted.Should().ContainSingle(
            "Joraga's OWN {T}: Add {G}{G} is re-homed exactly once — the LEVEL 5+ "
            + "quoted anthem ability is granted to OTHER Elves, not Joraga, so it "
            + "is NOT re-homed to the Agatha bearer (CR 613.1f / 702.49)");
        granted[0].Source.Should().BeSameAs(bearer,
            "the granted mana ability is re-homed to the BEARER, not the exiled card");
    }

    [Fact]
    public void Grant_AppliesToCreatureYouControlButOpponentOwns()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var manaDork = new Creature("Llanowar Stub", "G", 1, 1);
        manaDork.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(manaDork);
        manaDork.SetZone(ZoneType.Graveyard);

        // Bob owns the creature; it enters under Bob, then Alice steals control.
        // It physically lives in Bob's battlefield zone but Alice controls it.
        var stolen = new Creature("Stolen Bear", "1G", 2, 2);
        stolen.SetOwner(bob);
        stolen.ChangeController(bob);
        stolen.Counters.Add(CounterType.PlusOnePlusOne, 1);
        bob.Zones.Library.AddCard(stolen);
        zones.MoveCard(stolen, ZoneType.Library, ZoneType.Battlefield, bob);
        stolen.ChangeController(alice);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Llanowar Stub", "{T}: Add {G}.")),
            roster: () => new[] { alice, bob });
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), manaDork);

        stolen.Abilities.OfType<IManaAbility>().Where(ProducesGreen)
            .Should().NotBeEmpty(
                "a creature Alice controls but Bob owns is one of 'creatures you control' " +
                "and gains the ability, even living in Bob's battlefield zone (CR 110.2 / 700.6)");
    }

    // -----------------------------------------------------------------------
    // CR 613.1f / 702.49 — ability-grant static (NON-mana activated-ability
    // slice). An imprinted creature's firebreathing / pinger / sac-pinger is
    // RE-HOMED to each qualifying bearer via OracleActivatedAbilityBinder —
    // built fresh against the bearer as source, so the cost taps/sacrifices the
    // BEARER and the effect references the BEARER ("this creature" = bearer).
    // -----------------------------------------------------------------------

    /// <summary>Granted non-mana activated abilities on a bearer (i.e. the
    /// re-homed firebreathing / pinger abilities, excluding mana abilities).</summary>
    private static System.Collections.Generic.List<ActivatedAbility> GrantedActivated(Creature bearer)
        => bearer.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();

    /// <summary>Wire a bearer creature onto the battlefield with a +1/+1 counter
    /// and an effects service so granted self-pump can be observed via P/T.</summary>
    private static Creature SeatedBearer(
        Player owner,
        Majik.Core.Effects.ContinuousEffectsService effects,
        Majik.Core.Services.ZoneService zones,
        int power = 2, int toughness = 2)
    {
        var bearer = new Creature("Counter Bear", "1G", power, toughness);
        bearer.SetOwner(owner);
        bearer.ChangeController(owner);
        bearer.Counters.Add(CounterType.PlusOnePlusOne, 1);
        bearer.ClearSummoningSickness();
        owner.Zones.Library.AddCard(bearer);
        zones.MoveCard(bearer, ZoneType.Library, ZoneType.Battlefield, owner);
        bearer.ActiveEffects = effects;
        return bearer;
    }

    [Fact]
    public void Grant_NonMana_Firebreathing_RehomesSelfPumpToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature: "{R}: This creature gets +1/+0 until end of turn."
        var hellhound = new Creature("Fiery Stub", "1RR", 2, 2);
        hellhound.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(hellhound);
        hellhound.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Fiery Stub", "{R}: This creature gets +1/+0 until end of turn.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), hellhound);

        // The bearer gained a non-mana pump ability sourced on itself.
        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle("the bearer gains the imprinted creature's firebreathing");
        var pump = granted[0];
        pump.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        pump.Costs.OfType<ManaCostCost>().Should().ContainSingle(c => c.Description.Contains("R"));

        // Activating it pumps the BEARER (CR 613.1f Layer 7c), not the dork.
        // (Base 2/2 + the +1/+1 counter = 3/3 before the pump.)
        var powerBefore = bearer.GetPower();
        var toughnessBefore = bearer.GetToughness();
        foreach (var effect in pump.Effects) effect.Execute();
        bearer.GetPower().Should().Be(powerBefore + 1,
            "the re-homed firebreathing pumps the BEARER's power");
        bearer.GetToughness().Should().Be(toughnessBefore,
            "+1/+0 leaves the BEARER's toughness unchanged");
    }

    [Fact]
    public void Grant_NonMana_PumpOther_RehomesTargetedPumpToChosenCreature()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature whose only ability pumps ANOTHER creature:
        // "{G}: Target creature gets +1/+1 until end of turn." (a self-source
        // "lord-on-demand" / combat-trick payoff — a common targeted-pump shape,
        // e.g. an Overrun-style activated buff). Re-homing is sound: the SOURCE /
        // cost-payer is the BEARER, and the pump applies to the CHOSEN target
        // creature (PumpUntilEndOfTurnEffect against the target's own
        // ActiveEffects), never the exiled imprinted card (CR 613.1f Layer 7c).
        var lord = new Creature("Lord Stub", "1G", 2, 2);
        lord.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(lord);
        lord.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // A separate creature on the battlefield to receive the targeted pump.
        var ally = new Creature("Ally Bear", "1G", 2, 2);
        ally.SetOwner(alice);
        ally.ChangeController(alice);
        alice.Zones.Library.AddCard(ally);
        zones.MoveCard(ally, ZoneType.Library, ZoneType.Battlefield, alice);
        ally.ActiveEffects = effects;

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Lord Stub", "{G}: Target creature gets +1/+1 until end of turn.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), lord);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle("the bearer gains the imprinted creature's targeted pump");
        var pump = granted[0];
        pump.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        pump.Costs.OfType<ManaCostCost>().Should().ContainSingle(c => c.Description.Contains("G"));
        pump.TargetRequests.Should().ContainSingle(t => t.Description.Contains("target creature"),
            "a targeted pump requires a 1..1 target-creature request");

        // Resolving with the ALLY chosen pumps the ALLY (+1/+1), not the bearer
        // and not the exiled card.
        pump.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ally } });
        var allyPowerBefore = ally.GetPower();
        var allyToughnessBefore = ally.GetToughness();
        var bearerPowerBefore = bearer.GetPower();
        foreach (var effect in pump.Effects) effect.Execute();
        ally.GetPower().Should().Be(allyPowerBefore + 1,
            "the re-homed targeted pump raises the CHOSEN creature's power");
        ally.GetToughness().Should().Be(allyToughnessBefore + 1,
            "the re-homed targeted pump raises the CHOSEN creature's toughness");
        bearer.GetPower().Should().Be(bearerPowerBefore,
            "the bearer (mere source) is not pumped — only the chosen target");
        lord.GetPower().Should().Be(2,
            "the exiled imprinted card is untouched");

        // CR 514.2 — the targeted pump expires at cleanup.
        effects.ExpireEndOfTurn();
        ally.GetPower().Should().Be(allyPowerBefore,
            "the granted targeted pump expires at end of turn");
    }

    [Fact]
    public void Grant_NonMana_SignedPump_RehomesNegativeDeltaSelfPumpToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature with a SIGNED-delta self-pump — a real Modern shape
        // (Aetherling "{1}: ~ gets +1/-1", Canyon Crab "{1}{U}: ~ gets +2/-2",
        // the Flowstone cycle "{R}: ~ gets +1/-1"). The negative toughness delta
        // is fully sound to re-home: PumpUntilEndOfTurnEffect takes raw ints, so
        // it adds the signed deltas to the BEARER's characteristics.
        var crab = new Creature("Canyon Stub", "2U", 0, 4);
        crab.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(crab);
        crab.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones, power: 2, toughness: 5);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Canyon Stub", "{1}{U}: This creature gets +2/-2 until end of turn.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), crab);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle("the bearer gains the imprinted creature's signed self-pump");
        var pump = granted[0];
        pump.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");

        // Activating it applies +2/-2 to the BEARER (CR 613.1f Layer 7c).
        // Base 2/5 + the +1/+1 counter = 3/6 before the pump → 5/4 after.
        var powerBefore = bearer.GetPower();
        var toughnessBefore = bearer.GetToughness();
        foreach (var effect in pump.Effects) effect.Execute();
        bearer.GetPower().Should().Be(powerBefore + 2,
            "the re-homed signed self-pump raises the BEARER's power by +2");
        bearer.GetToughness().Should().Be(toughnessBefore - 2,
            "the negative toughness delta lowers the BEARER's toughness by 2");
    }

    [Fact]
    public void Grant_NonMana_SelfKeywordGrant_RehomesKeywordToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature: "{R}: This creature gains first strike until end of
        // turn." (Firebreathing's keyword sibling — a self-keyword grant.)
        var fervent = new Creature("Fervent Stub", "1R", 2, 2);
        fervent.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(fervent);
        fervent.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Fervent Stub", "{R}: This creature gains first strike until end of turn.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), fervent);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle("the bearer gains the imprinted creature's self-keyword grant");
        var grant = granted[0];
        grant.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        grant.Costs.OfType<ManaCostCost>().Should().ContainSingle(c => c.Description.Contains("R"));

        // The BEARER should not yet have first strike.
        effects.Compute(bearer).Keywords.Should().NotContain("First Strike");

        // Activating it grants the BEARER first strike until end of turn (CR 613.1f
        // Layer 6), not the exiled card.
        foreach (var effect in grant.Effects) effect.Execute();
        effects.Compute(bearer).Keywords.Should().Contain("First Strike",
            "the re-homed self-keyword grant gives the BEARER the keyword");

        // CR 514.2 — the grant expires at cleanup.
        effects.ExpireEndOfTurn();
        effects.Compute(bearer).Keywords.Should().NotContain("First Strike",
            "the granted keyword expires at end of turn");
    }

    [Fact]
    public void Grant_NonMana_ProtectionGrant_RehomesChosenColorProtectionToChosenCreature()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature: Mother of Runes' protection grant —
        // "{T}: Target creature you control gains protection from the color of
        // your choice until end of turn." Re-homing is sound: the SOURCE /
        // cost-payer is the BEARER (its own {T} cost taps it), and the
        // ProtectionAbility lands on the CHOSEN target creature via a self-sourced
        // GrantAbilityEffect against the target's own ActiveEffects, never the
        // exiled imprinted card (CR 613.1f Layer 6; CR 702.16). The chosen colour
        // defaults to white (first WUBRG) on the deterministic binder path — same
        // posture as MotherOfRunesFactory's WhitePicker default (the agent colour
        // prompt is a documented v1 gap).
        var mom = new Creature("Runes Stub", "W", 1, 1);
        mom.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(mom);
        mom.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // A separate creature on the battlefield to receive the protection grant.
        var ally = new Creature("Ally Bear", "1G", 2, 2);
        ally.SetOwner(alice);
        ally.ChangeController(alice);
        alice.Zones.Library.AddCard(ally);
        zones.MoveCard(ally, ZoneType.Library, ZoneType.Battlefield, alice);
        ally.ActiveEffects = effects;

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Runes Stub",
                "{T}: Target creature you control gains protection from the color of your choice until end of turn.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), mom);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle("the bearer gains the imprinted creature's protection grant");
        var grant = granted[0];
        grant.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        grant.Costs.OfType<Majik.Core.Costs.AdditionalCost>().Should()
            .ContainSingle("the {T} cost taps the BEARER");
        grant.TargetRequests.Should().ContainSingle(t => t.Description.Contains("target creature"),
            "a protection grant requires a 1..1 target-creature request");

        // The ally has no protection yet.
        Majik.Core.Rules.Protection.HasProtectionFromColor(ally, Majik.Core.ValueObjects.ManaColor.White)
            .Should().BeFalse("no grant has resolved yet");

        // Resolving with the ALLY chosen grants the ALLY protection from white
        // (the deterministic default colour), not the bearer and not the exiled card.
        grant.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ally } });
        foreach (var effect in grant.Effects) effect.Execute();

        Majik.Core.Rules.Protection.HasProtectionFromColor(ally, Majik.Core.ValueObjects.ManaColor.White)
            .Should().BeTrue("the re-homed protection grant gives the CHOSEN creature protection from the chosen colour");
        Majik.Core.Rules.Protection.HasProtectionFromColor(bearer, Majik.Core.ValueObjects.ManaColor.White)
            .Should().BeFalse("the bearer (mere source) is not protected — only the chosen target");

        // CR 514.2 — the protection grant expires at cleanup.
        effects.ExpireEndOfTurn();
        Majik.Core.Rules.Protection.HasProtectionFromColor(ally, Majik.Core.ValueObjects.ManaColor.White)
            .Should().BeFalse("the granted protection expires at end of turn");
    }

    [Fact]
    public void Grant_NonMana_KeywordGrantOther_RehomesKeywordToChosenCreature()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature whose only ability grants a keyword to ANOTHER
        // creature: "{1}{W}: Another target creature gains lifelink until end of
        // turn." (Heliod, Sun-Crowned's activated half — the keyword sibling of
        // the targeted-pump shape.) Re-homing is sound: the SOURCE / cost-payer
        // is the BEARER, and the GrantKeywordUntilEndOfTurnEffect lands on the
        // CHOSEN target creature's own ActiveEffects, never the exiled imprinted
        // card (CR 613.1f Layer 6).
        var heliodStub = new Creature("Heliod Stub", "2W", 5, 5);
        heliodStub.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(heliodStub);
        heliodStub.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // A separate creature on the battlefield to receive the keyword grant.
        var ally = new Creature("Ally Bear", "1G", 2, 2);
        ally.SetOwner(alice);
        ally.ChangeController(alice);
        alice.Zones.Library.AddCard(ally);
        zones.MoveCard(ally, ZoneType.Library, ZoneType.Battlefield, alice);
        ally.ActiveEffects = effects;

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Heliod Stub",
                "{1}{W}: Another target creature gains lifelink until end of turn.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), heliodStub);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle("the bearer gains the imprinted creature's targeted keyword grant");
        var grant = granted[0];
        grant.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        grant.Costs.OfType<ManaCostCost>().Should().ContainSingle(c => c.Description.Contains("W"));
        grant.TargetRequests.Should().ContainSingle(t => t.Description.Contains("target creature"),
            "a targeted keyword grant requires a 1..1 target-creature request");

        // The ally has no lifelink yet.
        effects.Compute(ally).Keywords.Should().NotContain("Lifelink");

        // Resolving with the ALLY chosen grants the ALLY lifelink (CR 613.1f
        // Layer 6), not the bearer and not the exiled card.
        grant.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ally } });
        foreach (var effect in grant.Effects) effect.Execute();
        effects.Compute(ally).Keywords.Should().Contain("Lifelink",
            "the re-homed targeted keyword grant gives the CHOSEN creature the keyword");
        effects.Compute(bearer).Keywords.Should().NotContain("Lifelink",
            "the bearer (mere source) does not gain the keyword — only the chosen target");

        // CR 514.2 — the granted keyword expires at cleanup.
        effects.ExpireEndOfTurn();
        effects.Compute(ally).Keywords.Should().NotContain("Lifelink",
            "the granted keyword expires at end of turn");
    }

    [Fact]
    public void Grant_NonMana_Pinger_RehomesTapAndDamageToBearer()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature: "{T}: This creature deals 1 damage to any target."
        var pinger = new Creature("Pinger Stub", "2R", 1, 1);
        pinger.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(pinger);
        pinger.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Pinger Stub", "{T}: This creature deals 1 damage to any target.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), pinger);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle("the bearer gains the imprinted creature's pinger");
        var ping = granted[0];
        ping.Source.Should().BeSameAs(bearer, "re-homed to the BEARER");
        ping.TargetRequests.Should().ContainSingle(t => t.Description.Contains("any target"));

        // The cost is a {T} that taps the BEARER.
        var tapCost = ping.Costs.OfType<AdditionalCost>()
            .Single(c => c.CostType == AdditionalCostType.Tap);
        bearer.IsTapped.Should().BeFalse();
        tapCost.Pay(alice);
        bearer.IsTapped.Should().BeTrue("the re-homed {T} cost taps the BEARER, not the exiled card");
        pinger.IsTapped.Should().BeFalse("the exiled imprinted card is never tapped");

        // Resolving deals 1 to the chosen target.
        ping.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        var lifeBefore = bob.LifeTotal;
        foreach (var effect in ping.Effects) effect.Execute();
        bob.LifeTotal.Should().Be(lifeBefore - 1,
            "the re-homed pinger deals its damage to the chosen target");
    }

    [Fact]
    public void Grant_NonMana_PowerPinger_RehomesDamageEqualToBearerPower()
    {
        // agatha-oracle-shape-spikeshot-goblin-ping-equal-power: the
        // OracleActivatedAbilityBinder now reconstructs the POWER-pinger oracle
        // shape "{cost}: This creature deals damage equal to its power to <target>."
        // (Spikeshot Goblin's shape). Re-homing is the exact case that motivated
        // the re-sourceable representation: the damage amount MUST read the
        // BEARER's power at resolution (CR 608.2h), never the exiled card's.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature: a 1/2 with Spikeshot's printed power-pinger line.
        var spike = new Creature("Spike Stub", "2R", 1, 2);
        spike.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(spike);
        spike.SetZone(ZoneType.Graveyard);

        // Bearer base 4/4 + the SeatedBearer +1/+1 counter = 5 live power.
        var bearer = SeatedBearer(alice, effects, zones, power: 4, toughness: 4);
        bearer.GetEffectivePower().Should().Be(5,
            "Counter Bear base 4/4 + the +1/+1 counter SeatedBearer adds");

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Spike Stub",
                "{R}, {T}: This creature deals damage equal to its power to any target.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), spike);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle("the bearer gains the imprinted creature's power-pinger");
        var ping = granted[0];
        ping.Source.Should().BeSameAs(bearer, "re-homed to the BEARER");
        ping.TargetRequests.Should().ContainSingle(t => t.Description.Contains("any target"));
        ping.Costs.OfType<ManaCostCost>().Should().ContainSingle(c => c.Description.Contains("R"),
            "the {R} mana cost is reconstructed");
        ping.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the {T} cost taps the BEARER");

        // Resolving deals damage equal to the BEARER's power (5), not the exiled
        // card's printed power (1).
        ping.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        var lifeBefore = bob.LifeTotal;
        foreach (var effect in ping.Effects) effect.Execute();
        bob.LifeTotal.Should().Be(lifeBefore - 5,
            "the re-homed power-pinger deals damage equal to the BEARER's power (5), " +
            "not the exiled imprinted card's printed power (1)");
    }

    [Fact]
    public void Grant_NonMana_SacrificePinger_RehomesSacOfBearer()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted: "Sacrifice this creature: It deals 1 damage to any target."
        var mogg = new Creature("Mogg Stub", "R", 1, 1);
        mogg.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(mogg);
        mogg.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Mogg Stub", "Sacrifice this creature: It deals 1 damage to any target.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), mogg);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle();
        var ping = granted[0];
        ping.Source.Should().BeSameAs(bearer);
        ping.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the re-homed sacrifice cost sacrifices the BEARER");

        // Paying the cost sacrifices the BEARER (moves it to its graveyard).
        var sacCost = ping.Costs.OfType<AdditionalCost>()
            .Single(c => c.CostType == AdditionalCostType.Sacrifice);
        sacCost.Pay(alice);
        bearer.Zone.Should().Be(ZoneType.Graveyard, "the BEARER is sacrificed, not the exiled card");
        mogg.Zone.Should().Be(ZoneType.Exile, "the exiled imprinted card stays exiled");
    }

    [Fact]
    public void Grant_NonMana_ParameterisedKeywordGrant_IsSkippedAsUnsound()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A self-keyword grant for a PARAMETERISED keyword ("protection from
        // red") — outside the binder's closed simple-keyword set. It must be
        // skipped, not emitted broken (CR 613.1f — only exactly-modellable
        // grants are reconstructed).
        var warded = new Creature("Warded Stub", "1W", 2, 2);
        warded.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(warded);
        warded.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Warded Stub", "{W}: This creature gains protection from red until end of turn.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), warded);

        GrantedActivated(bearer).Should().BeEmpty(
            "a parameterised keyword grant is outside the reconstructable simple-keyword set and is skipped");
    }

    [Fact]
    public void Grant_NonMana_SelfCounter_RehomesCounterPlacementToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature whose only ability puts a +1/+1 counter on itself:
        // "{2}: Put a +1/+1 counter on this creature." (e.g. a card with a
        // self-growth ability — a common self-source bespoke shape).
        var grower = new Creature("Grower Stub", "1G", 1, 1);
        grower.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(grower);
        grower.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Grower Stub", "{2}: Put a +1/+1 counter on this creature.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), grower);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's self-counter ability");
        var counterAbility = granted[0];
        counterAbility.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        counterAbility.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("2"));
        counterAbility.TargetRequests.Should().BeEmpty(
            "\"put a +1/+1 counter on this creature\" targets nothing — the bearer is fixed");

        // Activating it puts a +1/+1 counter on the BEARER (not the exiled card).
        var bearerCountersBefore = bearer.Counters.Count(CounterType.PlusOnePlusOne);
        var growerCountersBefore = grower.Counters.Count(CounterType.PlusOnePlusOne);
        foreach (var effect in counterAbility.Effects) effect.Execute();
        bearer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(bearerCountersBefore + 1,
            "the re-homed self-counter ability puts the counter on the BEARER");
        grower.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(growerCountersBefore,
            "the exiled imprinted card never receives the counter");
    }

    [Fact]
    public void Grant_NonMana_RegenerateSelf_RehomesRegenerationShieldToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature whose only ability regenerates itself:
        // "{B}: Regenerate this creature." (River Boa, Drudge Skeletons, Wall of
        // Bone, Twisted Abomination, Lotleth Troll — a very common real shape).
        // Sound to re-home: a regeneration shield (CR 701.18) is a self-source
        // replacement that protects the BEARER, never the exiled card.
        var boa = new Creature("Boa Stub", "1G", 1, 1);
        boa.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(boa);
        boa.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Boa Stub", "{B}: Regenerate this creature.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), boa);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's regenerate-self ability");
        var regenAbility = granted[0];
        regenAbility.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        regenAbility.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("B"));
        regenAbility.TargetRequests.Should().BeEmpty(
            "\"regenerate this creature\" targets nothing — the bearer is fixed");

        // Activating it creates a regeneration shield on the BEARER, not the
        // exiled card (CR 701.18 / 701.15a).
        var bearerShieldsBefore = bearer.RegenerationShieldCount;
        var boaShieldsBefore = boa.RegenerationShieldCount;
        foreach (var effect in regenAbility.Effects) effect.Execute();
        bearer.RegenerationShieldCount.Should().Be(bearerShieldsBefore + 1,
            "the re-homed regenerate-self ability shields the BEARER");
        boa.RegenerationShieldCount.Should().Be(boaShieldsBefore,
            "the exiled imprinted card never receives the shield");
    }

    [Fact]
    public void Grant_NonMana_DrawACard_RehomesDrawToBearerController()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature whose only ability draws a card:
        // "{2}, {T}: Draw a card." (Arcanis-style card-advantage engine — a
        // common self-source draw shape). Re-homing is sound: a draw references
        // the BEARER-CONTROLLER's own library/hand (Fx.DrawCards), never the
        // exiled card (CR 121 / 613.1f).
        var looter = new Creature("Looter Stub", "2U", 1, 1);
        looter.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(looter);
        looter.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // Put a card on top of Alice's library so the granted draw has something
        // to draw.
        var topCard = new Card("Top Card", "");
        topCard.SetOwner(alice);
        alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Looter Stub", "{2}, {T}: Draw a card.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), looter);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's draw-a-card ability");
        var drawAbility = granted[0];
        drawAbility.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        drawAbility.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("2"));
        drawAbility.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the re-homed {T} cost taps the BEARER");
        drawAbility.TargetRequests.Should().BeEmpty(
            "\"draw a card\" targets nothing — the controller's library is fixed");

        // Activating it draws into the BEARER-CONTROLLER's hand.
        var handBefore = alice.Zones.Hand.GetCards().Count();
        foreach (var effect in drawAbility.Effects) effect.Execute();
        alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1,
            "the re-homed draw-a-card ability draws for the bearer's controller");
        alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "the top card of the controller's library is drawn");
    }

    [Fact]
    public void Grant_NonMana_DrawNCards_RehomesMultiDrawToBearerController()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var engine = new Creature("Engine Stub", "3U", 1, 1);
        engine.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(engine);
        engine.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        for (var i = 0; i < 3; i++)
        {
            var c = new Card($"Lib {i}", "");
            c.SetOwner(alice);
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Engine Stub", "{4}: Draw two cards.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), engine);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's draw-two-cards ability");
        var drawAbility = granted[0];

        var handBefore = alice.Zones.Hand.GetCards().Count();
        foreach (var effect in drawAbility.Effects) effect.Execute();
        alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 2,
            "the re-homed \"draw two cards\" ability draws two for the bearer's controller");
    }

    [Fact]
    public void Grant_NonMana_TargetPlayerDraw_RehomesDrawToChosenPlayer()
    {
        // oracle-activated-shape-target-player-draws-card: the
        // OracleActivatedAbilityBinder now reconstructs the TARGETED-player draw
        // shape "{cost}: Target player draws a card." (Endbringer's
        // "{C}, {T}: Target player draws a card."). Unlike self-draw the CHOSEN
        // player draws — re-homing is sound because a draw references the chosen
        // player's OWN library (Fx.DrawCards on ChosenTargets), never the exiled
        // card (CR 121 / 613.1f). The BEARER is only the source / cost-payer.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature: "{T}: Target player draws a card."
        var sage = new Creature("Sage Stub", "2U", 1, 1);
        sage.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(sage);
        sage.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // Put a card on top of BOB's library — the chosen player draws from
        // their OWN library, not the controller's.
        var bobsTop = new Card("Bob's Top", "");
        bobsTop.SetOwner(bob);
        bob.Zones.Library.AddCard(bobsTop);
        bobsTop.SetZone(ZoneType.Library);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Sage Stub", "{T}: Target player draws a card.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), sage);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's target-player-draw ability");
        var drawAbility = granted[0];
        drawAbility.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        drawAbility.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the re-homed {T} cost taps the BEARER");
        drawAbility.TargetRequests.Should().ContainSingle(
            t => t.Description.Contains("target player"),
            "\"target player draws a card\" requests a single target player");

        // Choosing BOB and resolving draws into BOB's hand, not Alice's.
        drawAbility.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        var bobHandBefore = bob.Zones.Hand.GetCards().Count();
        var aliceHandBefore = alice.Zones.Hand.GetCards().Count();
        foreach (var effect in drawAbility.Effects) effect.Execute();
        bob.Zones.Hand.GetCards().Count().Should().Be(bobHandBefore + 1,
            "the re-homed target-player draw draws for the CHOSEN player");
        bob.Zones.Hand.GetCards().Should().Contain(bobsTop,
            "the chosen player draws from their OWN library");
        alice.Zones.Hand.GetCards().Count().Should().Be(aliceHandBefore,
            "the controller does not draw — only the chosen target player does");
    }

    [Fact]
    public void Grant_NonMana_TargetPlayerDrawN_RehomesMultiDrawToChosenPlayer()
    {
        // The N-card variant: "{cost}: Target player draws N cards."
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var sage = new Creature("Sage Stub", "3U", 1, 1);
        sage.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(sage);
        sage.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        for (var i = 0; i < 3; i++)
        {
            var c = new Card($"Bob Lib {i}", "");
            c.SetOwner(bob);
            bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Sage Stub", "{2}, {T}: Target player draws two cards.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), sage);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's target-player-draw-two ability");
        var drawAbility = granted[0];
        drawAbility.Costs.OfType<ManaCostCost>().Should().ContainSingle(c => c.Description.Contains("2"));

        drawAbility.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        var bobHandBefore = bob.Zones.Hand.GetCards().Count();
        foreach (var effect in drawAbility.Effects) effect.Execute();
        bob.Zones.Hand.GetCards().Count().Should().Be(bobHandBefore + 2,
            "the re-homed \"target player draws two cards\" ability draws two for the chosen player");
    }

    [Fact]
    public void Grant_NonMana_GainLife_RehomesLifeGainToBearerController()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature whose only ability gains life:
        // "{T}: You gain 1 life." (a common self-source lifegain shape — e.g.
        // a cleric/soul-warden style {T}: gain payoff). Re-homing is sound: a
        // lifegain references the BEARER-CONTROLLER's own life total
        // (Fx.GainLife), never the exiled card — there is no "this creature" /
        // source reference at all, so it is as sound a re-home as draw
        // (CR 119.3 / 613.1f).
        var cleric = new Creature("Cleric Stub", "1W", 1, 1);
        cleric.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(cleric);
        cleric.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Cleric Stub", "{T}: You gain 1 life.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), cleric);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's gain-life ability");
        var gainAbility = granted[0];
        gainAbility.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        gainAbility.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the re-homed {T} cost taps the BEARER");
        gainAbility.TargetRequests.Should().BeEmpty(
            "\"you gain N life\" targets nothing — the controller's life is fixed");

        // Activating it gains life for the BEARER-CONTROLLER.
        var lifeBefore = alice.LifeTotal;
        foreach (var effect in gainAbility.Effects) effect.Execute();
        alice.LifeTotal.Should().Be(lifeBefore + 1,
            "the re-homed gain-life ability gains life for the bearer's controller");
    }

    [Fact]
    public void Grant_NonMana_GainNLife_RehomesMultiLifeGainToBearerController()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var altar = new Creature("Altar Stub", "2W", 0, 4);
        altar.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(altar);
        altar.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Altar Stub", "{2}, {T}: You gain 3 life.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), altar);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's gain-3-life ability");
        var gainAbility = granted[0];
        gainAbility.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("2"));

        var lifeBefore = alice.LifeTotal;
        foreach (var effect in gainAbility.Effects) effect.Execute();
        alice.LifeTotal.Should().Be(lifeBefore + 3,
            "the re-homed \"you gain 3 life\" ability gains 3 for the bearer's controller");
    }

    [Fact]
    public void Grant_NonMana_ScrySelf_RehomesScryToBearerController()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature whose only ability is a tap-cost scry:
        // "{T}: Scry 2." (a common self-source library-smoothing shape — a
        // {T}: Scry payoff). Re-homing is sound: scry references the
        // BEARER-CONTROLLER's own library (Fx.Scry), never the exiled card —
        // there is no "this creature" / source reference at all, so it is as
        // sound a re-home as draw / gain-life (CR 701.20 / 613.1f). The agent
        // decision is read off the live ResolutionContext (PR #2696's
        // declarative scry_self verb), so no source-card identity is captured.
        var seer = new Creature("Seer Stub", "1U", 1, 1);
        seer.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(seer);
        seer.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Seer Stub", "{T}: Scry 2.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), seer);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's scry ability");
        var scryAbility = granted[0];
        scryAbility.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        scryAbility.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the re-homed {T} cost taps the BEARER");
        scryAbility.TargetRequests.Should().BeEmpty(
            "\"scry N\" targets nothing — the controller looks at their own library");

        // Set up Alice's library top: A (top), B, C (bottom). The scry-2 with
        // the no-agent default (ResolutionContext.Legacy → all-to-bottom)
        // sends the top 2 (A, B) to the bottom; remaining order is C, A, B.
        var cardA = new Land("Scry A") { Owner = alice, Zone = ZoneType.Library };
        var cardB = new Land("Scry B") { Owner = alice, Zone = ZoneType.Library };
        var cardC = new Land("Scry C") { Owner = alice, Zone = ZoneType.Library };
        alice.Zones.Library.AddCard(cardA);
        alice.Zones.Library.AddCard(cardB);
        alice.Zones.Library.AddCard(cardC);

        foreach (var effect in scryAbility.Effects) effect.Execute();

        var library = alice.Zones.Library.GetCards().ToList();
        library[0].Should().BeSameAs(cardC,
            "the re-homed scry sent the top two cards to the bottom for the bearer's controller");
        library[1].Should().BeSameAs(cardA);
        library[2].Should().BeSameAs(cardB);
    }

    [Fact]
    public void Grant_NonMana_SurveilSelf_RehomesSurveilToBearerController()
    {
        // agatha-oracle-shape-mill-or-surveil-tap-cost: the
        // OracleActivatedAbilityBinder now reconstructs the self-surveil shape
        // "{cost}: Surveil N." (Sinister Starfish "{T}: Surveil 1."). Re-homing is
        // sound: surveil references the BEARER-CONTROLLER's own library (CR 701.42
        // / 613.1f), never the exiled card. With no agent registered the default
        // is all-peeked-to-graveyard (matches CardDefRuntime.BuildSurveilSelfEffect).
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature: "{T}: Surveil 1."
        var starfish = new Creature("Starfish Stub", "1B", 0, 3);
        starfish.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(starfish);
        starfish.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // A card on top of Alice's library for the granted surveil to look at.
        var topCard = new Card("Top Card", "");
        topCard.SetOwner(alice);
        alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Starfish Stub", "{T}: Surveil 1.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), starfish);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's surveil ability");
        var surveilAbility = granted[0];
        surveilAbility.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        surveilAbility.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the re-homed {T} cost taps the BEARER");
        surveilAbility.TargetRequests.Should().BeEmpty(
            "\"surveil N\" targets nothing — the controller's own library");

        // Activating it surveils the BEARER-CONTROLLER's library. With no agent
        // the default sends the peeked card to the graveyard.
        var gyBefore = alice.Zones.Graveyard.GetCards().Count();
        foreach (var effect in surveilAbility.Effects) effect.Execute();
        alice.Zones.Graveyard.GetCards().Should().Contain(topCard,
            "the no-agent surveil default puts the peeked card into the controller's graveyard");
        alice.Zones.Graveyard.GetCards().Count().Should().Be(gyBefore + 1,
            "exactly one card was surveiled to the graveyard");
    }

    [Fact]
    public void Grant_NonMana_MillSelf_RehomesMillToBearerController()
    {
        // agatha-oracle-shape-mill-or-surveil-tap-cost: the
        // OracleActivatedAbilityBinder now reconstructs the self-mill shape
        // "{cost}: Mill N." (Excavated Wall "{1}, {T}: Mill a card."). Re-homing is
        // sound: mill references the BEARER-CONTROLLER's own library (CR 701.13 /
        // 613.1f), never the exiled card — no agent decision needed.
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature: "{1}, {T}: Mill a card."
        var wall = new Creature("Wall Stub", "1", 0, 4);
        wall.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(wall);
        wall.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var topCard = new Card("Top Card", "");
        topCard.SetOwner(alice);
        alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Wall Stub", "{1}, {T}: Mill a card.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), wall);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's mill ability");
        var millAbility = granted[0];
        millAbility.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        millAbility.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("1"));
        millAbility.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the re-homed {T} cost taps the BEARER");
        millAbility.TargetRequests.Should().BeEmpty(
            "\"mill N\" targets nothing — the controller's own library");

        // Activating it mills the BEARER-CONTROLLER's library.
        foreach (var effect in millAbility.Effects) effect.Execute();
        alice.Zones.Graveyard.GetCards().Should().Contain(topCard,
            "the re-homed mill puts the top of the controller's library into their graveyard");
    }

    [Fact]
    public void Grant_NonMana_TargetPlayerMill_RehomesMillToChosenPlayer()
    {
        // agatha-oracle-shape-mill-or-surveil-tap-cost: the
        // OracleActivatedAbilityBinder now reconstructs the TARGETED-player mill
        // shape "{cost}: Target player mills N." The CHOSEN player mills — re-homing
        // is sound because mill references the chosen player's OWN library (Fx.Mill
        // on ChosenTargets, CR 701.13 / 613.1f), never the exiled card. The BEARER
        // is only the source / cost-payer.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var grinder = new Creature("Grinder Stub", "1U", 1, 1);
        grinder.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(grinder);
        grinder.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // Two cards on top of BOB's library — the chosen player mills from their
        // OWN library, not the controller's.
        var bobsTop = new Card("Bob's Top", "");
        bobsTop.SetOwner(bob);
        bob.Zones.Library.AddCard(bobsTop);
        bobsTop.SetZone(ZoneType.Library);
        var bobsSecond = new Card("Bob's Second", "");
        bobsSecond.SetOwner(bob);
        bob.Zones.Library.AddCard(bobsSecond);
        bobsSecond.SetZone(ZoneType.Library);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Grinder Stub", "{T}: Target player mills two cards.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), grinder);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's target-player-mill ability");
        var millAbility = granted[0];
        millAbility.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        millAbility.TargetRequests.Should().ContainSingle(
            t => t.Description.Contains("target player"),
            "\"target player mills N\" requests a single target player");

        // Choosing BOB and resolving mills BOB's library, not Alice's.
        millAbility.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        var aliceGyBefore = alice.Zones.Graveyard.GetCards().Count();
        foreach (var effect in millAbility.Effects) effect.Execute();
        bob.Zones.Graveyard.GetCards().Should().Contain(new[] { bobsTop, bobsSecond },
            "the re-homed target-player mill mills the CHOSEN player's own library");
        alice.Zones.Graveyard.GetCards().Count().Should().Be(aliceGyBefore,
            "the controller's library is untouched — only the chosen target player mills");
    }

    [Fact]
    public void Grant_NonMana_Fight_RehomesFightToBearerAndChosenTarget()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature whose only ability fights another creature:
        // "{2}{G}, {T}: This creature fights target creature." (a self-source
        // fight payoff — a common bespoke shape on green creatures). Re-homing is
        // sound: the SOURCE of the fight is the BEARER (it deals + takes the fight
        // damage), never the exiled card; only an open "target creature" filter is
        // reconstructed (CR 701.12 / 613.1f).
        var fighter = new Creature("Fighter Stub", "2G", 3, 3);
        fighter.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(fighter);
        fighter.SetZone(ZoneType.Graveyard);

        // SeatedBearer is a base-2/2 Counter Bear with a +1/+1 counter ⇒ 3/3.
        var bearer = SeatedBearer(alice, effects, zones);

        // An opposing creature for the bearer to fight.
        var enemy = new Creature("Enemy Bear", "1G", 2, 2);
        enemy.SetOwner(bob);
        enemy.ChangeController(bob);
        bob.Zones.Library.AddCard(enemy);
        zones.MoveCard(enemy, ZoneType.Library, ZoneType.Battlefield, bob);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Fighter Stub", "{2}{G}, {T}: This creature fights target creature.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), fighter);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's fight ability");
        var fight = granted[0];
        fight.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        fight.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("G"));
        fight.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the re-homed {T} cost taps the BEARER");
        fight.TargetRequests.Should().ContainSingle(t => t.Description.Contains("target creature"));

        // Resolving the fight: the BEARER (3/3) and the chosen enemy (2/2) each
        // deal their power to the other (CR 701.12a). The exiled imprinted card is
        // untouched.
        fight.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { enemy } });
        foreach (var effect in fight.Effects) effect.Execute();

        enemy.Damage.Should().Be(3,
            "the BEARER deals its power (3) to the chosen creature in the fight");
        bearer.Damage.Should().Be(2,
            "the chosen creature deals its power (2) back to the BEARER (CR 701.12a)");
        fighter.Damage.Should().Be(0,
            "the exiled imprinted card never participates in the fight");
    }

    [Fact]
    public void Grant_NonMana_TapTarget_RehomesTapToBearerAndChosenCreature()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature whose only ability taps another creature:
        // "{W}, {T}: Tap target creature." (Master Decoy / Goldmeadow Harrier).
        // Re-homing is sound: the BEARER is only the source / cost-payer (its own
        // {T} cost taps it); the effect taps the CHOSEN target creature via
        // Fx.Tap (CR 701.21a), never the exiled imprinted card — the verb has no
        // "this creature" / source reference at all, so re-homing is a clean
        // controller-scoped tap of a chosen permanent.
        var tapper = new Creature("Decoy Stub", "1W", 1, 2);
        tapper.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(tapper);
        tapper.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // An opposing UNTAPPED creature for the bearer to tap.
        var enemy = new Creature("Enemy Bear", "1G", 2, 2);
        enemy.SetOwner(bob);
        enemy.ChangeController(bob);
        bob.Zones.Library.AddCard(enemy);
        zones.MoveCard(enemy, ZoneType.Library, ZoneType.Battlefield, bob);
        enemy.IsTapped.Should().BeFalse("the chosen creature starts untapped");

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Decoy Stub", "{W}, {T}: Tap target creature.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), tapper);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's tap-target ability");
        var tap = granted[0];
        tap.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card");
        tap.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("W"));
        tap.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the re-homed {T} cost taps the BEARER");
        tap.TargetRequests.Should().ContainSingle(t => t.Description.Contains("target creature"));

        // Resolving with the enemy chosen taps the ENEMY, not the bearer and not
        // the exiled card.
        tap.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { enemy } });
        foreach (var effect in tap.Effects) effect.Execute();

        enemy.IsTapped.Should().BeTrue(
            "the re-homed tap-target ability taps the CHOSEN creature (CR 701.21a)");
        bearer.IsTapped.Should().BeFalse(
            "the bearer (mere source) is not tapped by the effect — only its {T} cost taps it");
        tapper.IsTapped.Should().BeFalse(
            "the exiled imprinted card is never touched by the granted ability");
    }

    [Fact]
    public void Grant_NonMana_UnparseableBespokeAbility_GrantsNothing()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A creature whose only ability is a bespoke, NON-reconstructable one:
        // a tutor with an unmodellable cost shape and an effect the binder
        // doesn't know. Plus an "Activate only" rider on a pump (must be skipped
        // too — sound, not broken).
        var bespoke = new Creature("Bespoke Stub", "2GG", 3, 3);
        bespoke.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bespoke);
        bespoke.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Bespoke Stub",
                "{2}, {T}: Search your library for a creature card, reveal it, "
                + "put it into your hand, then shuffle.\n"
                + "{G}: This creature gets +2/+2 until end of turn. Activate only as a sorcery.\n"
                + "{E}{E}: This creature deals 2 damage to any target.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), bespoke);

        GrantedActivated(bearer).Should().BeEmpty(
            "no broken ability is emitted for bespoke / ridered / unmodellable-cost shapes; "
            + "the binder skips what it cannot soundly rebuild");
    }

    // -----------------------------------------------------------------------
    // CR 702.49 — imprint LINKAGE (ExiledWith back-link) + leave-the-battlefield
    // DETACH. An imprinted card is linked to the Cauldron instance it was exiled
    // with so a client can render it UNDER the Cauldron; when the Cauldron leaves
    // the battlefield the card STAYS in exile (does NOT return) but loses the
    // link (plain exile). A fresh Cauldron never grants the first's exiles.
    // -----------------------------------------------------------------------

    [Fact]
    public void Imprint_LinksExiledCreatureToThatCauldronInstance()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var manaDork = new Creature("Llanowar Stub", "G", 1, 1);
        manaDork.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(manaDork);
        manaDork.SetZone(ZoneType.Graveyard);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Llanowar Stub", "{T}: Add {G}.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        manaDork.ExiledWith.Should().BeNull("not yet exiled with anything");

        Resolve(TapAbility(cauldron), manaDork);

        manaDork.ExiledWith.Should().Be(cauldron.InstanceId,
            "the imprinted creature is linked to THIS Cauldron instance so a client " +
            "renders it under it (CR 702.49)");
        cauldron.ImprintedCards.Should().Contain(manaDork);
    }

    [Fact]
    public void CauldronLeavesBattlefield_ImprintsDetach_StayInExileWithNullLink()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var manaDork = new Creature("Llanowar Stub", "G", 1, 1);
        manaDork.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(manaDork);
        manaDork.SetZone(ZoneType.Graveyard);

        // A bearer with a +1/+1 counter so the grant is observably active.
        var bearer = new Creature("Counter Bear", "1G", 2, 2);
        bearer.SetOwner(alice);
        bearer.ChangeController(alice);
        bearer.Counters.Add(CounterType.PlusOnePlusOne, 1);
        alice.Zones.Library.AddCard(bearer);
        zones.MoveCard(bearer, ZoneType.Library, ZoneType.Battlefield, alice);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Llanowar Stub", "{T}: Add {G}.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);
        Resolve(TapAbility(cauldron), manaDork);

        // Sanity: linked + granted while the Cauldron is on the battlefield.
        manaDork.ExiledWith.Should().Be(cauldron.InstanceId);
        manaDork.Zone.Should().Be(ZoneType.Exile);
        bearer.Abilities.OfType<IManaAbility>().Where(ProducesGreen).Should().NotBeEmpty();

        // Cauldron leaves play.
        zones.MoveCard(cauldron, ZoneType.Battlefield, ZoneType.Graveyard, alice);
        effects.Prune();

        // The imprinted card STAYS in exile — it does NOT return.
        manaDork.Zone.Should().Be(ZoneType.Exile,
            "the imprinted card stays in exile when the Cauldron leaves — it does not return");
        alice.Zones.Exile.GetCards().Should().Contain(manaDork);
        // …but it is now PLAIN exile — the link is cleared.
        manaDork.ExiledWith.Should().BeNull(
            "the imprint back-link detaches when the Cauldron leaves the battlefield (CR 702.49)");
        // …and the Cauldron's own imprint list is cleared.
        cauldron.ImprintedCards.Should().BeEmpty("the Cauldron's imprint state is reset on leave");
        // …and the grant is gone.
        bearer.Abilities.OfType<IManaAbility>().Where(ProducesGreen).Should().BeEmpty(
            "with the Cauldron gone the granted ability is lost (CR 613.6e)");
    }

    [Fact]
    public void SecondCauldron_DoesNotGrantAbilitiesFromAPriorCauldronsExiles()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);
        var oracle = OracleStub(("Llanowar Stub", "{T}: Add {G}."));

        var manaDork = new Creature("Llanowar Stub", "G", 1, 1);
        manaDork.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(manaDork);
        manaDork.SetZone(ZoneType.Graveyard);

        var bearer = new Creature("Counter Bear", "1G", 2, 2);
        bearer.SetOwner(alice);
        bearer.ChangeController(alice);
        bearer.Counters.Add(CounterType.PlusOnePlusOne, 1);
        alice.Zones.Library.AddCard(bearer);
        zones.MoveCard(bearer, ZoneType.Library, ZoneType.Battlefield, alice);

        // First Cauldron: imprint the dork, then leave the battlefield.
        var first = GrantingCauldron(alice, effects, bus, oracle);
        alice.Zones.Library.AddCard(first);
        zones.MoveCard(first, ZoneType.Library, ZoneType.Battlefield, alice);
        Resolve(TapAbility(first), manaDork);
        bearer.Abilities.OfType<IManaAbility>().Where(ProducesGreen).Should().NotBeEmpty(
            "first Cauldron grants its own exile's ability");

        zones.MoveCard(first, ZoneType.Battlefield, ZoneType.Graveyard, alice);
        effects.Prune();
        manaDork.ExiledWith.Should().BeNull("detached when the first Cauldron left");

        // A NEW Cauldron enters — a different instance with its own empty imprint
        // list. It must NOT grant the dork's ability (the dork was exiled with
        // the FIRST Cauldron, not this one).
        var second = GrantingCauldron(alice, effects, bus, oracle);
        alice.Zones.Library.AddCard(second);
        zones.MoveCard(second, ZoneType.Library, ZoneType.Battlefield, alice);

        second.ImprintedCards.Should().BeEmpty(
            "a fresh Cauldron has its own empty imprint list");
        manaDork.ExiledWith.Should().NotBe(second.InstanceId,
            "the dork is not linked to the new Cauldron");
        bearer.Abilities.OfType<IManaAbility>().Where(ProducesGreen).Should().BeEmpty(
            "a new Cauldron does NOT grant abilities from a previous Cauldron's exiles");
    }

    // -----------------------------------------------------------------------
    // STAGE 2/3 — PRIMARY grant mechanism: RebindTo the imprinted creature's
    // REAL activated abilities. Where the existing OracleStub tests imprint a
    // synthetic creature with no engine-built abilities (so the oracle-rebuild
    // FALLBACK runs), these tests imprint a creature built through the real
    // data-driven CardDef path, so it carries actual RebindSafe ActivatedAbility
    // objects. The grant re-homes THOSE via RebindTo — covering whatever the
    // card actually has, not just oracle-parseable shapes — and never re-parses.
    // -----------------------------------------------------------------------

    /// <summary>Build a real Creature carrying a data-driven self-pump activated
    /// ability ("{R}: This creature gets +2/+0 until end of turn"). The ability
    /// is RebindSafe because pump_self reads ResolutionContext.Source.</summary>
    private static Creature DataDrivenFirebreather(Player owner)
    {
        var abilityDef = new Majik.Core.CardData.Definitions.ActivatedAbilityDefinition
        {
            Costs = { new Majik.Core.CardData.Definitions.ManaCostDef { Amount = "{R}" } },
            Effects = { new Majik.Core.CardData.Definitions.PumpSelfEffectDef { Power = 2, Toughness = 0 } },
        };
        var def = Majik.Core.CardData.Definitions.CardDef
            .Creature("Real Firebreather", "1R", 2, 2)
            .WithAbility(abilityDef.ToCardDefAbility())
            .Build();
        return (Creature)Majik.Core.CardData.Definitions.CardDefRuntime.Build(def, owner);
    }

    [Fact]
    public async Task Grant_RebindsRealActivatedAbility_OfDataDrivenImprint_ToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL data-driven creature (carries an actual RebindSafe ability).
        var firebreather = DataDrivenFirebreather(alice);
        alice.Zones.Graveyard.AddCard(firebreather);
        firebreather.SetZone(ZoneType.Graveyard);
        // It has a real activated ability sourced on ITSELF before the grant.
        firebreather.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility).RebindSafe.Should().BeTrue();

        var bearer = SeatedBearer(alice, effects, zones);

        // No oracle lookup needed — the grant uses the REAL ability, not text.
        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), firebreather);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the bearer gains the imprinted creature's REAL activated ability via RebindTo");
        var pump = granted[0];
        pump.Source.Should().BeSameAs(bearer, "re-homed to the BEARER (CR 707.2)");
        pump.RebindSafe.Should().BeTrue("RebindTo preserves the re-source-safe provenance");

        // Activating it pumps the BEARER (+2/+0), not the exiled card. Resolve
        // through the ability path so ResolutionContext.Source = the rebound
        // ability's own source (the bearer) — the re-source seam that makes the
        // migrated pump_self effect act on the bearer.
        var powerBefore = bearer.GetPower();
        await pump.ResolveAsync(agent: null, game: null);
        bearer.GetPower().Should().Be(powerBefore + 2,
            "the re-homed real ability pumps the BEARER (ResolutionContext.Source = bearer)");
        firebreather.GetPower().Should().Be(2,
            "the exiled imprinted card is untouched");
    }

    // -----------------------------------------------------------------------
    // agatha-bespoke-resourcecontext-source-migration — a real BESPOKE
    // [CardName]-factory creature (Lotleth Troll) whose activated abilities
    // were migrated to read ResolutionContext.Source + marked RebindSafe now
    // flows through the PRIMARY RebindTo path. This adds coverage the
    // oracle-rebuild fallback CANNOT: Lotleth Troll's "Discard a creature
    // card: Put a +1/+1 counter on this creature" carries a bespoke
    // DiscardACreatureCardCost the binder cannot reconstruct from text, so the
    // RebindTo of the REAL ability (reusing the real cost) is the only sound
    // way to re-home it.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_LotlethTroll_ToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its two
        // activated abilities (discard-pump + {B} regenerate) are now RebindSafe.
        var troll = LotlethTrollFactory.Create(alice);
        troll.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .Should().OnlyContain(a => a.RebindSafe,
                "the migrated Lotleth Troll abilities read ResolutionContext.Source and are RebindSafe");
        alice.Zones.Graveyard.AddCard(troll);
        troll.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // No oracle lookup needed — the grant uses the REAL abilities, not text.
        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), troll);

        var granted = GrantedActivated(bearer);
        granted.Should().HaveCount(2,
            "BOTH of Lotleth Troll's real activated abilities are re-homed via RebindTo");
        granted.Should().OnlyContain(a => ReferenceEquals(a.Source, bearer),
            "every re-homed ability is sourced on the BEARER (CR 707.2)");

        // The discard-pump ability — its bespoke DiscardACreatureCardCost is
        // reused verbatim by RebindTo (the oracle binder cannot rebuild it).
        var pump = granted.Single(a =>
            a.Costs.OfType<Majik.Core.Costs.DiscardACreatureCardCost>().Any());
        pump.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");

        // Resolving the re-homed discard-pump through the ability path puts the
        // +1/+1 counter on the BEARER (ResolutionContext.Source = bearer), never
        // the exiled Troll.
        var bearerCountersBefore = bearer.Counters.Count(CounterType.PlusOnePlusOne);
        var trollCountersBefore = troll.Counters.Count(CounterType.PlusOnePlusOne);
        await pump.ResolveAsync(agent: null, game: null);
        bearer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(bearerCountersBefore + 1,
            "the re-homed discard-pump puts the counter on the BEARER");
        troll.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(trollCountersBefore,
            "the exiled imprinted Troll never receives the counter");

        // The regenerate ability — re-homed shield protects the BEARER.
        var regen = granted.Single(a =>
            a.Costs.OfType<Majik.Core.Costs.ManaCostCost>().Any());
        var bearerShieldsBefore = bearer.RegenerationShieldCount;
        await regen.ResolveAsync(agent: null, game: null);
        bearer.RegenerationShieldCount.Should().Be(bearerShieldsBefore + 1,
            "the re-homed regenerate shields the BEARER (CR 701.18)");
        troll.RegenerationShieldCount.Should().Be(0,
            "the exiled imprinted Troll never receives the shield");
    }

    // -----------------------------------------------------------------------
    // priest-of-fell-rites-exile-from-gy-reanimate-rebind — the bespoke
    // [CardName]-factory creature whose reanimation activated ability ("{T},
    // Pay 3 life, Sacrifice this creature: Return target creature card from your
    // graveyard to the battlefield") was migrated to read
    // ResolutionContext.Source + marked RebindSafe. Its Tap + Sacrifice costs
    // re-home via AdditionalCost.RebindSource (Stage 1), so the grant re-homes
    // the REAL ability to a BEARER: the bearer taps + sacrifices ITSELF and
    // reanimates from the BEARER's controller's graveyard.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_PriestOfFellRites_ToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its
        // reanimation activated ability is now RebindSafe.
        var priest = PriestOfFellRitesFactory.Create(alice);
        priest.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<Majik.Core.Costs.AdditionalCost>()
                .Any(c => c.CostType == Majik.Core.Costs.AdditionalCostType.Sacrifice))
            .RebindSafe.Should().BeTrue(
                "the migrated Priest reanimation reads ResolutionContext.Source and is RebindSafe");
        alice.Zones.Graveyard.AddCard(priest);
        priest.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), priest);

        // The bearer gains the Priest's REAL reanimation ability, re-homed.
        var reanimate = GrantedActivated(bearer).Single(a =>
            a.Costs.OfType<Majik.Core.Costs.AdditionalCost>()
                .Any(c => c.CostType == Majik.Core.Costs.AdditionalCostType.Sacrifice));
        reanimate.Source.Should().BeSameAs(bearer, "re-homed to the BEARER (CR 707.2)");
        reanimate.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");

        // STAGE 1 — the Tap + Sacrifice costs now capture the BEARER, not the
        // exiled Priest.
        reanimate.Costs.OfType<Majik.Core.Costs.AdditionalCost>()
            .Single(c => c.CostType == Majik.Core.Costs.AdditionalCostType.Sacrifice)
            .Description.Should().Contain(bearer.Name,
                "the sacrifice cost re-homes to the bearer (AdditionalCost.RebindSource)");

        // A creature card in the BEARER's controller's graveyard reanimates when
        // the re-homed ability resolves (ResolutionContext.Source = bearer →
        // controller = bearer's controller).
        var zombie = new Creature("Walking Corpse", "1B", 2, 2);
        zombie.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(zombie);
        zombie.SetZone(ZoneType.Graveyard);

        reanimate.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { zombie } });
        await reanimate.ResolveAsync(agent: null, game: null);

        zombie.Zone.Should().Be(ZoneType.Battlefield,
            "the re-homed reanimation returns the chosen creature card to the bearer's controller's battlefield");
        alice.Zones.Battlefield.GetCards().Should().Contain(zombie);
    }

    // -----------------------------------------------------------------------
    // agatha-rebind-steel-hellkite-variable-x-sweep — the HARD bespoke case:
    // Steel Hellkite's "{X}: Destroy each nonland permanent with mv X whose
    // controller was dealt combat damage by THIS CREATURE this turn." The
    // "damaged by this creature this turn" linkage was migrated to key the
    // combat-victim tracker BY THE DAMAGE-SOURCE permanent + read the sweep's
    // live source off ResolutionContext.Source, so re-homing the REAL ability
    // to a BEARER destroys permanents whose controller the BEARER damaged — not
    // the exiled Steel Hellkite's stale linkage. The X already threads via
    // ChosenX (GAP 2); this closes the residual combat-damage re-source.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsSteelHellkiteSweep_ToBearer_UsesBearersCombatDamageLinkage()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL Steel Hellkite in the graveyard, built with the LIVE bus so its
        // per-source combat-victim tracker sees the bearer's combat damage too.
        var hellkite = SteelHellkiteFactory.Create(alice, xValueProvider: null, eventBus: bus);
        alice.Zones.Graveyard.AddCard(hellkite);
        hellkite.SetZone(ZoneType.Graveyard);

        // Its {X} sweep is RebindSafe (reads source + X + victims off the ctx).
        hellkite.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<Majik.Core.Costs.ManaCostCost>()
                .Any(m => m.Description == "X"))
            .RebindSafe.Should().BeTrue();

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        // Imprint Steel Hellkite — the bearer gains its real {X} sweep, re-homed.
        Resolve(TapAbility(cauldron), hellkite);

        var sweep = GrantedActivated(bearer)
            .Single(a => a.Costs.OfType<Majik.Core.Costs.ManaCostCost>()
                .Any(m => m.Description == "X"));
        sweep.Source.Should().BeSameAs(bearer, "the sweep is re-homed to the BEARER (CR 707.2)");

        // Bob controls an mv-2 nonland permanent. The BEARER (not the exiled
        // Steel Hellkite) deals combat damage to Bob this turn.
        var bobBear = new Creature("Grizzly Bears", "1G", 2, 2);
        bobBear.SetOwner(bob);
        bobBear.SetController(bob);
        bobBear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobBear);

        bus.Publish(new Majik.Core.Domain.DomainEvents.CombatDamageDealtEvent(
            bearer, bob, amount: 3));

        // Resolve the re-homed sweep for X = 2 through the ability path so
        // ResolutionContext.Source = the bearer and the victim set is the
        // BEARER's. A live game over [alice, bob] supplies the sweep scope.
        sweep.SetChosenX(2);
        var game = new Majik.Core.Game.GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(bus));
        await sweep.ResolveAsync(agent: null, game: game);

        bob.Zones.Graveyard.GetCards().Should().Contain(bobBear,
            "the re-homed sweep destroys the mv-2 permanent whose controller the BEARER damaged this turn");
    }

    [Fact]
    public async Task Grant_SteelHellkiteSweep_DoesNotUseExiledCardsCombatLinkage()
    {
        // Negative half: combat damage dealt by the EXILED Steel Hellkite (e.g.
        // a stale linkage from before imprint) must NOT drive the BEARER's
        // re-homed sweep. The bearer dealt no combat damage, so nothing dies.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var hellkite = SteelHellkiteFactory.Create(alice, xValueProvider: null, eventBus: bus);
        alice.Zones.Graveyard.AddCard(hellkite);
        hellkite.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);
        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);
        Resolve(TapAbility(cauldron), hellkite);

        var sweep = GrantedActivated(bearer)
            .Single(a => a.Costs.OfType<Majik.Core.Costs.ManaCostCost>()
                .Any(m => m.Description == "X"));

        var bobBear = new Creature("Grizzly Bears", "1G", 2, 2);
        bobBear.SetOwner(bob);
        bobBear.SetController(bob);
        bobBear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobBear);

        // The EXILED Steel Hellkite "deals" combat damage (stale linkage) — must
        // populate Steel Hellkite's source slot, NOT the bearer's.
        bus.Publish(new Majik.Core.Domain.DomainEvents.CombatDamageDealtEvent(
            hellkite, bob, amount: 3));

        sweep.SetChosenX(2);
        var game = new Majik.Core.Game.GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(bus));
        await sweep.ResolveAsync(agent: null, game: game);

        bob.Zones.Battlefield.GetCards().Should().Contain(bobBear,
            "the BEARER dealt no combat damage; the exiled Steel Hellkite's linkage does not drive the re-homed sweep");
    }

    [Fact]
    public async Task Grant_NonMana_SameNameSpillover_RehomesPingAndSweepToBearer()
    {
        // izzet-staticaster-pinger-spillover-oracle-shape: the
        // OracleActivatedAbilityBinder now reconstructs Izzet Staticaster's
        // spillover oracle shape "{cost}: This creature deals N damage to target
        // creature and each other creature with the same name as that creature."
        // (CR 109.2 / 707.2). Re-homing is sound: the BEARER is only the source /
        // cost-payer; the damage lands on the chosen creature + each OTHER
        // battlefield creature whose EFFECTIVE name matches, read off rc.Game —
        // never the exiled imprinted card.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature: a 0/3 with Izzet Staticaster's printed line.
        var staticaster = new Creature("Staticaster Stub", "1UR", 0, 3);
        staticaster.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(staticaster);
        staticaster.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Staticaster Stub",
                "{T}: This creature deals 1 damage to target creature and each "
                + "other creature with the same name as that creature.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), staticaster);

        var ping = GrantedActivated(bearer).Single(a =>
            a.TargetRequests.Any(t => t.Description.Contains("target creature")));
        ping.Source.Should().BeSameAs(bearer, "the spillover ping is re-homed to the BEARER");

        // Battlefield: the chosen target + a name-twin (both hit) + an unrelated
        // creature (not hit). Twins split across both players' battlefields.
        Creature OnBf(string name, Player p)
        {
            var c = new Creature(name, "1G", 2, 2);
            c.SetOwner(p);
            c.SetController(p);
            c.SetZone(ZoneType.Battlefield);
            p.Zones.Battlefield.AddCard(c);
            return c;
        }
        var targetBear = OnBf("Grizzly Bears", bob);
        var twinBear = OnBf("Grizzly Bears", alice);
        var giant = OnBf("Hill Giant", bob);

        ping.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { targetBear } });
        var game = new Majik.Core.Game.GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(bus));
        await ping.ResolveAsync(agent: null, game: game);

        targetBear.Damage.Should().Be(1, "the chosen target takes the ping");
        twinBear.Damage.Should().Be(1, "each OTHER creature with the same name takes the ping");
        giant.Damage.Should().Be(0, "an unrelated creature is untouched");
        staticaster.Damage.Should().Be(0, "the exiled imprinted card is never affected");
    }

    [Fact]
    public async Task Grant_RebindsRealAbility_ResolvesThroughAbilityPath_AffectingBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var firebreather = DataDrivenFirebreather(alice);
        alice.Zones.Graveyard.AddCard(firebreather);
        firebreather.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), firebreather);

        var pump = GrantedActivated(bearer).Single();
        var powerBefore = bearer.GetPower();

        // Resolve through the real ability path: ResolutionContext.Source is the
        // rebound ability's own Source = the bearer, so the migrated pump_self
        // effect pumps the bearer.
        await pump.ResolveAsync(agent: null, game: null);

        bearer.GetPower().Should().Be(powerBefore + 2,
            "resolving the re-homed ability through ResolveAsync pumps the BEARER");
    }

    // -----------------------------------------------------------------------
    // agatha-bespoke-factory-resolutioncontext-source-migration — a second
    // batch of bespoke [CardName]-factory creatures whose {cost}: Regenerate
    // <self> activated abilities were migrated to read ResolutionContext.Source
    // + marked RebindSafe. Skithiryx is the case the oracle-rebuild fallback
    // CANNOT cover: its printed regenerate names the creature ("Regenerate
    // Skithiryx"), not "this creature", so OracleActivatedAbilityBinder's
    // RegenerateSelfRegex never matches — only the PRIMARY RebindTo path of the
    // REAL ability re-homes it. Mortivore / River Boa say "Regenerate this
    // creature" (the fallback could rebuild those), but migrating them lets the
    // RebindTo path re-home the REAL ability instead of a text reconstruction.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_Skithiryx_RegenerateToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its
        // {cost}: Regenerate <self> activated ability is now RebindSafe (reads
        // ResolutionContext.Source). The oracle text says "Regenerate
        // Skithiryx" (BY NAME) so the oracle-rebuild fallback cannot reconstruct
        // it — the RebindTo of the real ability is the only sound re-home.
        var skithiryx = SkithiryxTheBlightDragonFactory.Create(alice);
        var realAbilities = skithiryx.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        realAbilities.Should().HaveCount(2,
            "current printing: {B}: gains haste until EOT + {B}{B}: regenerate self");
        realAbilities.Should().OnlyContain(a => a.RebindSafe,
            "both migrated Skithiryx abilities read ResolutionContext.Source and are RebindSafe");
        realAbilities.Should().Contain(a => a.Effects.Any(e =>
            e.Description.Contains("regenerate", StringComparison.OrdinalIgnoreCase)),
            "the by-name regenerate ability is the case the oracle-rebuild fallback cannot cover");
        alice.Zones.Graveyard.AddCard(skithiryx);
        skithiryx.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // OracleStub deliberately returns NOTHING for Skithiryx so the only way a
        // regenerate is granted is via RebindTo of the real ability — if the
        // grant still depended on the oracle fallback, nothing would be emitted
        // for a by-name regenerate and this test would fail.
        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), skithiryx);

        var granted = GrantedActivated(bearer);
        granted.Should().HaveCount(2,
            "both of Skithiryx's real abilities (gain-haste + regenerate) are re-homed via RebindTo");
        var regen = granted.Single(a => a.Effects.Any(e =>
            e.Description.Contains("regenerate", StringComparison.OrdinalIgnoreCase)));
        regen.Source.Should().BeSameAs(bearer,
            "the re-homed regenerate is sourced on the BEARER (CR 707.2)");
        regen.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");

        // Resolving the re-homed regenerate shields the BEARER (CR 701.18),
        // never the exiled Skithiryx.
        var bearerShieldsBefore = bearer.RegenerationShieldCount;
        await regen.ResolveAsync(agent: null, game: null);
        bearer.RegenerationShieldCount.Should().Be(bearerShieldsBefore + 1,
            "the re-homed regenerate shields the BEARER (ResolutionContext.Source = bearer)");
        skithiryx.RegenerationShieldCount.Should().Be(0,
            "the exiled imprinted Skithiryx never receives the shield");
    }

    [Fact]
    public async Task BespokeRegenerate_ResolvesOnOwnSourceWhenNotRebound()
    {
        // Sanity: the migrated effect still shields its OWN source on the normal
        // (un-rebound) resolution path — ResolutionContext.Source = the card.
        var alice = new Player("Alice", 20);
        var skithiryx = SkithiryxTheBlightDragonFactory.Create(alice);
        var regen = skithiryx.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility && a.Effects.Any(e =>
                e.Description.Contains("regenerate", StringComparison.OrdinalIgnoreCase)));

        skithiryx.RegenerationShieldCount.Should().Be(0);
        await regen.ResolveAsync(agent: null, game: null);
        skithiryx.RegenerationShieldCount.Should().Be(1,
            "resolving the un-rebound regenerate shields its own source");
    }

    // -----------------------------------------------------------------------
    // agatha-bespoke-activated-ability-non-reconstructable-source-migration —
    // Krenko, Mob Boss is a bespoke [CardName]-factory creature whose sole
    // activated ability ("{T}: Create X 1/1 red Goblin creature tokens, where
    // X is the number of Goblins you control") is OUTSIDE the
    // OracleActivatedAbilityBinder reconstructable set (self-pump / pinger /
    // keyword-grant / counter / draw / gain-life / regenerate) — token
    // creation with a "Goblins you control" count is not a parseable shape.
    // The migration retargets the effect to read "you" off
    // ResolutionContext.Source's controller and marks the ability RebindSafe,
    // so Agatha's group-grant re-homes the REAL token-maker (and its {T} cost,
    // auto-re-homed by RebindTo Stage 1) onto a counter-bearing bearer via
    // ActivatedAbility.RebindTo (CR 707.2 / 613.1f) — counting the BEARER's
    // controller's Goblins and minting under them, never re-reading the exiled
    // Krenko.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_Krenko_TokenMakerToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its sole
        // non-mana activated ability (the {T} token-maker) is now RebindSafe
        // (reads ResolutionContext.Source's controller). Token creation is NOT
        // reconstructable from oracle text, so the RebindTo of the real ability
        // is the only sound re-home.
        var krenko = KrenkoMobBossFactory.Create(alice);
        var realAbilities = krenko.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        realAbilities.Should().ContainSingle(
            "Krenko has exactly one non-mana activated ability — the {T} token-maker");
        realAbilities.Should().OnlyContain(a => a.RebindSafe,
            "the migrated Krenko ability reads ResolutionContext.Source and is RebindSafe");
        alice.Zones.Graveyard.AddCard(krenko);
        krenko.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // OracleStub deliberately returns NOTHING for Krenko so the only way the
        // ability is granted is via RebindTo of the real ability — the
        // oracle-rebuild fallback cannot reconstruct token creation, so if the
        // grant still depended on it nothing would be emitted and this test
        // would fail.
        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), krenko);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "Krenko's real {T} token-maker is re-homed via RebindTo");
        var tokenMaker = granted[0];
        tokenMaker.Source.Should().BeSameAs(bearer,
            "the re-homed token-maker is sourced on the BEARER (CR 707.2)");
        tokenMaker.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");
        tokenMaker.Costs.OfType<Majik.Core.Costs.AdditionalCost>()
            .Should().ContainSingle()
            .Which.Description.Should().Contain("Tap",
                "the {T} cost is auto-re-homed to the bearer by RebindTo (Stage 1)");

        // Put a couple of Goblins onto the bearer's controller (Alice) so the
        // "X = Goblins you control" count is observable. The bearer itself
        // ("Counter Bear") is not a Goblin and Krenko is in exile, so neither is
        // counted — only the two real Goblins on Alice's battlefield.
        var goblinA = new Creature("Goblin A", "R", 1, 1, subtypes: new[] { CardSubtype.Goblin });
        var goblinB = new Creature("Goblin B", "R", 1, 1, subtypes: new[] { CardSubtype.Goblin });
        foreach (var g in new[] { goblinA, goblinB })
        {
            g.SetOwner(alice);
            g.ChangeController(alice);
            alice.Zones.Library.AddCard(g);
            zones.MoveCard(g, ZoneType.Library, ZoneType.Battlefield, alice);
        }

        var goblinsBefore = alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Goblin));
        goblinsBefore.Should().Be(2, "two real Goblins control by Alice before resolution");

        // Resolving the re-homed token-maker counts Goblins the BEARER'S
        // controller (Alice) controls and mints that many tokens under Alice —
        // ResolutionContext.Source = bearer => its controller = Alice.
        await tokenMaker.ResolveAsync(agent: null, game: null);

        var goblinsAfter = alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Goblin));
        goblinsAfter.Should().Be(goblinsBefore + 2,
            "the re-homed token-maker minted X=2 (the bearer's controller's Goblin count) under Alice");
    }

    [Fact]
    public async Task BespokeTokenMaker_ResolvesOnOwnSourceWhenNotRebound()
    {
        // Sanity: the migrated effect still reads "Goblins you control" off its
        // OWN source on the normal (un-rebound) resolution path —
        // ResolutionContext.Source = the card.
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var zones = new Majik.Core.Services.ZoneService(bus);

        var krenko = KrenkoMobBossFactory.Create(alice, zones);
        alice.Zones.Library.AddCard(krenko);
        zones.MoveCard(krenko, ZoneType.Library, ZoneType.Battlefield, alice);
        krenko.ClearSummoningSickness();

        var tokenMaker = krenko.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        // Krenko himself is the only Goblin Alice controls — X = 1.
        var before = alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Goblin));
        before.Should().Be(1, "Krenko alone is on the battlefield (counts himself)");

        await tokenMaker.ResolveAsync(agent: null, game: null);

        var after = alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Goblin));
        after.Should().Be(before + 1,
            "resolving the un-rebound token-maker mints X=1 (Krenko counts himself)");
    }

    // -----------------------------------------------------------------------
    // agatha-grant-imprinted-arbitrary-bespoke-closure-rehome — Griselbrand is
    // a bespoke [CardName]-factory creature whose sole activated ability ("Pay 7
    // life: Draw seven cards") is OUTSIDE the OracleActivatedAbilityBinder
    // reconstructable set: the "Pay 7 life" cost is explicitly REJECTED by the
    // binder's cost grammar (mana pips + {T} + "Sacrifice this creature" only),
    // so the oracle-rebuild fallback cannot reconstruct this clause at all. The
    // migration retargets the effect to draw for ResolutionContext.Source's
    // controller (rather than capturing `card`) and marks the ability
    // RebindSafe, so Agatha's group-grant re-homes the REAL ability (and its
    // PayLife cost, passed through unchanged by RebindTo Stage 1) onto a
    // counter-bearing bearer via ActivatedAbility.RebindTo (CR 707.2 / 613.1f) —
    // the BEARER's controller pays 7 life and draws 7, never the exiled
    // Griselbrand. This is the Skithiryx case (a real ability whose printed cost
    // is not reconstructable from oracle text).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_Griselbrand_DrawSevenToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its sole
        // non-mana activated ability (the "Pay 7 life: Draw seven cards") is now
        // RebindSafe (draws for ResolutionContext.Source's controller). The
        // "Pay 7 life" cost is NOT reconstructable from oracle text, so the
        // RebindTo of the real ability is the only sound re-home.
        var griselbrand = GriselbrandFactory.Create(alice);
        var realAbilities = griselbrand.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        realAbilities.Should().ContainSingle(
            "Griselbrand has exactly one non-mana activated ability — the draw-7");
        realAbilities.Should().OnlyContain(a => a.RebindSafe,
            "the migrated Griselbrand ability reads ResolutionContext.Source and is RebindSafe");
        alice.Zones.Graveyard.AddCard(griselbrand);
        griselbrand.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // OracleStub deliberately returns NOTHING for Griselbrand so the only way
        // the ability is granted is via RebindTo of the real ability — the
        // oracle-rebuild fallback cannot reconstruct the "Pay 7 life" cost, so if
        // the grant still depended on it nothing would be emitted and this test
        // would fail.
        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), griselbrand);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "Griselbrand's real Pay-7-life draw-7 is re-homed via RebindTo");
        var draw7 = granted[0];
        draw7.Source.Should().BeSameAs(bearer,
            "the re-homed draw-7 is sourced on the BEARER (CR 707.2)");
        draw7.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");
        draw7.Costs.OfType<Majik.Core.Costs.AdditionalCost>()
            .Should().ContainSingle(
                "the PayLife cost is passed through unchanged by RebindTo (Stage 1)");

        // Stock the bearer-controller's (Alice's) library so the seven draws are
        // observable.
        for (int i = 0; i < 10; i++)
        {
            var libCard = new Creature($"Lib {i}", "G", 1, 1);
            libCard.SetOwner(alice);
            alice.Zones.Library.AddCard(libCard);
            libCard.SetZone(ZoneType.Library);
        }

        var handBefore = alice.Zones.Hand.GetCards().Count();

        // Resolving the re-homed draw-7 draws SEVEN for the BEARER'S controller
        // (Alice) — ResolutionContext.Source = bearer => its controller = Alice.
        await draw7.ResolveAsync(agent: null, game: null);

        var handAfter = alice.Zones.Hand.GetCards().Count();
        handAfter.Should().Be(handBefore + 7,
            "the re-homed draw-7 drew seven cards for the bearer's controller (Alice)");
    }

    [Fact]
    public async Task BespokeDrawSeven_ResolvesOnOwnSourceWhenNotRebound()
    {
        // Sanity: the migrated effect still draws for its OWN source's controller
        // on the normal (un-rebound) resolution path — ResolutionContext.Source =
        // the card.
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var zones = new Majik.Core.Services.ZoneService(bus);

        var griselbrand = GriselbrandFactory.Create(alice);
        alice.Zones.Library.AddCard(griselbrand);
        zones.MoveCard(griselbrand, ZoneType.Library, ZoneType.Battlefield, alice);

        var draw7 = griselbrand.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        for (int i = 0; i < 10; i++)
        {
            var libCard = new Creature($"Lib {i}", "G", 1, 1);
            libCard.SetOwner(alice);
            alice.Zones.Library.AddCard(libCard);
            libCard.SetZone(ZoneType.Library);
        }

        var before = alice.Zones.Hand.GetCards().Count();
        await draw7.ResolveAsync(agent: null, game: null);
        var after = alice.Zones.Hand.GetCards().Count();
        after.Should().Be(before + 7,
            "resolving the un-rebound draw-7 draws seven for its own source's controller");
    }

    // -----------------------------------------------------------------------
    // agatha-bespoke-closure-resolutioncontext-source-rebind-next-shape —
    // Fauna Shaman is a bespoke [CardName]-factory creature whose sole
    // activated ability ("{G}, {T}, Discard a creature card: Search your
    // library for a creature card, reveal it, put it into your hand, then
    // shuffle") is OUTSIDE the OracleActivatedAbilityBinder reconstructable set
    // (self-pump / pinger / keyword-grant / counter / draw / gain-life /
    // regenerate) — a "search your library → hand → shuffle" tutor is not a
    // parseable shape. The migration retargets the effect to read the searching
    // player off ResolutionContext.Source's controller (rather than capturing
    // `card`) and marks the ability RebindSafe, so Agatha's group-grant re-homes
    // the REAL tutor (and its {T} cost, auto-re-homed by RebindTo Stage 1; the
    // {G} mana cost + "Discard a creature card" cost pass through, paid by the
    // bearer's controller) onto a counter-bearing bearer via
    // ActivatedAbility.RebindTo (CR 707.2 / 613.1f) — the BEARER'S controller
    // searches THEIR library, never re-reading the exiled Fauna Shaman.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_FaunaShaman_TutorToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its sole
        // non-mana activated ability (the {G},{T},Discard tutor) is now
        // RebindSafe (searches ResolutionContext.Source's controller's library).
        // A library tutor is NOT reconstructable from oracle text, so the
        // RebindTo of the real ability is the only sound re-home.
        var faunaShaman = FaunaShamanFactory.Create(alice);
        var realAbilities = faunaShaman.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        realAbilities.Should().ContainSingle(
            "Fauna Shaman has exactly one non-mana activated ability — the tutor");
        realAbilities.Should().OnlyContain(a => a.RebindSafe,
            "the migrated Fauna Shaman ability reads ResolutionContext.Source and is RebindSafe");
        alice.Zones.Graveyard.AddCard(faunaShaman);
        faunaShaman.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // OracleStub deliberately returns NOTHING for Fauna Shaman so the only
        // way the ability is granted is via RebindTo of the real ability — the
        // oracle-rebuild fallback cannot reconstruct a library tutor, so if the
        // grant still depended on it nothing would be emitted and this test
        // would fail.
        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), faunaShaman);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "Fauna Shaman's real tutor is re-homed via RebindTo");
        var tutor = granted[0];
        tutor.Source.Should().BeSameAs(bearer,
            "the re-homed tutor is sourced on the BEARER (CR 707.2)");
        tutor.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");
        tutor.Costs.OfType<Majik.Core.Costs.AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == Majik.Core.Costs.AdditionalCostType.Tap)
            .Which.Description.Should().Contain("Tap",
                "the {T} cost is auto-re-homed to the bearer by RebindTo (Stage 1)");

        // Stock the bearer-controller's (Alice's) library with a creature so the
        // tutor has a legal target.
        var libCreature = new Creature("Library Bear", "1G", 2, 2);
        libCreature.SetOwner(alice);
        alice.Zones.Library.AddCard(libCreature);
        libCreature.SetZone(ZoneType.Library);

        var handBefore = alice.Zones.Hand.GetCards().Count();

        // Resolving the re-homed tutor searches the BEARER'S controller (Alice)'s
        // library and moves the creature to Alice's hand —
        // ResolutionContext.Source = bearer => its controller = Alice.
        await tutor.ResolveAsync(agent: null, game: null);

        alice.Zones.Hand.GetCards().Should().Contain(libCreature,
            "the re-homed tutor put the bearer-controller's library creature into their hand");
        alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1);
        alice.Zones.Library.GetCards().Should().NotContain(libCreature,
            "the tutored card left the library");
    }

    [Fact]
    public async Task BespokeTutor_ResolvesOnOwnSourceWhenNotRebound()
    {
        // Sanity: the migrated effect still searches its OWN source's
        // controller's library on the normal (un-rebound) resolution path —
        // ResolutionContext.Source = the card.
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var zones = new Majik.Core.Services.ZoneService(bus);

        var faunaShaman = FaunaShamanFactory.Create(alice);
        alice.Zones.Library.AddCard(faunaShaman);
        zones.MoveCard(faunaShaman, ZoneType.Library, ZoneType.Battlefield, alice);

        var tutor = faunaShaman.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        var libCreature = new Creature("Library Bear", "1G", 2, 2);
        libCreature.SetOwner(alice);
        alice.Zones.Library.AddCard(libCreature);
        libCreature.SetZone(ZoneType.Library);

        await tutor.ResolveAsync(agent: null, game: null);

        alice.Zones.Hand.GetCards().Should().Contain(libCreature,
            "resolving the un-rebound tutor searches its own source's controller's library");
    }

    // -----------------------------------------------------------------------
    // agatha-grant-next-bespoke-closure-resourcecontext-migration — Arcbound
    // Ravager is a bespoke [CardName]-factory creature whose sole non-mana
    // activated ability ("Sacrifice an artifact: Put a +1/+1 counter on this
    // creature.") is OUTSIDE the OracleActivatedAbilityBinder reconstructable
    // set. The binder's "self-counter" shape ("{cost}: Put a +1/+1 counter on
    // this creature.") only recognises a MANA / {T} / "Sacrifice this creature"
    // cost (CR 602.1); Arcbound Ravager's cost is "Sacrifice AN ARTIFACT" — a
    // different, non-self sacrifice the cost grammar does not match — so the
    // oracle-rebuild fallback SKIPS the clause entirely (the Skithiryx-class
    // case). The migration retargets the effect to put the +1/+1 counter on
    // ResolutionContext.Source (rather than capturing `card`) and marks the
    // ability RebindSafe, so Agatha's group-grant re-homes the REAL ability
    // onto a counter-bearing bearer via ActivatedAbility.RebindTo (CR 707.2 /
    // 613.1f) — the BEARER receives the +1/+1 counter, never the exiled
    // Arcbound Ravager. The "Sacrifice an artifact" cost passes through
    // unchanged (it does not capture the source — it sacrifices any artifact
    // the activating player controls), paid by the bearer's controller.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_ArcboundRavager_SelfCounterToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its sole
        // non-mana activated ability (the "Sacrifice an artifact: +1/+1
        // counter on self") is now RebindSafe (counters ResolutionContext.Source).
        // The "Sacrifice an artifact" cost is outside the oracle binder's cost
        // grammar, so the RebindTo of the real ability is the only sound re-home.
        var ravager = ArcboundRavagerFactory.Create(alice);
        var realAbilities = ravager.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        realAbilities.Should().ContainSingle(
            "Arcbound Ravager has exactly one non-mana activated ability — the self-counter");
        realAbilities.Should().OnlyContain(a => a.RebindSafe,
            "the migrated Arcbound Ravager ability reads ResolutionContext.Source and is RebindSafe");
        alice.Zones.Graveyard.AddCard(ravager);
        ravager.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // OracleStub deliberately returns NOTHING for Arcbound Ravager so the
        // only way the ability is granted is via RebindTo of the real ability —
        // the oracle-rebuild fallback cannot reconstruct a "Sacrifice an
        // artifact" cost, so if the grant still depended on it nothing would be
        // emitted and this test would fail.
        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), ravager);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "Arcbound Ravager's real self-counter ability is re-homed via RebindTo");
        var selfCounter = granted[0];
        selfCounter.Source.Should().BeSameAs(bearer,
            "the re-homed self-counter ability is sourced on the BEARER (CR 707.2)");
        selfCounter.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");

        // Resolving the re-homed ability puts a +1/+1 counter on the BEARER,
        // never the exiled Arcbound Ravager —
        // ResolutionContext.Source = bearer.
        var bearerCountersBefore = bearer.Counters.Count(CounterType.PlusOnePlusOne);
        var ravagerCountersBefore = ravager.Counters.Count(CounterType.PlusOnePlusOne);

        await selfCounter.ResolveAsync(agent: null, game: null);

        bearer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(bearerCountersBefore + 1,
            "the re-homed self-counter ability adds a +1/+1 counter to the BEARER");
        ravager.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(ravagerCountersBefore,
            "the exiled imprinted Arcbound Ravager never receives the counter");
    }

    [Fact]
    public async Task BespokeSelfCounter_ResolvesOnOwnSourceWhenNotRebound()
    {
        // Sanity: the migrated effect still puts the +1/+1 counter on its OWN
        // source on the normal (un-rebound) resolution path —
        // ResolutionContext.Source = the card.
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var zones = new Majik.Core.Services.ZoneService(bus);

        var ravager = ArcboundRavagerFactory.Create(alice);
        alice.Zones.Library.AddCard(ravager);
        zones.MoveCard(ravager, ZoneType.Library, ZoneType.Battlefield, alice);

        var selfCounter = ravager.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        var before = ravager.Counters.Count(CounterType.PlusOnePlusOne);

        await selfCounter.ResolveAsync(agent: null, game: null);

        ravager.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(before + 1,
            "resolving the un-rebound self-counter ability adds the counter to its own source");
    }

    // -----------------------------------------------------------------------
    // agatha-bespoke-resolutioncontext-source-migration-batch — Scavenging
    // Ooze is a bespoke [CardName]-factory creature whose sole activated
    // ability ("{G}: Exile target creature card from a graveyard. If you do,
    // put a +1/+1 counter on Scavenging Ooze. You gain 1 life.") is OUTSIDE
    // the OracleActivatedAbilityBinder reconstructable set — a graveyard-
    // exile-then-pump-and-lifegain rider is not a parseable shape. The
    // migration retargets the +1/+1 counter onto ResolutionContext.Source and
    // "you" (life gain + own-graveyard seed) onto its controller (rather than
    // capturing `card` / `owner`) and marks the ability RebindSafe, so Agatha's
    // group-grant re-homes the REAL ability onto a counter-bearing bearer via
    // ActivatedAbility.RebindTo (CR 707.2 / 613.1f) — the BEARER receives the
    // +1/+1 counter and the BEARER's controller gains the life, never re-reading
    // the exiled Scavenging Ooze.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_ScavengingOoze_ExilePumpLifegainToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var ooze = ScavengingOozeFactory.Create(alice);
        var realAbilities = ooze.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        realAbilities.Should().ContainSingle(
            "Scavenging Ooze has exactly one non-mana activated ability — the {G} exile");
        realAbilities.Should().OnlyContain(a => a.RebindSafe,
            "the migrated Scavenging Ooze ability reads ResolutionContext.Source and is RebindSafe");
        alice.Zones.Graveyard.AddCard(ooze);
        ooze.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), ooze);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "Scavenging Ooze's real exile ability is re-homed via RebindTo");
        var exileAbility = granted[0];
        exileAbility.Source.Should().BeSameAs(bearer,
            "the re-homed exile ability is sourced on the BEARER (CR 707.2)");
        exileAbility.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");

        // A creature card in the bearer-controller's (Alice's) graveyard so the
        // exile has a legal pick. (The exiled Ooze itself is also a creature
        // card in Alice's graveyard — either is a legal exile pick.)
        var foodCreature = new Creature("Graveyard Bear", "1G", 2, 2);
        foodCreature.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(foodCreature);
        foodCreature.SetZone(ZoneType.Graveyard);

        var bearerCountersBefore = bearer.Counters.Count(CounterType.PlusOnePlusOne);
        var lifeBefore = alice.LifeTotal;

        await exileAbility.ResolveAsync(agent: null, game: null);

        bearer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(bearerCountersBefore + 1,
            "the re-homed ability puts the +1/+1 counter on the BEARER (ctx.Source)");
        alice.LifeTotal.Should().Be(lifeBefore + 1,
            "the re-homed ability's controller (Alice) gains 1 life");
    }

    [Fact]
    public async Task BespokeExilePump_ResolvesOnOwnSourceWhenNotRebound()
    {
        // Sanity: the migrated effect still puts the +1/+1 counter on its OWN
        // source and gains life for its own controller on the normal
        // (un-rebound) resolution path — ResolutionContext.Source = the card.
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var zones = new Majik.Core.Services.ZoneService(bus);

        var ooze = ScavengingOozeFactory.Create(alice);
        alice.Zones.Library.AddCard(ooze);
        zones.MoveCard(ooze, ZoneType.Library, ZoneType.Battlefield, alice);

        var dead = new Creature("Dead Bear", "1G", 2, 2);
        dead.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(dead);
        dead.SetZone(ZoneType.Graveyard);

        var exile = ooze.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        var countersBefore = ooze.Counters.Count(CounterType.PlusOnePlusOne);
        var lifeBefore = alice.LifeTotal;

        await exile.ResolveAsync(agent: null, game: null);

        ooze.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(countersBefore + 1,
            "the un-rebound exile ability counters its own source");
        alice.LifeTotal.Should().Be(lifeBefore + 1, "the un-rebound exile ability gains its controller 1 life");
        alice.Zones.Exile.GetCards().Should().Contain(dead, "the targeted creature card was exiled");
    }

    // -----------------------------------------------------------------------
    // seasoned-pyromancer-resolutioncontext-source-migration — Joraga
    // Treespeaker is a bespoke [CardName]-factory creature whose sole non-mana
    // activated ability is its Level up (CR 702.87a — "Level up {1}{G}:
    // {1}{G}: Put a level counter on this. Level up only as a sorcery."). Level
    // up IS an activated ability, so Agatha's Soul Cauldron grants it to a
    // counter-bearing bearer. "Put a level counter on THIS" must place the
    // counter on the BEARER, not the exiled Joraga. The migration retargets the
    // level-counter placement onto ResolutionContext.Source (falling back to the
    // card on the legacy sync path) and marks the ability RebindSafe, so the
    // group-grant re-homes the REAL Level up ability via ActivatedAbility.
    // RebindTo (CR 707.2 / 613.1f). "Put a level counter on this" is outside the
    // OracleActivatedAbilityBinder reconstructable set (its self-counter shape
    // is +1/+1 only), so the RebindTo of the real ability is the sole sound
    // re-home (the Skithiryx-class case).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_JoragaTreespeaker_LevelCounterToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var joraga = JoragaTreespeakerFactory.Create(alice);
        var realAbilities = joraga.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        realAbilities.Should().ContainSingle(
            "Joraga Treespeaker has exactly one non-mana activated ability — its Level up");
        realAbilities.Should().OnlyContain(a => a.RebindSafe,
            "the migrated Joraga Level up ability reads ResolutionContext.Source and is RebindSafe");
        alice.Zones.Graveyard.AddCard(joraga);
        joraga.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // OracleStub deliberately returns NOTHING for Joraga so the only way the
        // ability is granted is via RebindTo of the real ability — the oracle-
        // rebuild fallback cannot reconstruct "Put a level counter on this", so
        // if the grant still depended on it nothing would be emitted and this
        // test would fail.
        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), joraga);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "Joraga Treespeaker's real Level up ability is re-homed via RebindTo");
        var levelUp = granted[0];
        levelUp.Source.Should().BeSameAs(bearer,
            "the re-homed Level up ability is sourced on the BEARER (CR 707.2)");
        levelUp.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");

        // Resolving the re-homed ability puts a level counter on the BEARER,
        // never the exiled Joraga Treespeaker — ResolutionContext.Source =
        // bearer.
        var bearerLevelsBefore = bearer.Counters.Count(CounterType.Level);
        var joragaLevelsBefore = joraga.Counters.Count(CounterType.Level);

        await levelUp.ResolveAsync(agent: null, game: null);

        bearer.Counters.Count(CounterType.Level).Should().Be(bearerLevelsBefore + 1,
            "the re-homed Level up ability adds a level counter to the BEARER");
        joraga.Counters.Count(CounterType.Level).Should().Be(joragaLevelsBefore,
            "the exiled imprinted Joraga Treespeaker never receives the level counter");
    }

    [Fact]
    public async Task BespokeLevelUp_ResolvesOnOwnSourceWhenNotRebound()
    {
        // Sanity: the migrated effect still puts the level counter on its OWN
        // source on the normal (un-rebound) resolution path —
        // ResolutionContext.Source = the card.
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var zones = new Majik.Core.Services.ZoneService(bus);

        var joraga = JoragaTreespeakerFactory.Create(alice);
        alice.Zones.Library.AddCard(joraga);
        zones.MoveCard(joraga, ZoneType.Library, ZoneType.Battlefield, alice);

        var levelUp = joraga.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        var before = joraga.Counters.Count(CounterType.Level);

        await levelUp.ResolveAsync(agent: null, game: null);

        joraga.Counters.Count(CounterType.Level).Should().Be(before + 1,
            "resolving the un-rebound Level up ability adds the counter to its own source");
    }

    // -----------------------------------------------------------------------
    // agatha-oracle-shape-scavenging-ooze-exile-gy-pump-gain — Mother of Runes
    // is a bespoke [CardName]-factory creature whose sole activated ability
    // ("{T}: Target creature you control gains protection from the color of
    // your choice until end of turn.") is OUTSIDE the
    // OracleActivatedAbilityBinder reconstructable set — a chosen-colour
    // protection grant is not a parseable shape. The migration reads the chosen
    // target off ResolutionContext.ChosenTargets and "you" (the grant-quality
    // controller) off ctx.Controller — rather than capturing a captured ability
    // handle / `owner` — and marks the ability RebindSafe, so Agatha's group-
    // grant re-homes the REAL ability onto a counter-bearing bearer via
    // ActivatedAbility.RebindTo (CR 707.2 / 613.1f) — the {T} cost taps the
    // BEARER and "you" is the BEARER's controller, never the exiled Mother of
    // Runes. The protection grant is self-sourced on the chosen TARGET, so the
    // re-home does not re-read Mother of Runes.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_MotherOfRunes_ProtectionGrantToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var mother = MotherOfRunesFactory.Create(alice);
        var realAbilities = mother.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        realAbilities.Should().ContainSingle(
            "Mother of Runes has exactly one non-mana activated ability — the {T} protection grant");
        realAbilities.Should().OnlyContain(a => a.RebindSafe,
            "the migrated Mother of Runes ability reads ResolutionContext.ChosenTargets/Controller and is RebindSafe");
        alice.Zones.Graveyard.AddCard(mother);
        mother.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), mother);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "Mother of Runes' real protection-grant ability is re-homed via RebindTo");
        var grantAbility = granted[0];
        grantAbility.Source.Should().BeSameAs(bearer,
            "the re-homed grant ability is sourced on the BEARER (CR 707.2)");
        grantAbility.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");

        // The bearer (a creature Alice controls, with ActiveEffects wired)
        // is the chosen target — "target creature you control" off the
        // re-homed controller.
        grantAbility.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bearer } });

        Majik.Core.Rules.Protection.HasProtectionFromColor(bearer, Majik.Core.ValueObjects.ManaColor.White)
            .Should().BeFalse("the bearer has no protection before the grant resolves");

        await grantAbility.ResolveAsync(agent: null, game: null);

        Majik.Core.Rules.Protection.HasProtectionFromColor(bearer, Majik.Core.ValueObjects.ManaColor.White)
            .Should().BeTrue(
                "the re-homed grant gives the chosen BEARER protection from white (self-sourced on the target)");
    }

    [Fact]
    public async Task BespokeProtectionGrant_ResolvesOnChosenTargetWhenNotRebound()
    {
        // Sanity: the migrated effect still grants protection to the CHOSEN
        // target on the normal (un-rebound) resolution path — it reads
        // ResolutionContext.ChosenTargets, not a captured ability handle.
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var mother = MotherOfRunesFactory.Create(alice);
        alice.Zones.Library.AddCard(mother);
        zones.MoveCard(mother, ZoneType.Library, ZoneType.Battlefield, alice);
        mother.ActiveEffects = effects;

        var grant = mother.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);
        grant.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { mother } });

        await grant.ResolveAsync(agent: null, game: null);

        Majik.Core.Rules.Protection.HasProtectionFromColor(mother, Majik.Core.ValueObjects.ManaColor.White)
            .Should().BeTrue(
                "the un-rebound grant gives the chosen target (Mother of Runes itself) protection from white");
    }

    // -----------------------------------------------------------------------
    // agatha-bespoke-resolutioncontext-source-migration-batch — Spikeshot
    // Goblin is a bespoke [CardName]-factory creature whose sole activated
    // ability ("{R}, {T}: This creature deals damage equal to its power to any
    // target.") is OUTSIDE the OracleActivatedAbilityBinder reconstructable set
    // — a "damage equal to its power" pinger is not a parseable fixed-amount
    // shape. The migration reads the source's power off
    // ResolutionContext.Source and the chosen target off ctx.ChosenTargets
    // (rather than capturing `card` / a captured ability handle) and marks the
    // ability RebindSafe, so Agatha's group-grant re-homes the REAL ping onto a
    // counter-bearing bearer via ActivatedAbility.RebindTo (CR 707.2 / 613.1f)
    // — the damage scales with the BEARER's power and the {T} taps the BEARER,
    // never the exiled Spikeshot Goblin.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_SpikeshotGoblin_PingToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var spikeshot = SpikeshotGoblinFactory.Create(alice);
        var realAbilities = spikeshot.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        realAbilities.Should().ContainSingle(
            "Spikeshot Goblin has exactly one non-mana activated ability — the ping");
        realAbilities.Should().OnlyContain(a => a.RebindSafe,
            "the migrated Spikeshot Goblin ability reads ResolutionContext.Source and is RebindSafe");
        alice.Zones.Graveyard.AddCard(spikeshot);
        spikeshot.SetZone(ZoneType.Graveyard);

        // Bearer with base power 4 (+ the SeatedBearer +1/+1 counter = 5 live
        // power) so the ping deals the BEARER's power (5), not Spikeshot's
        // printed 1 — proving the migrated effect reads ctx.Source's power.
        var bearer = SeatedBearer(alice, effects, zones, power: 4, toughness: 4);
        bearer.Power.Should().Be(5, "Counter Bear base 4/4 + the +1/+1 counter SeatedBearer adds");

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), spikeshot);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "Spikeshot Goblin's real ping is re-homed via RebindTo");
        var ping = granted[0];
        ping.Source.Should().BeSameAs(bearer,
            "the re-homed ping is sourced on the BEARER (CR 707.2)");
        ping.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");
        ping.Costs.OfType<Majik.Core.Costs.AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == Majik.Core.Costs.AdditionalCostType.Tap)
            .Which.Description.Should().Contain("Tap",
                "the {T} cost is auto-re-homed to the bearer by RebindTo (Stage 1)");

        // A creature to absorb the ping; the damage should equal the BEARER's
        // power (5), not the exiled Spikeshot's printed power (1).
        var victim = new Creature("Victim", "1G", 6, 6);
        victim.SetOwner(alice);
        alice.Zones.Library.AddCard(victim);
        zones.MoveCard(victim, ZoneType.Library, ZoneType.Battlefield, alice);

        ping.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { victim } });

        await ping.ResolveAsync(agent: null, game: null);

        victim.Damage.Should().Be(5,
            "the re-homed ping dealt damage equal to the BEARER's power (5), not Spikeshot's printed 1");
    }

    [Fact]
    public async Task BespokePowerPinger_ResolvesOnOwnSourceWhenNotRebound()
    {
        // Sanity: the migrated effect still reads its OWN source's power on the
        // normal (un-rebound) resolution path — ResolutionContext.Source = card.
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var zones = new Majik.Core.Services.ZoneService(bus);

        var spikeshot = SpikeshotGoblinFactory.Create(alice);
        alice.Zones.Library.AddCard(spikeshot);
        zones.MoveCard(spikeshot, ZoneType.Library, ZoneType.Battlefield, alice);

        var ping = spikeshot.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        var victim = new Creature("Victim", "1G", 5, 5);
        victim.SetOwner(alice);
        alice.Zones.Library.AddCard(victim);
        zones.MoveCard(victim, ZoneType.Library, ZoneType.Battlefield, alice);

        ping.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { victim } });

        await ping.ResolveAsync(agent: null, game: null);

        victim.Damage.Should().Be(1,
            "the un-rebound ping deals damage equal to its own source's power (Spikeshot's 1)");
    }

    // -----------------------------------------------------------------------
    // agatha-bespoke-resolutioncontext-source-migration-batch — Goblin Welder
    // is a bespoke [CardName]-factory creature whose sole activated ability
    // ("{T}: ... that player sacrifices the artifact they control and returns
    // the artifact card from their graveyard to the battlefield") is OUTSIDE
    // the OracleActivatedAbilityBinder reconstructable set. The effect body
    // already captured no authoring permanent / player (it scans the live
    // game's graveyards via ctx.Game.AllPlayers) and the sole cost is an
    // AdditionalCost.Tap that RebindTo re-homes automatically (Stage 1), so the
    // migration is a pure RebindSafe annotation — Agatha's group-grant re-homes
    // the REAL weld onto a counter-bearing bearer via ActivatedAbility.RebindTo
    // (CR 707.2 / 613.1f); the {T} taps the BEARER, never the exiled Welder.
    // -----------------------------------------------------------------------

    [Fact]
    public void Grant_RebindsBespokeFactoryCreature_GoblinWelder_WeldToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var welder = GoblinWelderFactory.Create(alice);
        var realAbilities = welder.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        realAbilities.Should().ContainSingle(
            "Goblin Welder has exactly one non-mana activated ability — the weld");
        realAbilities.Should().OnlyContain(a => a.RebindSafe,
            "the migrated Goblin Welder ability captures no source and is RebindSafe");
        alice.Zones.Graveyard.AddCard(welder);
        welder.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), welder);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "Goblin Welder's real weld ability is re-homed via RebindTo");
        var weld = granted[0];
        weld.Source.Should().BeSameAs(bearer,
            "the re-homed weld ability is sourced on the BEARER (CR 707.2)");
        weld.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");
        weld.Costs.OfType<Majik.Core.Costs.AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == Majik.Core.Costs.AdditionalCostType.Tap)
            .Which.Description.Should().Contain("Tap",
                "the {T} cost is auto-re-homed to the bearer by RebindTo (Stage 1)");
    }

    // -----------------------------------------------------------------------
    // agatha-adapt-rebind — Pteramander's "{7}{U}: Adapt 4" (CR 702.116) is a
    // bespoke [CardName]-factory activated ability whose effect previously
    // captured the source card in its closure for BOTH the "no +1/+1 counters"
    // gate (CR 702.116b) and the counter placement (CR 702.116a). Migrated to
    // read ResolutionContext.Source for both, marked RebindSafe, so Agatha's
    // group-grant re-homes the REAL Adapt ability to a counter-bearing bearer
    // via ActivatedAbility.RebindTo (CR 707.2 / 613.1f). Adapt is OUTSIDE the
    // OracleActivatedAbilityBinder reconstructable set, so the RebindTo of the
    // real ability is the only sound re-home.
    // -----------------------------------------------------------------------

    [Fact]
    public void Grant_RebindsBespokeFactoryCreature_Pteramander_AdaptToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its sole
        // non-mana activated ability (the {7}{U} Adapt 4) is now RebindSafe
        // (reads ResolutionContext.Source). Adapt is not reconstructable from
        // oracle text, so the RebindTo of the real ability is the only sound
        // re-home.
        var pteramander = PteramanderFactory.Create(alice);
        pteramander.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .Should().OnlyContain(a => a.RebindSafe,
                "the migrated Pteramander Adapt ability reads ResolutionContext.Source and is RebindSafe");
        alice.Zones.Graveyard.AddCard(pteramander);
        pteramander.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), pteramander);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "Pteramander's real Adapt ability is re-homed via RebindTo");
        var adapt = granted[0];
        adapt.Source.Should().BeSameAs(bearer,
            "the re-homed Adapt ability is sourced on the BEARER (CR 707.2)");
        adapt.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");
    }

    [Fact]
    public async Task AdaptRebind_PlacesCountersOnBearer_NotExiledCard()
    {
        // The gate + placement read ResolutionContext.Source. Re-home the real
        // Adapt ability onto a COUNTER-LESS permanent and resolve it: the four
        // +1/+1 counters land on the BEARER, never the exiled Pteramander.
        var alice = new Player("Alice", 20);
        var pteramander = PteramanderFactory.Create(alice);
        var adapt = pteramander.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        var bearer = new Creature("Adapt Bearer", "1G", 2, 2);
        bearer.SetOwner(alice);
        bearer.SetController(alice);
        bearer.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(bearer);

        var rebound = adapt.RebindTo(bearer, alice);
        await rebound.ResolveAsync(agent: null, game: null);

        bearer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(PteramanderFactory.AdaptAmount,
            "Adapt 4 (CR 702.116a) places four +1/+1 counters on the BEARER (ResolutionContext.Source)");
        pteramander.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the exiled imprinted Pteramander never receives the counters");
    }

    [Fact]
    public async Task AdaptRebind_FizzlesReadingBearerCounters_NotExiledCard()
    {
        // CR 702.116b — the "no +1/+1 counters" gate reads the BEARER (the
        // rebound source), not the exiled card. A bearer that already carries a
        // +1/+1 counter fizzles, even though the exiled Pteramander has none.
        var alice = new Player("Alice", 20);
        var pteramander = PteramanderFactory.Create(alice);
        var adapt = pteramander.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        var bearer = new Creature("Counterful Bearer", "1G", 2, 2);
        bearer.SetOwner(alice);
        bearer.SetController(alice);
        bearer.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(bearer);
        bearer.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var rebound = adapt.RebindTo(bearer, alice);
        await rebound.ResolveAsync(agent: null, game: null);

        bearer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the gate read the BEARER's existing counter and fizzled (CR 702.116b)");
    }

    [Fact]
    public async Task Adapt_ResolvesOnOwnSourceWhenNotRebound()
    {
        // Sanity: the migrated effect still places counters on its OWN source on
        // the normal (un-rebound) resolution path — ResolutionContext.Source =
        // the card.
        var alice = new Player("Alice", 20);
        var pteramander = PteramanderFactory.Create(alice);
        pteramander.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(pteramander);
        var adapt = pteramander.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        await adapt.ResolveAsync(agent: null, game: null);

        pteramander.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(PteramanderFactory.AdaptAmount,
            "resolving the un-rebound Adapt places counters on its own source");
    }

    // -----------------------------------------------------------------------
    // agatha-rebind-steel-hellkite-variable-x-sweep — Steel Hellkite is a
    // bespoke [CardName]-factory artifact creature with TWO non-mana activated
    // abilities, BOTH now RebindSafe:
    //   * "{2}: This creature gets +1/+0 until end of turn." — reads
    //     ResolutionContext.Source for the pump subject (migrated earlier).
    //   * "{X}: Destroy each nonland permanent with mv X whose controller was
    //     dealt combat damage by THIS CREATURE this turn." — the combat-victim
    //     tracker is now keyed by the damage-SOURCE permanent and the sweep
    //     reads its victim set + X off the live ResolutionContext, so re-homing
    //     it to a BEARER uses the BEARER's combat-damage linkage.
    // Agatha's group-grant re-homes BOTH real abilities onto a counter-bearing
    // bearer via ActivatedAbility.RebindTo (CR 707.2 / 613.1f).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_SteelHellkite_BothAbilitiesToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var hellkite = SteelHellkiteFactory.Create(alice);
        // BOTH the pump and the {X} destroy-sweep are now RebindSafe.
        hellkite.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .Should().OnlyContain(a => a.RebindSafe,
                "both Steel Hellkite non-mana abilities read the live ResolutionContext and are RebindSafe");
        alice.Zones.Graveyard.AddCard(hellkite);
        hellkite.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones, power: 4, toughness: 4);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), hellkite);

        var granted = GrantedActivated(bearer);
        granted.Should().HaveCount(2,
            "BOTH of Steel Hellkite's real activated abilities are re-homed via RebindTo");
        granted.Should().OnlyContain(a => ReferenceEquals(a.Source, bearer),
            "every re-homed ability is sourced on the BEARER (CR 707.2)");

        // The pump re-homed to the bearer: base 4/4 + the SeatedBearer +1/+1
        // counter = 5/5 before the pump. Resolve via the ability path
        // (ResolutionContext.Source = the bearer) so the reused effect
        // re-sources itself.
        var pump = granted.Single(a =>
            a.Costs.OfType<Majik.Core.Costs.ManaCostCost>().Any(m => m.Description == "2"));
        var powerBefore = bearer.GetPower();
        await pump.ResolveAsync(agent: null, game: null);
        bearer.GetPower().Should().Be(powerBefore + 1,
            "the re-homed +1/+0 pumped the BEARER, not the exiled Steel Hellkite");
    }

    [Fact]
    public void BespokeSteelHellkitePump_ResolvesOnOwnSourceWhenNotRebound()
    {
        // Sanity: the migrated pump still pumps its OWN source on the normal
        // (un-rebound) resolution path — ResolutionContext.Source = card.
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var hellkite = SteelHellkiteFactory.Create(alice);
        alice.Zones.Library.AddCard(hellkite);
        zones.MoveCard(hellkite, ZoneType.Library, ZoneType.Battlefield, alice);
        hellkite.ActiveEffects = effects;

        // Select the PUMP specifically (the {2} ability) — both non-mana
        // abilities are now RebindSafe.
        var pump = hellkite.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<Majik.Core.Costs.ManaCostCost>()
                .Any(m => m.Description == "2"));

        var powerBefore = hellkite.GetPower();
        foreach (var effect in pump.Effects) effect.Execute();
        hellkite.GetPower().Should().Be(powerBefore + 1,
            "the un-rebound pump pumps its own source (Steel Hellkite)");
    }

    // -----------------------------------------------------------------------
    // agatha-stale-body-rewrite-then-migrate — Etched Oracle is a bespoke
    // [CardName]-factory artifact creature. Its "{1}, Remove four +1/+1
    // counters from this creature: Target player draws three cards." ability
    // now declares the counter-removal as an AdditionalCost.RemoveCounters
    // cost (additional-cost-remove-counters-primitive). That cost is
    // re-source-safe (rebinds via AdditionalCost.RebindSource), and the draw
    // reads its target/controller off the ResolutionContext, so the whole
    // ability is RebindSafe — Agatha's group-grant re-homes the REAL ability
    // onto a counter-bearing bearer via ActivatedAbility.RebindTo
    // (CR 707.2 / 613.1f); the cost's counters come off the BEARER.
    // -----------------------------------------------------------------------

    [Fact]
    public void Grant_RebindsBespokeFactoryCreature_EtchedOracle_DrawToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var oracle = EtchedOracleFactory.Create(alice);
        var realAbilities = oracle.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        realAbilities.Should().ContainSingle(
            "Etched Oracle has exactly one non-mana activated ability — the each-player-draw");
        realAbilities.Should().OnlyContain(a => a.RebindSafe,
            "the migrated Etched Oracle ability reads ResolutionContext.Source and is RebindSafe");
        alice.Zones.Graveyard.AddCard(oracle);
        oracle.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), oracle);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "Etched Oracle's real each-player-draw ability is re-homed via RebindTo");
        var draw = granted[0];
        draw.Source.Should().BeSameAs(bearer,
            "the re-homed ability is sourced on the BEARER (CR 707.2)");
        draw.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");
    }

    [Fact]
    public async Task BespokeEtchedOracle_RemovesCountersFromOwnSourceWhenNotRebound()
    {
        // Sanity: on the normal (un-rebound) path the DECLARED counter-removal
        // cost (AdditionalCost.RemoveCounters) comes off the ability's OWN
        // source, and the resolve effect draws three for the controller (the
        // no-target fallback). Stack four +1/+1 counters, pay the declared cost,
        // resolve, expect them removed and the controller to draw three.
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var zones = new Majik.Core.Services.ZoneService(bus);

        var oracle = EtchedOracleFactory.Create(alice);
        oracle.Counters.Add(CounterType.PlusOnePlusOne, 4);
        alice.Zones.Library.AddCard(oracle);
        zones.MoveCard(oracle, ZoneType.Library, ZoneType.Battlefield, alice);

        // Three cards to draw.
        for (var i = 0; i < 3; i++)
        {
            var c = new Creature($"Card{i}", "G", 1, 1);
            c.SetOwner(alice);
            alice.Zones.Library.AddCard(c);
        }

        var ability = oracle.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        // Pay the DECLARED counter-removal cost (CR 118.3), then resolve.
        var counterCost = ability.Costs
            .OfType<Majik.Core.Costs.AdditionalCost>()
            .Single(c => c.CostType == Majik.Core.Costs.AdditionalCostType.RemoveCounters);
        new Majik.Core.Costs.CostPayment().PayCosts(alice, new[] { (Majik.Core.Costs.ICost)counterCost });

        await ability.ResolveAsync(agent: null, game: null);

        oracle.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the declared counter-removal cost removed four +1/+1 counters from its own source");
        alice.Zones.Hand.GetCards().Count().Should().Be(3,
            "the controller drew three cards (no-target fallback)");
    }

    // -----------------------------------------------------------------------
    // agatha-oracle-shape-remove-counters-each-player-draws — the
    // OracleActivatedAbilityBinder now reconstructs a counter-removal cost token
    // ("Remove N +1/+1 counters from this creature") inside its cost grammar, so
    // the FALLBACK oracle-rebuild path (an arbitrary imprinted creature with the
    // Etched-Oracle-style shape but NO bespoke [CardName] factory) re-homes the
    // ability soundly: the declared AdditionalCost.RemoveCounters comes off the
    // BEARER (CR 118.3 / 707.2), and the existing draw verbs ride on top. This is
    // the soundly-reconstructable oracle shape the deferral asked for — the
    // counter-removal cost is re-source-safe (rebinds onto the new bearer via
    // AdditionalCost.RebindSource), exactly like the bespoke Etched Oracle.
    // -----------------------------------------------------------------------

    [Fact]
    public void Grant_NonMana_RemoveCountersTargetPlayerDraw_RehomesCostAndDrawToBearer()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // Imprinted creature with the Etched-Oracle-style shape but WITHOUT a
        // bespoke factory (a generic stub), so the only way Agatha re-homes its
        // ability is the OracleActivatedAbilityBinder reconstruction path.
        var stub = new Creature("Counter Sage Stub", "4", 0, 0);
        stub.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(stub);
        stub.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);
        // Give the bearer enough +1/+1 counters to pay the reconstructed cost.
        bearer.Counters.Add(CounterType.PlusOnePlusOne, 4);

        // Cards on BOB's library — the chosen target player draws their own.
        for (var i = 0; i < 3; i++)
        {
            var c = new Card($"Bob Lib {i}", "");
            c.SetOwner(bob);
            bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var cauldron = GrantingCauldron(alice, effects, bus,
            OracleStub(("Counter Sage Stub",
                "{1}, Remove four +1/+1 counters from this creature: Target player draws three cards.")));
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), stub);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "the binder reconstructs the remove-counters target-player-draw shape and re-homes it");
        var draw = granted[0];
        draw.Source.Should().BeSameAs(bearer,
            "the granted ability is re-homed to the BEARER, not the exiled card (CR 707.2)");

        // The mana leg is the {1}; the counter-removal leg is a declared
        // AdditionalCost.RemoveCounters re-homed onto the BEARER.
        draw.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the {1} mana leg is reconstructed");
        var counterCost = draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.RemoveCounters)
            .Subject;
        counterCost.CounterType.Should().Be(CounterType.PlusOnePlusOne,
            "the reconstructed cost removes +1/+1 counters");
        counterCost.CounterAmount.Should().Be(4,
            "the reconstructed cost removes the stated four counters");
        counterCost.Permanent.Should().BeSameAs(bearer,
            "the counter-removal cost is re-homed onto the BEARER (CR 707.2), not the exiled card");

        // Pay the declared cost off the BEARER, then choose BOB and resolve:
        // the chosen player draws from THEIR OWN library.
        new Majik.Core.Costs.CostPayment().PayCosts(
            alice, new[] { (Majik.Core.Costs.ICost)counterCost });
        bearer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "paying the reconstructed cost removed four +1/+1 counters from the BEARER (5 → 1)");

        draw.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        var bobHandBefore = bob.Zones.Hand.GetCards().Count();
        var aliceHandBefore = alice.Zones.Hand.GetCards().Count();
        foreach (var effect in draw.Effects) effect.Execute();
        bob.Zones.Hand.GetCards().Count().Should().Be(bobHandBefore + 3,
            "the re-homed \"target player draws three cards\" draws three for the CHOSEN player");
        alice.Zones.Hand.GetCards().Count().Should().Be(aliceHandBefore,
            "only the chosen target player draws — the controller does not");
    }

    // -----------------------------------------------------------------------
    // etched-oracle-variable-x-counter-removal-rebind (Pteramander leg) —
    // Pteramander is a bespoke [CardName]-factory creature whose Adapt 4
    // activated ability carries a GraveyardReducedManaCost ("{7}{U}; this
    // ability costs {1} less for each instant/sorcery in your graveyard").
    // The cost captures the source card to count "your graveyard" (CR 118.5).
    // Before this seam, an Agatha-granted Adapt re-homed its EFFECT + counter
    // gate to the bearer (RebindSafe) but the GraveyardReducedManaCost was a
    // plain ManaCostCost that passed THROUGH RebindTo untouched — still bound to
    // the exiled Pteramander, so the reduction read the WRONG graveyard. The
    // cost now implements IRebindableCost, so RebindTo (CR 707.2 / 613.1f)
    // swaps its captured source onto the BEARER.
    // -----------------------------------------------------------------------

    [Fact]
    public void Grant_RebindsBespokeFactoryCreature_Pteramander_ReducedCostHomedToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its Adapt
        // ability reads ResolutionContext.Source (RebindSafe) and carries the
        // GraveyardReducedManaCost the oracle-rebuild fallback cannot reconstruct.
        var pteramander = PteramanderFactory.Create(alice);
        var realAbility = pteramander.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);
        realAbility.RebindSafe.Should().BeTrue(
            "the Pteramander Adapt ability reads ResolutionContext.Source and is RebindSafe");
        realAbility.Costs.OfType<PteramanderFactory.GraveyardReducedManaCost>()
            .Single().Source.Should().BeSameAs(pteramander,
                "before re-home the reduction is bound to Pteramander's own source");
        alice.Zones.Graveyard.AddCard(pteramander);
        pteramander.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), pteramander);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "Pteramander's real Adapt ability is re-homed via RebindTo");
        var adapt = granted[0];
        adapt.Source.Should().BeSameAs(bearer,
            "the re-homed ability is sourced on the BEARER (CR 707.2)");

        var reducedCost = adapt.Costs
            .OfType<PteramanderFactory.GraveyardReducedManaCost>()
            .Should().ContainSingle(
                "the graveyard-reducing mana cost is re-homed, not dropped")
            .Subject;
        reducedCost.Source.Should().BeSameAs(bearer,
            "the GraveyardReducedManaCost is re-homed onto the BEARER (CR 707.2 / 613.1f) " +
            "so the {1}-less-per-instant/sorcery reduction reads the bearer's controller's graveyard");
    }

    [Fact]
    public void Pteramander_ReducedCost_ReadsOwnSourceWhenNotRebound()
    {
        // Sanity: on the normal (un-rebound) path the GraveyardReducedManaCost
        // is bound to Pteramander itself, and RebindTo against a non-matching
        // source is a no-op (returns the same instance — purity).
        var alice = new Player("Alice", 20);
        var pteramander = PteramanderFactory.Create(alice);

        var cost = pteramander.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility)
            .Costs.OfType<PteramanderFactory.GraveyardReducedManaCost>()
            .Single();

        cost.Source.Should().BeSameAs(pteramander);

        var someoneElse = new Creature("Bystander", "1G", 2, 2);
        cost.RebindTo(someoneElse, pteramander).Should().BeSameAs(cost,
            "RebindTo against a non-matching old source returns the same instance (pure no-op)");
    }

    // -----------------------------------------------------------------------
    // agatha-oracle-shape-yawgmoth-pay-life-counter-pump-loop — Yawgmoth, Thran
    // Physician is a bespoke [CardName]-factory creature whose activated ability
    // ("Pay 1 life, Sacrifice another creature: Put a -1/-1 counter on up to one
    // target creature and draw a card") has a TWO-leg non-tap cost (pay 1 life +
    // sacrifice another creature) and a "draw a card" rider that previously
    // captured the original owner. Migrated to read ResolutionContext.Controller
    // for the draw + marked RebindSafe; the SacrificeAnotherCreatureCost now
    // implements IRebindableCost so RebindTo re-homes its captured source. The
    // oracle-rebuild fallback CANNOT reconstruct this multi-leg cost shape, so
    // the PRIMARY RebindTo path of the REAL ability is the only sound re-home.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_Yawgmoth_DrawToBearerController()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its
        // activated ability is now RebindSafe (reads ResolutionContext.Source /
        // Controller) and carries the multi-leg pay-life + sacrifice-another cost
        // the oracle-rebuild fallback cannot reconstruct.
        var yawg = YawgmothFactory.Create(alice);
        var realAbility = yawg.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);
        realAbility.RebindSafe.Should().BeTrue(
            "the migrated Yawgmoth ability reads ResolutionContext.Source / Controller and is RebindSafe");
        alice.Zones.Graveyard.AddCard(yawg);
        yawg.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // OracleStub deliberately returns NOTHING for Yawgmoth so the only way the
        // ability is granted is via RebindTo of the real ability — if the grant
        // depended on the oracle fallback, nothing would be emitted for this
        // multi-leg-cost ability and this test would fail.
        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), yawg);

        var granted = GrantedActivated(bearer);
        granted.Should().HaveCount(1,
            "Yawgmoth's real activated ability is re-homed via RebindTo");
        var rehomed = granted[0];
        rehomed.Source.Should().BeSameAs(bearer,
            "the re-homed ability is sourced on the BEARER (CR 707.2)");
        rehomed.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");

        // The sacrifice-another cost was re-homed: it now excludes the BEARER
        // (the new source), not the exiled Yawgmoth.
        var sac = rehomed.Costs.OfType<Majik.Core.Costs.SacrificeAnotherCreatureCost>().Single();
        sac.EligibleSacrifices(alice).Should().NotContain(bearer,
            "the re-homed sacrifice-another cost excludes the BEARER (its new source)");

        // The pay-life leg survives.
        rehomed.Costs.OfType<Majik.Core.Costs.AdditionalCost>()
            .Should().Contain(c => c.Description.Contains("1 life"),
                "the pay-1-life leg survives the rebind");

        // Resolving the re-homed ability draws a card for the BEARER's controller
        // (ResolutionContext.Controller = bearer's controller), never the exiled
        // Yawgmoth's owner via a captured closure.
        var top = new Card("Swamp", "");
        top.SetOwner(alice);
        alice.Zones.Library.AddCard(top);
        await rehomed.ResolveAsync(agent: null, game: null);
        alice.Zones.Hand.GetCards().Should().Contain(top,
            "the re-homed draw reads ResolutionContext.Controller (the bearer's controller)");
    }

    // -----------------------------------------------------------------------
    // agatha-bespoke-factory-resolutioncontext-source-migration-endbringer-
    // reckoner — Endbringer is a bespoke [CardName]-factory creature whose
    // three activated abilities ({T}: 1 damage to any target / {C},{T}: target
    // player draws / {C},{T}: tap target creature) are OUTSIDE the
    // OracleActivatedAbilityBinder reconstructable set. The migration retargets
    // each effect to read its chosen target off ResolutionContext.ChosenTargets
    // (and the damage source / draw fallback off ctx.Source / ctx.Controller)
    // and marks all three RebindSafe, so Agatha's group-grant re-homes the REAL
    // abilities (and their {T} costs, auto-re-homed by RebindTo Stage 1) onto a
    // counter-bearing bearer via ActivatedAbility.RebindTo (CR 707.2 / 613.1f) —
    // the {T} taps the BEARER, never the exiled Endbringer.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_Endbringer_AllThreeAbilitiesToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var endbringer = EndbringerFactory.Create(alice);
        var realAbilities = endbringer.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        realAbilities.Should().HaveCount(3,
            "Endbringer has three activated abilities: ping, target-player-draw, tap-target");
        realAbilities.Should().OnlyContain(a => a.RebindSafe,
            "all migrated Endbringer abilities read ResolutionContext.Source/ChosenTargets and are RebindSafe");
        alice.Zones.Graveyard.AddCard(endbringer);
        endbringer.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        // OracleStub returns NOTHING for Endbringer so the only re-home path is
        // RebindTo of the real abilities — the binder fallback cannot
        // reconstruct a "1 damage to any target" pinger / tap-target form.
        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), endbringer);

        var granted = GrantedActivated(bearer);
        granted.Should().HaveCount(3,
            "all three of Endbringer's real abilities are re-homed via RebindTo");
        granted.Should().OnlyContain(a => ReferenceEquals(a.Source, bearer),
            "every re-homed ability is sourced on the BEARER (CR 707.2)");
        granted.Should().OnlyContain(a => a.RebindSafe,
            "RebindTo preserves the re-source provenance");

        // Resolve the re-homed PING against a victim — the damage is sourced
        // from the BEARER (ctx.Source), never the exiled Endbringer.
        var ping = granted.Single(a => a.Effects.Any(e =>
            e.Description.Contains("damage", StringComparison.OrdinalIgnoreCase)));
        var victim = new Creature("Victim", "1G", 5, 5);
        victim.SetOwner(alice);
        alice.Zones.Library.AddCard(victim);
        zones.MoveCard(victim, ZoneType.Library, ZoneType.Battlefield, alice);
        ping.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { victim } });
        await ping.ResolveAsync(agent: null, game: null);
        victim.Damage.Should().Be(1, "the re-homed ping deals 1 damage to its chosen target");

        // Resolve the re-homed TAP-TARGET against an untapped creature.
        var tapAbility = granted.Single(a => a.Effects.Any(e =>
            e.Description.Contains("tap target", StringComparison.OrdinalIgnoreCase)));
        var tapVictim = new Creature("Tapped", "1G", 2, 2);
        tapVictim.SetOwner(alice);
        alice.Zones.Library.AddCard(tapVictim);
        zones.MoveCard(tapVictim, ZoneType.Library, ZoneType.Battlefield, alice);
        tapVictim.IsTapped.Should().BeFalse();
        tapAbility.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { tapVictim } });
        await tapAbility.ResolveAsync(agent: null, game: null);
        tapVictim.IsTapped.Should().BeTrue("the re-homed tap-target taps its chosen creature");

        // Resolve the re-homed DRAW with no chosen target — falls back to the
        // BEARER's controller (ResolutionContext.Controller), never the exiled
        // Endbringer's owner via a captured closure.
        var draw = granted.Single(a => a.Effects.Any(e =>
            e.Description.Contains("draws a card", StringComparison.OrdinalIgnoreCase)));
        var top = new Card("Mountain", "");
        top.SetOwner(alice);
        alice.Zones.Library.AddCard(top);
        await draw.ResolveAsync(agent: null, game: null);
        alice.Zones.Hand.GetCards().Should().Contain(top,
            "the re-homed draw falls back to ResolutionContext.Controller (the bearer's controller)");
    }

    [Fact]
    public async Task BespokeEndbringerPing_ResolvesOnOwnSourceWhenNotRebound()
    {
        // Sanity: the migrated ping still deals damage to its chosen target on
        // the normal (un-rebound) resolution path.
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var zones = new Majik.Core.Services.ZoneService(bus);

        var endbringer = EndbringerFactory.Create(alice);
        alice.Zones.Library.AddCard(endbringer);
        zones.MoveCard(endbringer, ZoneType.Library, ZoneType.Battlefield, alice);

        var ping = endbringer.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Effects.Any(e =>
                e.Description.Contains("damage", StringComparison.OrdinalIgnoreCase)));

        var lifeBefore = alice.LifeTotal;
        ping.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { alice } });
        await ping.ResolveAsync(agent: null, game: null);
        alice.LifeTotal.Should().Be(lifeBefore - 1,
            "the un-rebound ping deals 1 damage to the chosen target (a player)");
    }

    // -----------------------------------------------------------------------
    // agatha-bespoke-factory-resolutioncontext-source-migration-endbringer-
    // reckoner — Reckoner Bankbuster is a bespoke [CardName]-factory
    // Artifact-Vehicle (Creature shell) whose activated ability ({T}, remove a
    // charge counter: draw a card; then if no charge counters remain, create a
    // Powerstone) is OUTSIDE the OracleActivatedAbilityBinder reconstructable
    // set. The migration reads the source whose charge-counter tail-clause it
    // inspects off ResolutionContext.Source and the drawing player off that
    // source's controller (then ctx.Controller), and marks the ability
    // RebindSafe — so Agatha's group-grant re-homes the REAL "draw a card" onto
    // a counter-bearing bearer via ActivatedAbility.RebindTo (CR 707.2 /
    // 613.1f); the {T} taps the BEARER and the RemoveChargeCounterCost is
    // re-homed via IRebindableCost (Stage 1), so the tail-clause counter check
    // reads the BEARER, never the exiled Bankbuster.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_ReckonerBankbuster_DrawToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var bankbuster = ReckonerBankbusterFactory.Create(alice);
        var realAbilities = bankbuster.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        realAbilities.Should().ContainSingle(
            "Reckoner Bankbuster has exactly one non-mana activated ability — the {T}, remove-counter draw");
        realAbilities.Should().OnlyContain(a => a.RebindSafe,
            "the migrated Bankbuster ability reads ResolutionContext.Source and is RebindSafe");
        alice.Zones.Graveyard.AddCard(bankbuster);
        bankbuster.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);
        // Give the bearer a charge counter so the re-homed cost can remove one
        // and the tail-clause "no charge counters remain" branch is exercised.
        bearer.Counters.Add(CounterType.Charge, 1);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), bankbuster);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "Bankbuster's real draw ability is re-homed via RebindTo");
        var draw = granted[0];
        draw.Source.Should().BeSameAs(bearer,
            "the re-homed draw is sourced on the BEARER (CR 707.2)");
        draw.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");
        draw.Costs.OfType<RemoveChargeCounterCost>().Should().ContainSingle(
            "the remove-charge-counter cost is re-homed to the bearer via IRebindableCost (Stage 1)");

        // Resolving the re-homed draw draws for the BEARER's controller, never
        // the exiled Bankbuster's owner via a captured closure.
        var top = new Card("Island", "");
        top.SetOwner(alice);
        alice.Zones.Library.AddCard(top);
        await draw.ResolveAsync(agent: null, game: null);
        alice.Zones.Hand.GetCards().Should().Contain(top,
            "the re-homed draw reads the source's controller (the bearer's controller)");
    }

    [Fact]
    public async Task BespokeBankbusterDraw_ResolvesOnOwnSourceWhenNotRebound()
    {
        // Sanity: the migrated draw still draws for its own source's controller
        // on the normal (un-rebound) resolution path.
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var zones = new Majik.Core.Services.ZoneService(bus);

        var bankbuster = ReckonerBankbusterFactory.Create(alice);
        alice.Zones.Library.AddCard(bankbuster);
        zones.MoveCard(bankbuster, ZoneType.Library, ZoneType.Battlefield, alice);

        var draw = bankbuster.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        var top = new Card("Plains", "");
        top.SetOwner(alice);
        alice.Zones.Library.AddCard(top);
        await draw.ResolveAsync(agent: null, game: null);
        alice.Zones.Hand.GetCards().Should().Contain(top,
            "the un-rebound draw draws for its own source's controller");
    }

    // -----------------------------------------------------------------------
    // agatha-bespoke-factory-resolutioncontext-source-migration-utility-batch
    // — three more bespoke [CardName]-factory activated abilities migrated to
    // read ResolutionContext.Source / .Controller / .ChosenTargets + marked
    // RebindSafe so Agatha's group-grant re-homes the REAL ability (incl. its
    // bespoke cost the oracle-rebuild fallback cannot reconstruct) onto a
    // counter-bearing bearer via ActivatedAbility.RebindTo (CR 707.2).
    //   - Vexing Shusher: "{R/G}: Target spell can't be countered."
    //   - Insolent Neonate: "Discard a card, Sacrifice this creature: Draw."
    //   - Mausoleum Wanderer: "Sacrifice ~: Counter target instant/sorcery
    //     unless its controller pays {X}, where X is THIS CREATURE's power."
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_VexingShusher_ToBearer()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its
        // "{R/G}: target spell can't be countered" ability is now RebindSafe.
        var shusher = VexingShusherFactory.Create(alice);
        shusher.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility)
            .RebindSafe.Should().BeTrue(
                "the migrated Vexing Shusher grant reads ResolutionContext targets and is RebindSafe");
        alice.Zones.Graveyard.AddCard(shusher);
        shusher.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), shusher);

        var grant = GrantedActivated(bearer).Single(a =>
            a.TargetRequests.Any(t => t.Description.Contains("target spell")));
        grant.Source.Should().BeSameAs(bearer, "the grant is re-homed to the BEARER (CR 707.2)");
        grant.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");

        // Bob casts a counterable instant. The re-homed grant stamps it
        // uncounterable (the grant targets a spell, not the source — re-home
        // affects the chosen spell regardless of which permanent is the source).
        var bolt = new Majik.Core.Cards.Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(bob);
        bolt.SetController(bob);
        bolt.SetZone(ZoneType.Stack);
        var stack = new Majik.Core.Stack.Stack(bus);
        var boltSpell = new Majik.Core.Spells.Spell(bolt, bob);
        stack.Push(boltSpell);
        boltSpell.CannotBeCountered.Should().BeFalse("counterable before the grant resolves");

        grant.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { boltSpell } });
        var game = new Majik.Core.Game.GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: null,
            stack: stack);
        await grant.ResolveAsync(agent: null, game: game);

        boltSpell.CannotBeCountered.Should().BeTrue(
            "the re-homed grant stamps the chosen spell uncounterable (CR 701.5b)");
    }

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_InsolentNeonate_ToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its
        // "Discard a card, Sacrifice this creature: Draw a card" ability is now
        // RebindSafe (its bespoke DiscardACardCost is reused verbatim by
        // RebindTo; the sacrifice cost re-homes via AdditionalCost.RebindSource).
        var neonate = InsolentNeonateFactory.Create(alice);
        neonate.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility)
            .RebindSafe.Should().BeTrue(
                "the migrated Insolent Neonate ability reads ResolutionContext.Source/.Controller and is RebindSafe");
        alice.Zones.Graveyard.AddCard(neonate);
        neonate.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), neonate);

        var draw = GrantedActivated(bearer).Single(a =>
            a.Costs.OfType<Majik.Core.Costs.DiscardACardCost>().Any());
        draw.Source.Should().BeSameAs(bearer, "the ability is re-homed to the BEARER (CR 707.2)");
        draw.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");

        // STAGE 1 — the Sacrifice cost re-homes to the BEARER.
        draw.Costs.OfType<Majik.Core.Costs.AdditionalCost>()
            .Single(c => c.CostType == Majik.Core.Costs.AdditionalCostType.Sacrifice)
            .Description.Should().Contain(bearer.Name,
                "the sacrifice cost re-homes to the bearer (AdditionalCost.RebindSource)");

        // Resolving the re-homed ability sacrifices the BEARER and draws for the
        // bearer's controller — never the exiled Insolent Neonate.
        var top = new Card("Plains", "");
        top.SetOwner(alice);
        alice.Zones.Library.AddCard(top);

        bearer.Zone.Should().Be(ZoneType.Battlefield, "the bearer is alive before resolution");
        await draw.ResolveAsync(agent: null, game: null);

        bearer.Zone.Should().Be(ZoneType.Graveyard,
            "the re-homed ability sacrifices the BEARER (ResolutionContext.Source), not the exiled Neonate");
        neonate.Zone.Should().Be(ZoneType.Exile,
            "the imprinted Neonate stays in exile under the Cauldron, untouched by the re-homed sac");
        alice.Zones.Hand.GetCards().Should().Contain(top,
            "the re-homed draw draws for the BEARER's controller (ResolutionContext.Controller)");
    }

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_MausoleumWanderer_ToBearer_XReadsBearerPower()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its
        // "Sacrifice ~: counter unless pay X" ability is now RebindSafe (the
        // sacrifice cost re-homes via RebindSource; X reads the source's power).
        var wanderer = MausoleumWandererFactory.Create(alice);
        wanderer.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility)
            .RebindSafe.Should().BeTrue(
                "the migrated Mausoleum Wanderer ability reads ResolutionContext.Source and is RebindSafe");
        alice.Zones.Graveyard.AddCard(wanderer);
        wanderer.SetZone(ZoneType.Graveyard);

        // Bearer is a 4/4 base (+1/+1 counter = 5/5) — so X reads the BEARER's
        // power (5), NOT the exiled Wanderer's printed power (1).
        var bearer = SeatedBearer(alice, effects, zones, power: 4, toughness: 4);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), wanderer);

        var counter = GrantedActivated(bearer).Single(a =>
            a.TargetRequests.Any(t => t.Description.Contains("instant or sorcery")));
        counter.Source.Should().BeSameAs(bearer, "the ability is re-homed to the BEARER (CR 707.2)");

        // Bob casts a sorcery; he has only {2} — LESS than the BEARER's power
        // (5), so he cannot pay X and the spell is countered. (If X read the
        // exiled Wanderer's printed power 1, Bob's {2} would cover it and the
        // spell would survive — this asserts X = the BEARER's power.)
        bob.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(2));
        var sorcery = new Majik.Core.Cards.Sorcery("Big Spell", "{4}{B}");
        sorcery.SetOwner(bob);
        sorcery.SetController(bob);
        sorcery.SetZone(ZoneType.Stack);
        var stack = new Majik.Core.Stack.Stack(bus);
        var stackSpell = new Majik.Core.Spells.Spell(sorcery, bob);
        stack.Push(stackSpell);

        counter.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { stackSpell } });
        var game = new Majik.Core.Game.GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: null,
            stack: stack);
        await counter.ResolveAsync(agent: null, game: game);

        // The BEARER is sacrificed (ResolutionContext.Source), not the Wanderer.
        bearer.Zone.Should().Be(ZoneType.Graveyard,
            "the re-homed ability sacrifices the BEARER (CR 701.16)");

        // X = the BEARER's power (5) > Bob's {2}, so the spell is countered.
        stack.GetAll().Should().NotContain(stackSpell,
            "X reads the BEARER's power (5), which Bob's {2} cannot pay → countered (CR 701.5)");
        sorcery.Zone.Should().Be(ZoneType.Graveyard,
            "the countered spell goes to its owner's graveyard");
    }

    // -----------------------------------------------------------------------
    // Bespoke-factory migration batch (agatha-bespoke-migration-discard-self-
    // impulse-and-free-counter-batch / item #5). Boromir's "Sacrifice Boromir:
    // Creatures you control gain indestructible until end of turn." sac-self
    // activated ability now reads ResolutionContext.Source / .Controller and
    // marks the ability RebindSafe, so Agatha's group-grant re-homes the REAL
    // ability — including its self-sacrifice cost (SacrificeSelfCost, now an
    // IRebindableCost) — onto a counter-bearing bearer via RebindTo
    // (CR 707.2 / 613.1f). The re-homed ability sacrifices the BEARER and grants
    // the BEARER's controller's creatures indestructible — never the exiled
    // Boromir.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_Boromir_SacGrantToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its
        // "Sacrifice Boromir: Creatures you control gain indestructible until
        // end of turn. The Ring tempts you." ability is now RebindSafe (the
        // self-sacrifice cost re-homes via IRebindableCost; the grant reads
        // ResolutionContext.Source / .Controller).
        var boromir = BoromirWardenOfTheTowerFactory.Create(alice);
        boromir.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility
                && a.Costs.OfType<Majik.Core.Costs.SacrificeSelfCost>().Any())
            .RebindSafe.Should().BeTrue(
                "the migrated Boromir sac ability reads ResolutionContext.Source/.Controller and is RebindSafe");
        alice.Zones.Graveyard.AddCard(boromir);
        boromir.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), boromir);

        var sac = GrantedActivated(bearer).Single(a =>
            a.Costs.OfType<Majik.Core.Costs.SacrificeSelfCost>().Any());
        sac.Source.Should().BeSameAs(bearer, "the ability is re-homed to the BEARER (CR 707.2)");
        sac.RebindSafe.Should().BeTrue("RebindTo preserves the re-source-safe provenance");

        // STAGE 1 — the SacrificeSelfCost re-homes to the BEARER (IRebindableCost).
        sac.Costs.OfType<Majik.Core.Costs.SacrificeSelfCost>()
            .Single().Self.Should().BeSameAs(bearer,
                "the self-sacrifice cost re-homes to the bearer (IRebindableCost.RebindTo)");

        // A second creature the BEARER's controller controls — should receive
        // the indestructible grant when the re-homed ability resolves.
        var other = new Creature("Other Soldier", "{W}", 1, 1);
        other.SetOwner(alice);
        other.ChangeController(alice);
        other.SetZone(ZoneType.Battlefield);
        other.ActiveEffects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        alice.Zones.Battlefield.AddCard(other);

        // Resolve the re-homed ability through a live GameContext (Source = bearer).
        var game = new Majik.Core.Game.GameContext(
            self: alice,
            allPlayers: new[] { alice },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(bus));
        await sac.ResolveAsync(agent: null, game: game);

        // The grant affects the BEARER's controller's creatures (CR 613.1f).
        Majik.Core.Combat.CombatAbilities.HasIndestructible(other).Should().BeTrue(
            "the re-homed ability grants the BEARER's controller's creatures indestructible");
        boromir.Zone.Should().Be(ZoneType.Exile,
            "the imprinted Boromir stays untouched in exile under the Cauldron");
    }

    // -----------------------------------------------------------------------
    // agatha-bespoke-migration-worldbreaker-audit-grep-tail — Heliod,
    // Sun-Crowned, a bespoke [CardName]-factory creature from the audit-grep
    // tail, migrated to read ResolutionContext.Source + marked RebindSafe:
    //   - Heliod, Sun-Crowned — "{1}{W}: Another target creature gains lifelink
    //     until end of turn." Non-reconstructable from oracle text; "Another"
    //     now measures against ResolutionContext.Source (the BEARER), not the
    //     exiled Heliod, so Agatha re-homes the REAL ability via RebindTo.
    // (Joraga Treespeaker — the other audit-grep-tail card — was migrated in a
    //  sibling PR; its re-home test lives above.)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grant_RebindsBespokeFactoryCreature_Heliod_LifelinkGrantToBearer()
    {
        var alice = new Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        // A REAL bespoke [CardName]-factory creature in the graveyard. Its sole
        // non-mana activated ability (the {1}{W} lifelink grant) is now
        // RebindSafe (the "Another" check reads ResolutionContext.Source).
        var heliod = HeliodSunCrownedFactory.Create(alice);
        var realAbilities = heliod.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        realAbilities.Should().ContainSingle(
            "Heliod has exactly one non-mana activated ability — the {1}{W} lifelink grant");
        realAbilities.Should().OnlyContain(a => a.RebindSafe,
            "the migrated Heliod ability reads ResolutionContext.Source and is RebindSafe");
        alice.Zones.Graveyard.AddCard(heliod);
        heliod.SetZone(ZoneType.Graveyard);

        var bearer = SeatedBearer(alice, effects, zones);

        var cauldron = GrantingCauldron(alice, effects, bus, OracleStub());
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);

        Resolve(TapAbility(cauldron), heliod);

        var granted = GrantedActivated(bearer);
        granted.Should().ContainSingle(
            "Heliod's real lifelink grant is re-homed via RebindTo");
        var lifelinkGrant = granted[0];
        lifelinkGrant.Source.Should().BeSameAs(bearer,
            "the re-homed lifelink grant is sourced on the BEARER (CR 707.2)");
        lifelinkGrant.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");

        // Another creature the BEARER's controller controls — the legal
        // "Another target creature" for the re-homed grant.
        var ally = new Creature("Ally Soldier", "{W}", 1, 1);
        ally.SetOwner(alice);
        ally.ChangeController(alice);
        ally.ActiveEffects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        alice.Zones.Library.AddCard(ally);
        zones.MoveCard(ally, ZoneType.Library, ZoneType.Battlefield, alice);

        lifelinkGrant.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ally } });
        await lifelinkGrant.ResolveAsync(agent: null, game: null);

        ally.ActiveEffects.Compute(ally).Keywords
            .Any(k => string.Equals(k, "Lifelink", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue(
                "the re-homed grant gives the chosen 'Another' creature Lifelink");

        // "Another" now excludes the BEARER (ResolutionContext.Source), not the
        // exiled Heliod — targeting the bearer itself grants nothing.
        bearer.ActiveEffects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        lifelinkGrant.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bearer } });
        await lifelinkGrant.ResolveAsync(agent: null, game: null);
        bearer.ActiveEffects.Compute(bearer).Keywords
            .Any(k => string.Equals(k, "Lifelink", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeFalse(
                "'Another' is measured against the BEARER (CR 707.2 / 608.2b)");
    }
}
