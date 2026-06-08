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
}
