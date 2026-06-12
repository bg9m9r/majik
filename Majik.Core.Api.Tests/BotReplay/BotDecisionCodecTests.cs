using FluentAssertions;
using Majik.Core.Api.BotReplay;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Simulation;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Moq;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Api.Tests.BotReplay;

/// <summary>
/// Bot-decision persistence — per-kind codec round-trips. Every recorded bot
/// answer is encoded to an Id-level payload (InstanceId for cards, Player.Id
/// for players, scalars verbatim — NEVER live object references) and decoded
/// against a REBUILT facade (modelled here by <see cref="GameStateCloner"/>
/// clones, which preserve InstanceIds), resolving back to equivalent objects.
/// </summary>
public class BotDecisionCodecTests
{
    // -------------------------------------------------------------------
    // The three pattern-pinning kinds (plan Task 1 Step 1).
    // -------------------------------------------------------------------

    [Fact]
    public void Priority_Pass_RoundTrips()
    {
        var (self, opp) = BuildPlayers();
        var ctx = Ctx(self, opp);

        var payload = BotDecisionCodec.EncodePriority(PriorityAction.Pass);

        AssertIdLevelOnly(payload);
        var decoded = BotDecisionCodec.DecodePriority(payload, ctx, self);
        decoded.Should().BeOfType<PriorityAction.PassAction>();
    }

    [Fact]
    public void Blockers_RoundTrips_ByInstanceIds_AgainstClone()
    {
        var (self, opp) = BuildPlayers();
        var attacker = SeedCreature(opp, "Raging Goblin", 1, 1);
        var blocker = SeedCreature(self, "Grizzly Bears", 2, 2);

        var plan = new BlockPlan(new[] { new BlockerDeclaration(blocker, attacker) });
        var payload = BotDecisionCodec.EncodeBlockers(plan);
        AssertIdLevelOnly(payload);

        // Decode against a CLONE of the players (InstanceIds preserved,
        // object identities fresh) — models the rebuilt facade.
        var clone = GameStateCloner.Clone(new[] { self, opp });
        var cloneSelf = clone.Players[0];
        var cloneOpp = clone.Players[1];
        var cloneAttackers = cloneOpp.Zones.Battlefield.GetCards().OfType<Creature>().ToList();
        var cloneBlockers = cloneSelf.Zones.Battlefield.GetCards().OfType<Creature>().ToList();

        var decoded = BotDecisionCodec.DecodeBlockers(payload, cloneAttackers, cloneBlockers);

        decoded.Blockers.Should().HaveCount(1);
        decoded.Blockers[0].Blocker.InstanceId.Should().Be(blocker.InstanceId);
        decoded.Blockers[0].Attacker.InstanceId.Should().Be(attacker.InstanceId);
        // And they are the CLONE's objects, not the originals.
        ReferenceEquals(decoded.Blockers[0].Blocker, blocker).Should().BeFalse();
    }

    [Fact]
    public void Priority_CastSpell_WithExileAltCostAndTargets_RoundTrips()
    {
        var (self, opp) = BuildPlayers();
        var spell = SeedExiled(self, "Lightning Bolt");
        var creatureTarget = SeedCreature(opp, "Grizzly Bears", 2, 2);

        var altCost = new ExileCastAlternativeCost(
            "Cast Lightning Bolt from exile (R)", ManaCost.Parse("R"));
        var action = new PriorityAction.CastSpell(
            spell,
            new object[] { creatureTarget, opp },
            HoldPriority: true,
            AlternativeCost: altCost);

        var payload = BotDecisionCodec.EncodePriority(action);
        AssertIdLevelOnly(payload);

        // Decode the card + creature target against a clone; the player
        // target resolves by Player.Id (clones mint fresh player ids, so we
        // decode against the live players — production rehydrate reproduces
        // Player.Id via DeterministicIdScope).
        var ctx = Ctx(self, opp);
        var decoded = BotDecisionCodec.DecodePriority(payload, ctx, self)
            .Should().BeOfType<PriorityAction.CastSpell>().Subject;

        decoded.Card.InstanceId.Should().Be(spell.InstanceId);
        decoded.HoldPriority.Should().BeTrue();
        decoded.Targets.Should().HaveCount(2);
        decoded.Targets[0].Should().BeAssignableTo<ICard>()
            .Which.InstanceId.Should().Be(creatureTarget.InstanceId);
        decoded.Targets[1].Should().BeAssignableTo<Player>()
            .Which.Id.Should().Be(opp.Id);
        var decodedAlt = decoded.AlternativeCost.Should()
            .BeOfType<ExileCastAlternativeCost>().Subject;
        decodedAlt.Description.Should().Be(altCost.Description);
        decodedAlt.AlternativeManaCost.Should().Be(altCost.AlternativeManaCost);
    }

    [Fact]
    public void Priority_CastSpell_WithUnknownAltCost_ThrowsAtEncode()
    {
        var (self, _) = BuildPlayers();
        var spell = SeedCreature(self, "Grizzly Bears", 2, 2);
        var exotic = new Mock<IAlternativeCost>();
        exotic.SetupGet(c => c.Description).Returns("exotic");
        exotic.SetupGet(c => c.AlternativeManaCost).Returns(ManaCost.Parse("1"));

        var action = new PriorityAction.CastSpell(
            spell, Array.Empty<object>(), AlternativeCost: exotic.Object);

        var act = () => BotDecisionCodec.EncodePriority(action);
        act.Should().Throw<UnsupportedBotDecisionException>();
    }

    [Fact]
    public void Priority_CastSpell_WithMultikickerAdditionalCost_RoundTrips()
    {
        var (self, opp) = BuildPlayers();
        var chalice = SeedHand(self, "Everflowing Chalice");

        var action = new PriorityAction.CastSpell(
            chalice,
            Array.Empty<object>(),
            AdditionalCosts: new IAdditionalCost[]
            {
                new MultikickerAdditionalCost(chalice, ManaCost.Parse("2"), times: 3),
            });

        var payload = BotDecisionCodec.EncodePriority(action);
        AssertIdLevelOnly(payload);

        var ctx = Ctx(self, opp);
        var decoded = BotDecisionCodec.DecodePriority(payload, ctx, self)
            .Should().BeOfType<PriorityAction.CastSpell>().Subject;
        decoded.AdditionalCosts.Should().ContainSingle()
            .Which.Should().BeOfType<MultikickerAdditionalCost>();
    }

    // -------------------------------------------------------------------
    // Remaining kinds — scalar / id-list round-trips.
    // -------------------------------------------------------------------

    [Fact]
    public void Priority_PlayLand_RoundTrips()
    {
        var (self, opp) = BuildPlayers();
        var land = SeedHand(self, "Forest", land: true);

        var payload = BotDecisionCodec.EncodePriority(new PriorityAction.PlayLand(land));
        AssertIdLevelOnly(payload);

        var decoded = BotDecisionCodec.DecodePriority(payload, Ctx(self, opp), self)
            .Should().BeOfType<PriorityAction.PlayLand>().Subject;
        decoded.Land.InstanceId.Should().Be(land.InstanceId);
    }

    [Fact]
    public void Mulligan_RoundTrips()
    {
        var payload = BotDecisionCodec.EncodeMulligan(MulliganDecision.Mulligan);
        AssertIdLevelOnly(payload);
        BotDecisionCodec.DecodeMulligan(payload).Should().Be(MulliganDecision.Mulligan);
    }

    [Fact]
    public void CardsToBottom_RoundTrips_AgainstHandList()
    {
        var (self, _) = BuildPlayers();
        var c1 = SeedHand(self, "Forest", land: true);
        var c2 = SeedHand(self, "Grizzly Bears");
        var hand = self.Zones.Hand.GetCards().ToList();

        var payload = BotDecisionCodec.EncodeCardsToBottom(new[] { c2, c1 });
        AssertIdLevelOnly(payload);

        var decoded = BotDecisionCodec.DecodeCardsToBottom(payload, hand);
        decoded.Select(c => c.InstanceId).Should().Equal(c2.InstanceId, c1.InstanceId);
    }

    [Fact]
    public void Targets_RoundTrips_CardsAndPlayers()
    {
        var (self, opp) = BuildPlayers();
        var creature = SeedCreature(opp, "Grizzly Bears", 2, 2);

        var payload = BotDecisionCodec.EncodeTargets(new object[] { creature, opp });
        AssertIdLevelOnly(payload);

        var decoded = BotDecisionCodec.DecodeTargets(payload, Ctx(self, opp));
        decoded.Should().HaveCount(2);
        decoded[0].Should().BeAssignableTo<ICard>()
            .Which.InstanceId.Should().Be(creature.InstanceId);
        decoded[1].Should().BeAssignableTo<Player>().Which.Id.Should().Be(opp.Id);
    }

    [Fact]
    public void X_And_Mode_RoundTrip()
    {
        BotDecisionCodec.DecodeX(BotDecisionCodec.EncodeX(7)).Should().Be(7);
        BotDecisionCodec.DecodeMode(BotDecisionCodec.EncodeMode(2)).Should().Be(2);
    }

    [Fact]
    public void TriggerOrder_RoundTrips_ByAbilityId_AgainstPresentedList()
    {
        var t1 = new Mock<Majik.Core.Abilities.ITriggeredAbility>();
        t1.SetupGet(t => t.Id).Returns(Guid.NewGuid());
        var t2 = new Mock<Majik.Core.Abilities.ITriggeredAbility>();
        t2.SetupGet(t => t.Id).Returns(Guid.NewGuid());
        var mine = new[] { t1.Object, t2.Object };

        // Bot chose reversed order.
        var payload = BotDecisionCodec.EncodeTriggerOrder(new[] { t2.Object, t1.Object });
        AssertIdLevelOnly(payload);

        var decoded = BotDecisionCodec.DecodeTriggerOrder(payload, mine);
        decoded.Select(t => t.Id).Should().Equal(t2.Object.Id, t1.Object.Id);
    }

    [Fact]
    public void ManaSources_RoundTrips_IncludingCancelledSentinel()
    {
        var (self, opp) = BuildPlayers();
        var forest = SeedLand(self, "Forest");

        var payload = BotDecisionCodec.EncodeManaSources(new ManaPayment(new ICard[] { forest }));
        AssertIdLevelOnly(payload);
        var decoded = BotDecisionCodec.DecodeManaSources(payload, Ctx(self, opp));
        decoded.IsCancelled.Should().BeFalse();
        decoded.Sources.Should().ContainSingle().Which.InstanceId.Should().Be(forest.InstanceId);

        var cancelled = BotDecisionCodec.DecodeManaSources(
            BotDecisionCodec.EncodeManaSources(ManaPayment.Cancelled), Ctx(self, opp));
        cancelled.IsCancelled.Should().BeTrue();
    }

    [Fact]
    public void Attackers_RoundTrips_PlayerAndPlaneswalkerDefenders()
    {
        var (self, opp) = BuildPlayers();
        var attacker = SeedCreature(self, "Raging Goblin", 1, 1);

        var plan = new CombatPlan(new[] { new AttackerDeclaration(attacker, opp) });
        var payload = BotDecisionCodec.EncodeAttackers(plan);
        AssertIdLevelOnly(payload);

        var decoded = BotDecisionCodec.DecodeAttackers(
            payload, Ctx(self, opp), new[] { attacker });
        decoded.Attackers.Should().ContainSingle();
        decoded.Attackers[0].Attacker.InstanceId.Should().Be(attacker.InstanceId);
        decoded.Attackers[0].DefendingPlayerOrPlaneswalker.Should().BeAssignableTo<Player>()
            .Which.Id.Should().Be(opp.Id);
    }

    [Fact]
    public void Scry_And_Surveil_RoundTrip_AgainstPeekedList()
    {
        var (self, _) = BuildPlayers();
        var top = SeedLibrary(self, "Forest", land: true);
        var second = SeedLibrary(self, "Grizzly Bears");
        var peeked = new ICard[] { top, second };

        var scry = BotDecisionCodec.DecodeScry(
            BotDecisionCodec.EncodeScry(new ScryAction.ScryDecision(
                ToBottom: new[] { top }, TopOrder: new[] { second })),
            peeked);
        scry.ToBottom.Should().ContainSingle().Which.InstanceId.Should().Be(top.InstanceId);
        scry.TopOrder.Should().ContainSingle().Which.InstanceId.Should().Be(second.InstanceId);

        var surveil = BotDecisionCodec.DecodeSurveil(
            BotDecisionCodec.EncodeSurveil(new SurveilAction.SurveilDecision(
                ToGraveyard: new[] { second }, TopOrder: new[] { top })),
            peeked);
        surveil.ToGraveyard.Should().ContainSingle().Which.InstanceId.Should().Be(second.InstanceId);
        surveil.TopOrder.Should().ContainSingle().Which.InstanceId.Should().Be(top.InstanceId);
    }

    [Fact]
    public void LibraryPick_RoundTrips_IncludingDecline()
    {
        var (self, _) = BuildPlayers();
        var card = SeedLibrary(self, "Forest", land: true);

        var picked = BotDecisionCodec.DecodeLibraryPick(
            BotDecisionCodec.EncodeLibraryPick(card), new ICard[] { card });
        picked.Should().NotBeNull();
        picked!.InstanceId.Should().Be(card.InstanceId);

        var declined = BotDecisionCodec.DecodeLibraryPick(
            BotDecisionCodec.EncodeLibraryPick(null), new ICard[] { card });
        declined.Should().BeNull();
    }

    [Fact]
    public void YesNo_RoundTrips()
    {
        BotDecisionCodec.DecodeYesNo(BotDecisionCodec.EncodeYesNo(true)).Should().BeTrue();
        BotDecisionCodec.DecodeYesNo(BotDecisionCodec.EncodeYesNo(false)).Should().BeFalse();
    }

    [Fact]
    public void Choose_RoundTrips_CardsScalarsAndBoolSentinel()
    {
        var (self, opp) = BuildPlayers();
        var card = SeedHand(self, "Grizzly Bears");

        var req = new ChoiceRequest(
            ChoiceKind.PickN, "test", Min: 1, Max: 3,
            Candidates: new object[] { card, 2, true });

        var payload = BotDecisionCodec.EncodeChoose(new object[] { card, 2, true });
        AssertIdLevelOnly(payload);

        var decoded = BotDecisionCodec.DecodeChoose(payload, req, Ctx(self, opp));
        decoded.Should().HaveCount(3);
        decoded[0].Should().BeAssignableTo<ICard>()
            .Which.InstanceId.Should().Be(card.InstanceId);
        decoded[1].Should().Be(2);
        decoded[2].Should().Be(true);
    }

    [Fact]
    public void Decode_MissingId_Throws()
    {
        var (self, opp) = BuildPlayers();
        var stray = new Creature("Ghost", "1", 1, 1) { Owner = self, Controller = self };

        // Encode a card that is NOT present in any zone → decode must throw
        // (replay's graceful-stop catches it).
        var payload = BotDecisionCodec.EncodeTargets(new object[] { stray });
        var act = () => BotDecisionCodec.DecodeTargets(payload, Ctx(self, opp));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Records_JsonRoundTrip_PreservesKindAndPayload()
    {
        var (self, _) = BuildPlayers();
        var blocker = SeedCreature(self, "Grizzly Bears", 2, 2);
        var attacker = SeedCreature(self, "Raging Goblin", 1, 1);

        var record = new BotDecisionRecord(
            BotSeq: 5,
            Kind: BotDecisionKind.Blockers,
            Payload: BotDecisionCodec.EncodeBlockers(
                new BlockPlan(new[] { new BlockerDeclaration(blocker, attacker) })));

        var json = System.Text.Json.JsonSerializer.Serialize(record);
        var back = System.Text.Json.JsonSerializer.Deserialize<BotDecisionRecord>(json);

        back.Should().BeEquivalentTo(record);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static (Player Self, Player Opp) BuildPlayers()
        => (new Player("Bob", 20), new Player("Alice", 20));

    private static GameContext Ctx(Player self, Player opp) => new(
        self, new[] { self, opp }, activePlayer: self, turnNumber: 1,
        currentPhase: null, stack: new Majik.Core.Stack.Stack());

    private static Creature SeedCreature(Player p, string name, int power, int toughness)
    {
        var c = new Creature(name, "1", power, toughness) { Owner = p, Controller = p };
        p.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Land SeedLand(Player p, string name)
    {
        var land = new Land(name) { Owner = p, Controller = p };
        p.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        return land;
    }

    private static ICard SeedHand(Player p, string name, bool land = false)
    {
        ICard card = land
            ? new Land(name) { Owner = p, Controller = p }
            : new Creature(name, "1", 2, 2) { Owner = p, Controller = p };
        p.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        return card;
    }

    private static ICard SeedLibrary(Player p, string name, bool land = false)
    {
        ICard card = land
            ? new Land(name) { Owner = p, Controller = p }
            : new Creature(name, "1", 2, 2) { Owner = p, Controller = p };
        p.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }

    private static ICard SeedExiled(Player p, string name)
    {
        ICard card = new Creature(name, "1", 2, 2) { Owner = p, Controller = p };
        p.Zones.GetZone(ZoneType.Exile).AddCard(card);
        card.SetZone(ZoneType.Exile);
        return card;
    }

    /// <summary>
    /// Recursively walk the payload object graph and assert it carries NO
    /// engine object references — only Ids and scalars survive encoding.
    /// </summary>
    private static void AssertIdLevelOnly(object? node)
    {
        if (node == null) return;
        var type = node.GetType();

        node.Should().NotBeAssignableTo<ICard>("payloads must carry InstanceIds, not card refs");
        node.Should().NotBeAssignableTo<Player>("payloads must carry Player.Ids, not player refs");
        node.Should().NotBeAssignableTo<IAlternativeCost>("alt costs must be encoded as descriptors");
        node.Should().NotBeAssignableTo<IAdditionalCost>("additional costs must be encoded as descriptors");
        node.Should().NotBeAssignableTo<Majik.Core.Abilities.IAbility>("abilities must be encoded as ids");

        if (type.IsPrimitive || type.IsEnum || node is string || node is Guid || node is decimal)
            return;

        if (node is System.Collections.IEnumerable seq)
        {
            foreach (var item in seq) AssertIdLevelOnly(item);
            return;
        }

        foreach (var prop in type.GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            AssertIdLevelOnly(prop.GetValue(node));
        }
    }
}
