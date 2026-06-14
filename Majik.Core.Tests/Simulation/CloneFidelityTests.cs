using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Simulation;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Simulation;

public sealed class CloneFidelityTests
{
    [Fact]
    public void Clone_CopiesCardsIntoZones_PreservingInstanceIdAndOrder()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Grizzly Bears", manaCost: "{1}{G}", power: 2, toughness: 2);
        bear.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(bear);

        var cloned = GameStateCloner.Clone(new[] { alice });
        var cAlice = cloned.PlayerFor(alice);

        var cBear = cAlice.Zones.Battlefield.GetCards().Single();   // real zone-read accessor
        cBear.Should().NotBeSameAs(bear);
        cBear.InstanceId.Should().Be(bear.InstanceId);
        cBear.Name.Should().Be("Grizzly Bears");
        cloned.CardMap[bear.InstanceId].Should().BeSameAs(cBear);
    }
    [Fact]
    public void Clone_CopiesLife_AndIsIndependent()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 17);

        var cloned = GameStateCloner.Clone(new[] { alice, bob });

        var cAlice = cloned.PlayerFor(alice);
        var cBob = cloned.PlayerFor(bob);
        cAlice.LifeTotal.Should().Be(20);
        cBob.LifeTotal.Should().Be(17);

        // Independence: mutating the clone must not touch the original.
        cAlice.SetLifeTotal(5);
        alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Clone_CopiesPlayerScalarState()
    {
        var alice = new Player("Alice", 20);
        alice.GainEnergy(3);                              // EnergyCounters
        alice.AddPoisonCounters(2);                       // PoisonCounters
        alice.AddManaToPool(ManaCost.Parse("{R}{R}"));    // _manaPool (Red = 2)

        var cloned = GameStateCloner.Clone(new[] { alice });
        var c = cloned.PlayerFor(alice);

        c.EnergyCounters.Should().Be(3);
        c.PoisonCounters.Should().Be(2);
        c.ManaPool.Red.Should().Be(2);

        // Independence: mutating the clone must not touch the original.
        c.GainEnergy(10);
        alice.EnergyCounters.Should().Be(3);
    }

    [Fact]
    public void Clone_CopiesPermanentBoardState()
    {
        // Arrange: a creature with tap state, damage, a counter, and no summoning sickness.
        var alice = new Player("Alice", 20);
        var bear = new Creature("Grizzly Bears", manaCost: "{1}{G}", power: 2, toughness: 2);
        bear.ChangeOwner(alice);
        bear.ClearSummoningSickness();          // clear the default sick flag
        alice.Zones.Battlefield.AddCard(bear);
        bear.Tap();                             // IsTapped = true
        bear.TakeDamage(1);                     // Damage = 1
        bear.Counters.Add(CounterType.PlusOnePlusOne, 1); // one +1/+1 counter

        // Act: clone
        var cloned = GameStateCloner.Clone(new[] { alice });
        var cAlice = cloned.PlayerFor(alice);
        var cBear = (Creature)cAlice.Zones.Battlefield.GetCards().Single();

        // Assert: clone carries the board state
        cBear.IsTapped.Should().BeTrue();
        cBear.Damage.Should().Be(1);
        cBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        cBear.HasSummoningSickness.Should().BeFalse();

        // Independence: mutating the clone must not touch the original
        cBear.Untap();
        bear.IsTapped.Should().BeTrue("original must remain tapped after cloning");
    }

    [Fact]
    public void Clone_RelinksControllerAndAttachments_ToClones()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        var aura = new Enchantment("Holy Strength", "{W}");
        bear.ChangeOwner(alice); aura.ChangeOwner(alice);
        bear.ChangeController(alice); aura.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(bear);
        alice.Zones.Battlefield.AddCard(aura);
        aura.AttachTo(bear);

        var cloned = GameStateCloner.Clone(new[] { alice });
        var cAlice = cloned.PlayerFor(alice);
        var cBear = (Creature)cloned.CardMap[bear.InstanceId];
        var cAura = (Permanent)cloned.CardMap[aura.InstanceId];

        cBear.Controller.Should().BeSameAs(cAlice);           // points at CLONE player
        cBear.Owner.Should().BeSameAs(cAlice);
        cAura.AttachedTo.Should().BeSameAs(cBear);            // attachment remapped to clone
        cBear.Attachments.Should().ContainSingle().Which.Should().BeSameAs(cAura);
    }

    [Fact]
    public void Clone_PreservesRuntimeTypeForEachCardType()
    {
        // Arrange: one of each concrete card type in a zone.
        var alice = new Player("Alice", 20);

        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(creature);

        var land = new Land("Forest");
        land.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(land);

        var artifact = new Artifact("Sol Ring", "{1}");
        artifact.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(artifact);

        var enchantment = new Enchantment("Glorious Anthem", "{1}{W}{W}");
        enchantment.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(enchantment);

        var planeswalker = new Planeswalker("Liliana of the Veil", "{1}{B}{B}", startingLoyalty: 3);
        planeswalker.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(planeswalker);

        var instant = new Instant("Lightning Bolt", "{R}");
        instant.ChangeOwner(alice);
        alice.Zones.Graveyard.AddCard(instant);

        var sorcery = new Sorcery("Cultivate", "{2}{G}");
        sorcery.ChangeOwner(alice);
        alice.Zones.Graveyard.AddCard(sorcery);

        // Act
        var cloned = GameStateCloner.Clone(new[] { alice });
        var cAlice = cloned.PlayerFor(alice);

        var bf = cAlice.Zones.Battlefield.GetCards().ToList();
        var gy = cAlice.Zones.Graveyard.GetCards().ToList();

        // Assert: type preservation
        bf.Should().ContainSingle(c => c.InstanceId == creature.InstanceId)
            .Which.Should().BeOfType<Creature>();
        bf.Should().ContainSingle(c => c.InstanceId == land.InstanceId)
            .Which.Should().BeOfType<Land>();
        bf.Should().ContainSingle(c => c.InstanceId == artifact.InstanceId)
            .Which.Should().BeOfType<Artifact>();
        bf.Should().ContainSingle(c => c.InstanceId == enchantment.InstanceId)
            .Which.Should().BeOfType<Enchantment>();
        bf.Should().ContainSingle(c => c.InstanceId == planeswalker.InstanceId)
            .Which.Should().BeOfType<Planeswalker>();
        gy.Should().ContainSingle(c => c.InstanceId == instant.InstanceId)
            .Which.Should().BeOfType<Instant>();
        gy.Should().ContainSingle(c => c.InstanceId == sorcery.InstanceId)
            .Which.Should().BeOfType<Sorcery>();
    }

    [Fact]
    public void Clone_RelinksRuntimeExileCastAllowedCaster_ToClone()
    {
        // Arrange: alice grants exile-cast permission on a card to bob (Ragavan style).
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.ChangeOwner(alice);
        alice.Zones.Exile.AddCard(bolt);

        // Grant: bob may cast the card from exile for {R}.
        var cost = ManaCost.Parse("{R}");
        bolt.GrantRuntimeExileCast(bob, cost, spendAsAnyColor: false);

        // Act: clone with both players.
        var cloned = GameStateCloner.Clone(new[] { alice, bob });
        var cBob = cloned.PlayerFor(bob);
        var cBolt = (Instant)cloned.CardMap[bolt.InstanceId];

        // Assert: the cloned card's allowed caster is the CLONED bob, not the original.
        cBolt.RuntimeExileCastAllowedCaster.Should().BeSameAs(cBob,
            "RelinkReferences must remap the exile-cast allowed caster to the cloned player");
        cBolt.RuntimeExileCastAllowedCaster.Should().NotBeSameAs(bob,
            "the original player reference must not survive the clone boundary");

        // Companion fields must be preserved.
        cBolt.RuntimeExileCastCost.Should().Be(cost);
        cBolt.RuntimeExileCastSpendAsAnyColor.Should().BeFalse();
    }

    [Fact]
    public void Clone_PreservesBackFaceLoyaltyAbilities_OnFlippedToPlaneswalkerBack()
    {
        // CR 711 / 606 — a creature-front transform DFC whose BACK face is a
        // planeswalker (Ral, Monsoon Mage // Ral, Leyline Prodigy) is a Creature
        // C# instance, NOT a Planeswalker. Flipping to the back attaches the
        // back face's loyalty abilities through the Permanent-typed loyalty
        // surface and records them in the detach ledger so a flip-BACK detaches
        // exactly those. The sim clone (CloneForSim) must carry that ledger
        // across the clone boundary, or a flip-back inside the MCTS sandbox
        // would fail to detach them. (Pairs with the _mdfcState sim deferral —
        // same clone-omission class.)
        var alice = new Player("Alice", 20);
        var ral = new Creature("Ral, Monsoon Mage", "{1}{R}", 1, 3);
        ral.ChangeOwner(alice);
        ral.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(ral);

        // Attach the planeswalker back face with loyalty + oracle text so the
        // transform path binds + records the loyalty abilities.
        ral.MdfcState = new MdfcState(
            "Ral, Monsoon Mage",
            "Ral, Leyline Prodigy",
            new BackFaceCharacteristics(
                name: "Ral, Leyline Prodigy",
                isCreature: false,
                power: 0,
                toughness: 0,
                types: new[] { CardType.Planeswalker },
                subtypes: new[] { CardSubtype.Ral },
                supertypes: new[] { CardSupertype.Legendary },
                colors: new[] { ManaColor.Blue, ManaColor.Red },
                loyalty: 2,
                oracleText: "+1: Draw a card.\n-3: You gain 3 life."));

        ral.MdfcState!.Transform(); // → back face: ledger populated

        ral.BackFaceLoyaltyAbilities.Should().HaveCount(2,
            "the back face has two loyalty-cost lines (sanity: ledger populated pre-clone)");

        // Act: clone for sim.
        var cClone = (Creature)ral.CloneForSim();

        // Assert: the detach ledger survives the clone boundary (shared refs,
        // same posture as _abilities).
        cClone.BackFaceLoyaltyAbilities.Should().HaveCount(2,
            "CloneForSim must copy the back-face loyalty-ability detach ledger");
        cClone.BackFaceLoyaltyAbilities.Should().BeEquivalentTo(
            ral.BackFaceLoyaltyAbilities,
            "the ledger shares the same LoyaltyAbility refs as the source (definition-data posture)");

        // And those same abilities are reachable through the clone's ability list.
        cClone.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void Clone_CopiesMdfcState_PreservingActiveFace()
    {
        // CR 711 — a transform DFC's face tracker (MdfcState) must travel across
        // the sim clone boundary so the MCTS determinized search can observe the
        // active face AND flip it. Previously _mdfcState was nulled on the clone,
        // so a sim clone could neither see "is this transformed?" nor transform.
        var alice = new Player("Alice", 20);
        var delver = new Creature("Delver of Secrets", "{U}", 1, 1);
        delver.ChangeOwner(alice);
        delver.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(delver);

        delver.MdfcState = new MdfcState(
            "Delver of Secrets",
            "Insectile Aberration",
            BackFaceCharacteristics.Creature(
                name: "Insectile Aberration",
                power: 3,
                toughness: 2,
                keywords: new[] { "Flying" },
                colors: new[] { ManaColor.Blue }));
        delver.MdfcState!.Transform(); // → back face up

        var cClone = (Creature)delver.CloneForSim();

        cClone.MdfcState.Should().NotBeNull("CloneForSim must thread MdfcState onto the clone");
        cClone.MdfcState!.IsBackFace.Should().BeTrue("the clone carries the same active (back) face");
        cClone.MdfcState.ActiveFaceName.Should().Be("Insectile Aberration");
        cClone.MdfcState.BackFaceCharacteristics.Should()
            .BeSameAs(delver.MdfcState!.BackFaceCharacteristics,
                "immutable back-face definition data is shared by reference");

        // The cloned state must be a DISTINCT instance (so flipping the clone
        // does not flip the original — sandbox independence).
        cClone.MdfcState.Should().NotBeSameAs(delver.MdfcState);
    }

    [Fact]
    public void Clone_MdfcState_CanTransformInSandbox_WithoutTouchingOriginal()
    {
        // CR 711 — the cloned face tracker must support a working Transform()
        // whose OnTransformed callback fires against the CLONE permanent, not the
        // original (the source state's callback closes over the original). The
        // search must be able to model flipping an MDFC inside the sandbox.
        var alice = new Player("Alice", 20);
        var delver = new Creature("Delver of Secrets", "{U}", 1, 1);
        delver.ChangeOwner(alice);
        delver.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(delver);

        delver.MdfcState = new MdfcState(
            "Delver of Secrets",
            "Insectile Aberration",
            BackFaceCharacteristics.Creature(
                name: "Insectile Aberration",
                power: 3,
                toughness: 2,
                keywords: new[] { "Flying" },
                colors: new[] { ManaColor.Blue }));
        // Original starts on the FRONT face.

        var cClone = (Creature)delver.CloneForSim();
        cClone.MdfcState!.IsBackFace.Should().BeFalse("clone starts on the same (front) face");

        // Flip the clone inside the sandbox.
        cClone.MdfcState.Transform();

        cClone.MdfcState.IsBackFace.Should().BeTrue("the clone's Transform() flips its own face");
        delver.MdfcState!.IsBackFace.Should().BeFalse(
            "flipping the clone must NOT transform the original (sandbox independence)");
    }

    [Fact]
    public void Clone_CopiesStackObjects_TargetingClones()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.ChangeOwner(alice);
        alice.Zones.Hand.AddCard(bolt);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        // Build spell targeting Bob (a Player target) and push onto the stack.
        var spell = new Majik.Core.Spells.Spell(bolt, alice, new[] { Target.Player(bob) });
        stack.Push(spell);

        var cloned = GameStateCloner.Clone(new[] { alice, bob }, stack);

        // The cloned stack must carry exactly one object.
        cloned.Stack.Should().NotBeNull();
        cloned.Stack!.Count.Should().Be(1);

        // The top of the cloned stack must be a Spell.
        var top = cloned.Stack.Top as Majik.Core.Spells.Spell;
        top.Should().NotBeNull();

        // The spell's source card must be the CLONE of bolt (not the original).
        top!.Card.Should().BeSameAs(cloned.CardMap[bolt.InstanceId]);

        // The spell's controller must be the CLONE of alice.
        top.Controller.Should().BeSameAs(cloned.PlayerFor(alice));

        // The spell's target must be the CLONE of bob (retargeted to the clone).
        top.Targets.Should().HaveCount(1);
        var targetPlayer = ((Target)top.Targets[0]).GetPlayer();
        targetPlayer.Should().BeSameAs(cloned.PlayerFor(bob));
    }
}
