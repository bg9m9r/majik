using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Api.BotReplay;

/// <summary>
/// Encode/decode between live <see cref="IPlayerAgent"/> answers and Id-level
/// <see cref="BotDecisionPayload"/>s. Encoding strips every object reference
/// down to InstanceId / Player.Id / scalar; decoding resolves those Ids by
/// dictionary lookup against the rebuilt facade's live objects (the
/// <c>RebindForReplay</c> precedent) — possible because rehydration runs
/// under the same <c>DeterministicIdScope</c> seed that minted the originals.
///
/// <para><b>Coverage policy:</b> one codec per primitive decision kind
/// (see <see cref="BotDecisionKind"/>). Alternative / additional costs are
/// encoded only for the concrete types the bot emits today
/// (<see cref="ExileCastAlternativeCost"/>, <see cref="MultikickerAdditionalCost"/>);
/// anything else throws <see cref="UnsupportedBotDecisionException"/> at
/// encode time so a record is never silently wrong.</para>
/// </summary>
public static class BotDecisionCodec
{
    private const string ExileCastAltCostType = "exile-cast";
    private const string MultikickerCostType = "multikicker";

    /// <summary>
    /// Registry: primitive <see cref="IPlayerAgent"/> method name → the
    /// decision kind its codec handles. <c>CodecCoverageTripwireTests</c>
    /// asserts this covers the whole primitive surface.
    /// </summary>
    public static IReadOnlyDictionary<string, BotDecisionKind> PrimitiveMethodKinds { get; } =
        new Dictionary<string, BotDecisionKind>
        {
            [nameof(IPlayerAgent.ChoosePriorityActionAsync)] = BotDecisionKind.Priority,
            [nameof(IPlayerAgent.ChooseMulliganAsync)] = BotDecisionKind.Mulligan,
            [nameof(IPlayerAgent.ChooseCardsToBottomAsync)] = BotDecisionKind.CardsToBottom,
            [nameof(IPlayerAgent.ChooseTargetsAsync)] = BotDecisionKind.Targets,
            [nameof(IPlayerAgent.ChooseXAsync)] = BotDecisionKind.X,
            [nameof(IPlayerAgent.ChooseModeAsync)] = BotDecisionKind.Mode,
            [nameof(IPlayerAgent.OrderTriggersAsync)] = BotDecisionKind.TriggerOrder,
            [nameof(IPlayerAgent.ChooseManaSourcesAsync)] = BotDecisionKind.ManaSources,
            [nameof(IPlayerAgent.DeclareAttackersAsync)] = BotDecisionKind.Attackers,
            [nameof(IPlayerAgent.DeclareBlockersAsync)] = BotDecisionKind.Blockers,
            [nameof(IPlayerAgent.ChooseScryDecisionAsync)] = BotDecisionKind.Scry,
            [nameof(IPlayerAgent.ChooseSurveilDecisionAsync)] = BotDecisionKind.Surveil,
            [nameof(IPlayerAgent.ChooseLibraryPickAsync)] = BotDecisionKind.LibraryPick,
            [nameof(IPlayerAgent.ChooseYesNoAsync)] = BotDecisionKind.YesNo,
            [nameof(IPlayerAgent.ChooseAsync)] = BotDecisionKind.Choose,
        };

    // =======================================================================
    // Encode — live answer → Id-level payload
    // =======================================================================

    public static BotDecisionPayload EncodePriority(PriorityAction action) => action switch
    {
        PriorityAction.PassAction => new PassPayload(),

        PriorityAction.CastSpell cs => new CastSpellPayload(
            cs.Card.InstanceId,
            EncodeRefs(cs.Targets),
            cs.HoldPriority,
            EncodeAltCost(cs.AlternativeCost),
            EncodeAdditionalCosts(cs.AdditionalCosts)),

        PriorityAction.PlayLand pl => new PlayLandPayload(pl.Land.InstanceId, pl.HoldPriority),

        PriorityAction.ActivateAbility aa => new ActivateAbilityPayload(
            aa.Ability.Id,
            aa.Ability.Source is ICard src ? src.InstanceId : default,
            EncodeRefs(aa.Targets),
            aa.HoldPriority),

        PriorityAction.ActivateLoyaltyAbility la => EncodeLoyalty(la),

        PriorityAction.ActivateManaAbility ma => EncodeManaAbility(ma),

        _ => throw new UnsupportedBotDecisionException(
            $"No codec for priority action '{action.GetType().Name}'."),
    };

    public static BotDecisionPayload EncodeMulligan(MulliganDecision decision)
        => new MulliganPayload(decision == MulliganDecision.Keep);

    public static BotDecisionPayload EncodeCardsToBottom(IReadOnlyList<ICard> cards)
        => new CardsToBottomPayload(cards.Select(c => c.InstanceId).ToList());

    public static BotDecisionPayload EncodeTargets(IReadOnlyList<object> targets)
        => new TargetsPayload(EncodeRefs(targets));

    public static BotDecisionPayload EncodeX(int x) => new XPayload(x);

    public static BotDecisionPayload EncodeMode(int modeIndex) => new ModePayload(modeIndex);

    public static BotDecisionPayload EncodeTriggerOrder(IReadOnlyList<ITriggeredAbility> ordered)
        => new TriggerOrderPayload(ordered.Select(t => t.Id).ToList());

    public static BotDecisionPayload EncodeManaSources(ManaPayment payment)
        => new ManaSourcesPayload(
            payment.Sources.Select(s => s.InstanceId).ToList(),
            payment.IsCancelled);

    public static BotDecisionPayload EncodeAttackers(CombatPlan plan)
        => new AttackersPayload(plan.Attackers
            .Select(a => new AttackerPair(a.Attacker.InstanceId, EncodeRef(a.DefendingPlayerOrPlaneswalker)))
            .ToList());

    public static BotDecisionPayload EncodeBlockers(BlockPlan plan)
        => new BlockersPayload(plan.Blockers
            .Select(b => new BlockerPair(b.Blocker.InstanceId, b.Attacker.InstanceId))
            .ToList());

    public static BotDecisionPayload EncodeScry(ScryAction.ScryDecision decision)
        => new ScryPayload(
            decision.ToBottom.Select(c => c.InstanceId).ToList(),
            decision.TopOrder.Select(c => c.InstanceId).ToList());

    public static BotDecisionPayload EncodeSurveil(SurveilAction.SurveilDecision decision)
        => new SurveilPayload(
            decision.ToGraveyard.Select(c => c.InstanceId).ToList(),
            decision.TopOrder.Select(c => c.InstanceId).ToList());

    public static BotDecisionPayload EncodeLibraryPick(ICard? pick)
        => new LibraryPickPayload(pick?.InstanceId);

    public static BotDecisionPayload EncodeYesNo(bool answer) => new YesNoPayload(answer);

    public static BotDecisionPayload EncodeChoose(IReadOnlyList<object> chosen)
        => new ChoosePayload(EncodeRefs(chosen));

    // =======================================================================
    // Decode — Id-level payload → live objects on the rebuilt facade
    // =======================================================================

    public static PriorityAction DecodePriority(BotDecisionPayload payload, GameContext ctx, Player self)
        => payload switch
        {
            PassPayload => PriorityAction.Pass,

            CastSpellPayload cs => new PriorityAction.CastSpell(
                ResolveCard(cs.CardId, ctx),
                DecodeRefs(cs.Targets, ctx),
                cs.HoldPriority,
                DecodeAltCost(cs.AlternativeCost),
                DecodeAdditionalCosts(cs.AdditionalCosts, ctx)),

            PlayLandPayload pl => new PriorityAction.PlayLand(
                ResolveCard(pl.LandId, ctx), pl.HoldPriority),

            ActivateAbilityPayload aa => new PriorityAction.ActivateAbility(
                ResolveActivatedAbility(aa, ctx),
                DecodeRefs(aa.Targets, ctx),
                aa.HoldPriority),

            ActivateLoyaltyAbilityPayload la => new PriorityAction.ActivateLoyaltyAbility(
                ResolveLoyaltyAbility(la, ctx),
                DecodeRefs(la.Targets, ctx),
                la.HoldPriority),

            ActivateManaAbilityPayload ma => DecodeManaAbility(ma, ctx),

            _ => throw new InvalidOperationException(
                $"Recorded priority payload '{payload.GetType().Name}' cannot be decoded."),
        };

    public static MulliganDecision DecodeMulligan(BotDecisionPayload payload)
        => Expect<MulliganPayload>(payload).Keep ? MulliganDecision.Keep : MulliganDecision.Mulligan;

    public static IReadOnlyList<ICard> DecodeCardsToBottom(
        BotDecisionPayload payload, IReadOnlyList<ICard> hand)
        => Expect<CardsToBottomPayload>(payload).CardIds
            .Select(id => ResolveFrom(hand, id, "hand"))
            .ToList();

    public static IReadOnlyList<object> DecodeTargets(BotDecisionPayload payload, GameContext ctx)
        => DecodeRefs(Expect<TargetsPayload>(payload).Targets, ctx);

    public static int DecodeX(BotDecisionPayload payload) => Expect<XPayload>(payload).X;

    public static int DecodeMode(BotDecisionPayload payload) => Expect<ModePayload>(payload).ModeIndex;

    public static IReadOnlyList<ITriggeredAbility> DecodeTriggerOrder(
        BotDecisionPayload payload, IReadOnlyList<ITriggeredAbility> mine)
        => Expect<TriggerOrderPayload>(payload).AbilityIds
            .Select(id => mine.FirstOrDefault(t => t.Id == id)
                ?? throw Missing($"triggered ability {id}", "presented trigger list"))
            .ToList();

    public static ManaPayment DecodeManaSources(BotDecisionPayload payload, GameContext ctx)
    {
        var p = Expect<ManaSourcesPayload>(payload);
        if (p.IsCancelled) return ManaPayment.Cancelled;
        return new ManaPayment(p.SourceIds.Select(id => ResolveCard(id, ctx)).ToList());
    }

    public static CombatPlan DecodeAttackers(
        BotDecisionPayload payload, GameContext ctx, IReadOnlyList<Creature> eligibleAttackers)
        => new(Expect<AttackersPayload>(payload).Attackers
            .Select(a => new AttackerDeclaration(
                (Creature)ResolveFrom(eligibleAttackers, a.AttackerId, "eligible attackers"),
                DecodeRef(a.Defender, ctx)))
            .ToList());

    public static BlockPlan DecodeBlockers(
        BotDecisionPayload payload,
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> eligibleBlockers)
        => new(Expect<BlockersPayload>(payload).Pairs
            .Select(b => new BlockerDeclaration(
                (Creature)ResolveFrom(eligibleBlockers, b.BlockerId, "eligible blockers"),
                (Creature)ResolveFrom(attackers, b.AttackerId, "attackers")))
            .ToList());

    public static ScryAction.ScryDecision DecodeScry(
        BotDecisionPayload payload, IReadOnlyList<ICard> peeked)
    {
        var p = Expect<ScryPayload>(payload);
        return new ScryAction.ScryDecision(
            p.ToBottom.Select(id => ResolveFrom(peeked, id, "peeked cards")).ToList(),
            p.TopOrder.Select(id => ResolveFrom(peeked, id, "peeked cards")).ToList());
    }

    public static SurveilAction.SurveilDecision DecodeSurveil(
        BotDecisionPayload payload, IReadOnlyList<ICard> peeked)
    {
        var p = Expect<SurveilPayload>(payload);
        return new SurveilAction.SurveilDecision(
            p.ToGraveyard.Select(id => ResolveFrom(peeked, id, "peeked cards")).ToList(),
            p.TopOrder.Select(id => ResolveFrom(peeked, id, "peeked cards")).ToList());
    }

    public static ICard? DecodeLibraryPick(
        BotDecisionPayload payload, IReadOnlyList<ICard> candidates)
    {
        var p = Expect<LibraryPickPayload>(payload);
        return p.SelectedId is { } id ? ResolveFrom(candidates, id, "library candidates") : null;
    }

    public static bool DecodeYesNo(BotDecisionPayload payload)
        => Expect<YesNoPayload>(payload).Answer;

    public static IReadOnlyList<object> DecodeChoose(
        BotDecisionPayload payload, ChoiceRequest req, GameContext? ctx)
    {
        var candidates = req.Candidates ?? Array.Empty<object>();
        return Expect<ChoosePayload>(payload).Selected
            .Select(tag => DecodeChoiceElement(tag, candidates, ctx))
            .ToList();
    }

    // =======================================================================
    // Ref tags
    // =======================================================================

    private static IReadOnlyList<RefTag> EncodeRefs(IReadOnlyList<object> items)
        => items.Select(EncodeRef).ToList();

    private static RefTag EncodeRef(object item) => item switch
    {
        ICard card => new RefTag(RefKind.Card, Id: card.InstanceId),
        Player player => new RefTag(RefKind.Player, Id: player.Id),
        int i => new RefTag(RefKind.Int, IntValue: i),
        bool b => new RefTag(RefKind.Bool, BoolValue: b),
        string s => new RefTag(RefKind.String, StringValue: s),
        _ => throw new UnsupportedBotDecisionException(
            $"No ref-tag codec for choice/target element of type '{item?.GetType().Name ?? "null"}'."),
    };

    private static IReadOnlyList<object> DecodeRefs(IReadOnlyList<RefTag> tags, GameContext ctx)
        => tags.Select(t => DecodeRef(t, ctx)).ToList();

    private static object DecodeRef(RefTag tag, GameContext ctx) => tag.Kind switch
    {
        RefKind.Card => ResolveCard(tag.Id, ctx),
        RefKind.Player => ResolvePlayer(tag.Id, ctx),
        RefKind.Int => tag.IntValue,
        RefKind.Bool => tag.BoolValue,
        RefKind.String => tag.StringValue!,
        _ => throw new InvalidOperationException($"Unknown ref kind '{tag.Kind}'."),
    };

    private static object DecodeChoiceElement(
        RefTag tag, IReadOnlyList<object> candidates, GameContext? ctx)
    {
        switch (tag.Kind)
        {
            case RefKind.Card:
                // Prefer the prompt's own candidate pool (precise even when
                // ctx is null), then fall back to a facade-wide lookup.
                var fromCandidates = candidates.OfType<ICard>()
                    .FirstOrDefault(c => c.InstanceId == tag.Id);
                if (fromCandidates != null) return fromCandidates;
                if (ctx != null) return ResolveCard(tag.Id, ctx);
                throw Missing($"card {tag.Id}", "choice candidates");

            case RefKind.Player:
                var player = candidates.OfType<Player>().FirstOrDefault(p => p.Id == tag.Id)
                    ?? ctx?.AllPlayers.FirstOrDefault(p => p.Id == tag.Id);
                return player ?? throw Missing($"player {tag.Id}", "choice candidates");

            case RefKind.Int: return tag.IntValue;
            case RefKind.Bool: return tag.BoolValue;
            case RefKind.String: return tag.StringValue!;
            default:
                throw new InvalidOperationException($"Unknown ref kind '{tag.Kind}'.");
        }
    }

    // =======================================================================
    // Alt / additional costs (concrete types the bot emits today)
    // =======================================================================

    private static AltCostDescriptor? EncodeAltCost(IAlternativeCost? cost) => cost switch
    {
        null => null,
        ExileCastAlternativeCost exile => new AltCostDescriptor(
            ExileCastAltCostType, exile.Description, exile.AlternativeManaCost.ToString()),
        _ => throw new UnsupportedBotDecisionException(
            $"No codec for alternative cost type '{cost.GetType().Name}' — add one " +
            "before the bot may elect it (recording would otherwise corrupt replay)."),
    };

    private static IAlternativeCost? DecodeAltCost(AltCostDescriptor? descriptor) => descriptor switch
    {
        null => null,
        { Type: ExileCastAltCostType } d => new ExileCastAlternativeCost(
            d.Description, ManaCost.Parse(d.ManaCost)),
        _ => throw new InvalidOperationException(
            $"Recorded alternative-cost type '{descriptor.Type}' cannot be decoded."),
    };

    private static IReadOnlyList<AdditionalCostDescriptor>? EncodeAdditionalCosts(
        IReadOnlyList<IAdditionalCost>? costs)
        => costs?.Select(c => c switch
        {
            MultikickerAdditionalCost mk => new AdditionalCostDescriptor(
                MultikickerCostType, mk.Card.InstanceId, mk.PerKickCost.ToString(), mk.Times),
            _ => throw new UnsupportedBotDecisionException(
                $"No codec for additional cost type '{c.GetType().Name}'."),
        }).ToList();

    private static IReadOnlyList<IAdditionalCost>? DecodeAdditionalCosts(
        IReadOnlyList<AdditionalCostDescriptor>? descriptors, GameContext ctx)
        => descriptors?.Select(d => (IAdditionalCost)(d.Type switch
        {
            MultikickerCostType => new MultikickerAdditionalCost(
                ResolveCard(d.CardId, ctx), ManaCost.Parse(d.ManaCost), d.Times),
            _ => throw new InvalidOperationException(
                $"Recorded additional-cost type '{d.Type}' cannot be decoded."),
        })).ToList();

    // =======================================================================
    // Loyalty / mana abilities (positional addressing — their ids are not
    // deterministic across a rebuild)
    // =======================================================================

    private static ActivateLoyaltyAbilityPayload EncodeLoyalty(
        PriorityAction.ActivateLoyaltyAbility action)
    {
        if (action.Ability.Source is not ICard source)
            throw new UnsupportedBotDecisionException(
                "Loyalty ability without an ICard source cannot be recorded.");
        var index = source.Abilities.OfType<LoyaltyAbility>().ToList().IndexOf(action.Ability);
        if (index < 0)
            throw new UnsupportedBotDecisionException(
                $"Loyalty ability not found on its source card '{source.Name}'.");
        return new ActivateLoyaltyAbilityPayload(
            source.InstanceId, index, EncodeRefs(action.Targets), action.HoldPriority);
    }

    private static LoyaltyAbility ResolveLoyaltyAbility(
        ActivateLoyaltyAbilityPayload payload, GameContext ctx)
    {
        var source = ResolveCard(payload.SourceCardId, ctx);
        var abilities = source.Abilities.OfType<LoyaltyAbility>().ToList();
        if (payload.AbilityIndex < 0 || payload.AbilityIndex >= abilities.Count)
            throw Missing(
                $"loyalty ability #{payload.AbilityIndex}", $"card '{source.Name}'");
        return abilities[payload.AbilityIndex];
    }

    private static ActivateManaAbilityPayload EncodeManaAbility(
        PriorityAction.ActivateManaAbility action)
    {
        var index = action.Source.Abilities.OfType<IManaAbility>().ToList().IndexOf(action.Ability);
        if (index < 0)
            throw new UnsupportedBotDecisionException(
                $"Mana ability not found on its source card '{action.Source.Name}'.");
        return new ActivateManaAbilityPayload(action.Source.InstanceId, index);
    }

    private static PriorityAction DecodeManaAbility(
        ActivateManaAbilityPayload payload, GameContext ctx)
    {
        var source = ResolveCard(payload.SourceCardId, ctx);
        var abilities = source.Abilities.OfType<IManaAbility>().ToList();
        if (payload.AbilityIndex < 0 || payload.AbilityIndex >= abilities.Count)
            throw Missing($"mana ability #{payload.AbilityIndex}", $"card '{source.Name}'");
        return new PriorityAction.ActivateManaAbility(source, abilities[payload.AbilityIndex]);
    }

    private static IActivatedAbility ResolveActivatedAbility(
        ActivateAbilityPayload payload, GameContext ctx)
    {
        foreach (var player in ctx.AllPlayers)
        {
            foreach (var card in player.Zones.Battlefield.GetCards())
            {
                foreach (var ability in card.Abilities.OfType<IActivatedAbility>())
                {
                    if (ability.Id == payload.AbilityId) return ability;
                }
            }
        }

        // Fallback: same source card, match positionally is unsafe — fail.
        throw Missing($"activated ability {payload.AbilityId}", "battlefield");
    }

    // =======================================================================
    // Id resolution
    // =======================================================================

    private static readonly ZoneType[] AllZoneTypes = (ZoneType[])Enum.GetValues(typeof(ZoneType));

    private static ICard ResolveCard(Guid instanceId, GameContext ctx)
    {
        foreach (var player in ctx.AllPlayers)
        {
            foreach (var zoneType in AllZoneTypes)
            {
                foreach (var card in player.Zones.GetZone(zoneType).GetCards())
                {
                    if (card.InstanceId == instanceId) return card;
                }
            }
        }
        throw Missing($"card {instanceId}", "any zone of the rebuilt game");
    }

    private static Player ResolvePlayer(Guid playerId, GameContext ctx)
        => ctx.AllPlayers.FirstOrDefault(p => p.Id == playerId)
           ?? throw Missing($"player {playerId}", "rebuilt game");

    private static T ResolveFrom<T>(IReadOnlyList<T> pool, Guid instanceId, string poolLabel)
        where T : class, ICard
        => pool.FirstOrDefault(c => c.InstanceId == instanceId)
           ?? throw Missing($"card {instanceId}", poolLabel);

    private static T Expect<T>(BotDecisionPayload payload) where T : BotDecisionPayload
        => payload as T ?? throw new InvalidOperationException(
            $"Recorded payload is '{payload.GetType().Name}' but the replay prompt " +
            $"expects '{typeof(T).Name}' — bot-decision stream desync.");

    private static InvalidOperationException Missing(string what, string where)
        => new($"Recorded {what} did not resolve against {where} — bot-decision " +
               "stream desync (replay stops gracefully).");
}
