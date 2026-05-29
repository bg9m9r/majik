using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// Builds a runtime <see cref="ICard"/> from a <see cref="CardDefinition"/>.
/// Bridges the data-only schema with the engine's hand-rolled card
/// hierarchy. Phase-3 scope: card-type dispatch, P/T + cost wiring,
/// supertype/subtype attachment, and mana abilities. Activated/triggered/
/// static abilities will land in follow-up PRs as
/// <see cref="AbilityDefinition"/> grows.
/// </summary>
public static class CardDefinitionFactory
{
    /// <summary>
    /// Materialize a card for the supplied owner. The first listed
    /// <see cref="CardDefinition.Types"/> dictates the runtime C# class
    /// (Land / Creature / Instant / …); additional types are added via
    /// <see cref="Card.AddCardType"/> so multi-type cards (Artifact
    /// Creature, …) work correctly.
    /// </summary>
    public static ICard Build(CardDefinition definition, Player owner) =>
        Build(definition, owner, replacements: null);

    /// <summary>
    /// Materialize a card for the supplied owner, optionally routing
    /// JSON-driven +1/+1 counter placements through the supplied
    /// <see cref="ReplacementBus"/> (CR 614). When <paramref name="replacements"/>
    /// is null, counter placements fall through to a direct add — same
    /// behaviour as today's untouched callers.
    /// </summary>
    public static ICard Build(CardDefinition definition, Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(owner);
        if (definition.Types.Count == 0)
            throw new ArgumentException($"Card '{definition.Name}' has no types.", nameof(definition));

        var supertypes = definition.Supertypes.Select(ParseSupertype).ToArray();
        var subtypes = definition.Subtypes.Select(ParseSubtype).ToArray();
        var primary = ParseType(definition.Types[0]);
        // ManaCost passed verbatim — JSON authors decide bracketing.
        var manaCost = definition.ManaCost;

        ICard card = primary switch
        {
            CardType.Land => new Land(definition.Name, supertypes, subtypes),
            CardType.Creature => new Creature(
                definition.Name, manaCost,
                definition.Power ?? throw MissingStat(definition.Name, "power"),
                definition.Toughness ?? throw MissingStat(definition.Name, "toughness"),
                supertypes, subtypes),
            CardType.Artifact => new Artifact(definition.Name, manaCost, supertypes, subtypes),
            CardType.Enchantment => new Enchantment(definition.Name, manaCost, supertypes, subtypes),
            CardType.Instant => new Instant(definition.Name, manaCost, supertypes, subtypes),
            CardType.Sorcery => new Sorcery(definition.Name, manaCost, supertypes, subtypes),
            CardType.Planeswalker => new Planeswalker(
                definition.Name, manaCost,
                definition.Loyalty ?? throw MissingStat(definition.Name, "loyalty"),
                supertypes, subtypes),
            _ => throw new NotSupportedException(
                $"Card '{definition.Name}' primary type '{definition.Types[0]}' is not supported by CardDefinitionFactory."),
        };

        // Multi-type cards (e.g. Artifact Creature) — apply secondary types
        // via the AddCardType seam on the concrete Card base class
        // (the method is internal on Card, not exposed on ICard).
        if (card is Card concrete)
        {
            for (var i = 1; i < definition.Types.Count; i++)
            {
                concrete.AddCardType(ParseType(definition.Types[i]));
            }
        }

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 202.2c — printed color indicator. Stamped on the concrete
        // Card so CardColors.GetColors honours it (Dryad Arbor: no mana
        // cost, indicator says green). Skipped when the definition lists
        // no indicator codes (the default for the overwhelming majority
        // of cards).
        if (card is Card concreteForIndicator && definition.Colors.Count > 0)
        {
            var indicator = new List<ManaColor>(definition.Colors.Count);
            foreach (var letter in definition.Colors)
            {
                indicator.Add(ParseColorLetter(letter));
            }
            concreteForIndicator.SetColorIndicator(indicator);
        }

        foreach (var ability in definition.Abilities)
        {
            card.AddAbility(BuildAbility(ability, card, owner, replacements));
        }

        return card;
    }

    private static ManaColor ParseColorLetter(string raw) =>
        raw?.Trim().ToUpperInvariant() switch
        {
            "W" => ManaColor.White,
            "U" => ManaColor.Blue,
            "B" => ManaColor.Black,
            "R" => ManaColor.Red,
            "G" => ManaColor.Green,
            _ => throw new ArgumentException(
                $"Unknown color indicator code '{raw}'. Expected single-letter Scryfall codes (W/U/B/R/G).",
                nameof(raw)),
        };

    private static IAbility BuildAbility(AbilityDefinition definition, ICard card, Player controller, ReplacementBus? replacements) =>
        definition switch
        {
            ManaAbilityDefinition mana => new ManaAbility(card, controller, ManaCost.Parse(mana.Produces)),
            ActivatedAbilityDefinition activated => BuildActivatedAbility(activated, card, controller, replacements),
            TriggeredAbilityDefinition triggered => BuildTriggeredAbility(triggered, card, controller, replacements),
            _ => throw new NotSupportedException(
                $"Ability '{definition.GetType().Name}' is not yet supported by CardDefinitionFactory."),
        };

    private static TriggeredAbility BuildTriggeredAbility(
        TriggeredAbilityDefinition definition, ICard card, Player controller, ReplacementBus? replacements)
    {
        var condition = BuildTrigger(definition.Trigger, card);
        var effects = definition.Effects.Select(e => BuildEffect(e, card, controller, replacements)).ToArray();
        return new TriggeredAbility(
            source: card,
            controller: controller,
            condition: condition,
            effects: effects);
    }

    private static Majik.Core.Abilities.ITriggerCondition BuildTrigger(
        TriggerDefinition definition, ICard card) =>
        definition switch
        {
            EnterBattlefieldSelfTriggerDef => Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf(card),
            CardLeavesYourGraveyardTriggerDef gy => BuildCardLeavesYourGraveyardTrigger(gy, card),
            _ => throw new NotSupportedException(
                $"Trigger '{definition.GetType().Name}' is not yet supported by CardDefinitionFactory."),
        };

    private static Majik.Core.Abilities.ITriggerCondition BuildCardLeavesYourGraveyardTrigger(
        CardLeavesYourGraveyardTriggerDef def, ICard card)
    {
        var types = def.CardTypes.Select(ParseType).ToArray();
        return new Majik.Core.Abilities.EventTriggerCondition<Majik.Core.Events.CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != Majik.Core.Zones.ZoneType.Graveyard) return false;
            // "Your" graveyard — the controller of this trigger's source card.
            var triggerController = card.Controller;
            if (triggerController is null || !ReferenceEquals(e.Card.Owner, triggerController))
            {
                return false;
            }
            return types.Length == 0 || types.Any(t => e.Card.HasType(t));
        });
    }

    private static ActivatedAbility BuildActivatedAbility(
        ActivatedAbilityDefinition definition, ICard card, Player controller, ReplacementBus? replacements)
    {
        var costs = definition.Costs.Select(c => BuildCost(c, card)).ToArray();
        var effects = definition.Effects.Select(e => BuildEffect(e, card, controller, replacements)).ToArray();
        // CR 117.1a / 307.5 — "Activate only as a sorcery" rider is
        // threaded from the definition onto the runtime ActivatedAbility
        // so ActionValidator can gate activation on the controller's
        // main phase + empty stack. See SorcerySpeedActivationTests for
        // the validator behaviour.
        return new ActivatedAbility(
            source: card,
            controller: controller,
            costs: costs,
            effects: effects,
            sorcerySpeed: definition.SorcerySpeed);
    }

    private static ICost BuildCost(CostDefinition definition, ICard card) =>
        definition switch
        {
            ManaCostDef mana => new ManaCostCost(mana.Amount),
            RemoveCounterCostDef rc => BuildRemoveCounterCost(rc, card),
            TapSelfCostDef => BuildTapSelfCost(card),
            SacrificeSelfCostDef => BuildSacrificeSelfCost(card),
            DiscardSelfCostDef => new DiscardSelfCost(card),
            _ => throw new NotSupportedException(
                $"Cost '{definition.GetType().Name}' is not yet supported by CardDefinitionFactory."),
        };

    private static ICost BuildTapSelfCost(ICard card)
    {
        if (card is not Permanent permanent)
        {
            throw new InvalidOperationException(
                $"Card '{card.Name}' is not a Permanent — cannot pay {{T}} as a cost.");
        }
        return AdditionalCost.Tap(permanent);
    }

    private static ICost BuildSacrificeSelfCost(ICard card)
    {
        if (card is not Permanent permanent)
        {
            throw new InvalidOperationException(
                $"Card '{card.Name}' is not a Permanent — cannot pay 'sacrifice this' as a cost.");
        }
        return AdditionalCost.Sacrifice(permanent);
    }

    private static ICost BuildRemoveCounterCost(RemoveCounterCostDef def, ICard card)
    {
        if (!string.Equals(def.From, "self", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"RemoveCounterCostDef.From '{def.From}' is not yet supported (v1 = 'self').");
        }
        if (!string.Equals(def.Counter, "+1/+1", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"RemoveCounterCostDef.Counter '{def.Counter}' is not yet supported (v1 = '+1/+1').");
        }
        if (card is not Permanent permanent)
        {
            throw new InvalidOperationException(
                $"Card '{card.Name}' is not a Permanent — cannot remove counters from it as a cost.");
        }
        return new RemovePlusOnePlusOneCounterCost(permanent, def.Amount);
    }

    private static IEffect BuildEffect(EffectDefinition definition, ICard card, Player controller, ReplacementBus? replacements) =>
        definition switch
        {
            PutCounterEffectDef put => BuildPutCounterEffect(put, card, replacements),
            DealDamageStubEffectDef stub => BuildDealDamageStubEffect(stub, card),
            DrawCardEffectDef draw => BuildDrawCardEffect(draw, card, controller),
            SurveilSelfEffectDef surveil => BuildSurveilSelfEffect(surveil, card, controller),
            ScrySelfEffectDef scry => BuildScrySelfEffect(scry, card, controller),
            DestroyTargetStubEffectDef destroy => BuildDestroyTargetStubEffect(destroy, card),
            UntapTargetStubEffectDef untap => BuildUntapTargetStubEffect(untap, card),
            GainLifeSelfEffectDef gain => BuildGainLifeSelfEffect(gain, card, controller),
            MillThenPickFirstMatchingToHandEffectDef mp => BuildMillThenPickEffect(mp, card, controller),
            ConniveSelfEffectDef connive => BuildConniveSelfEffect(connive, card),
            AmassSelfEffectDef amass => BuildAmassSelfEffect(amass, card, controller),
            _ => throw new NotSupportedException(
                $"Effect '{definition.GetType().Name}' is not yet supported by CardDefinitionFactory."),
        };

    private static IEffect BuildConniveSelfEffect(ConniveSelfEffectDef def, ICard card)
    {
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: connive x{amount}",
            () =>
            {
                if (card is not Creature creature) return;
                Majik.Core.Keywords.ConniveAction.ApplyN(creature, amount);
            });
    }

    private static IEffect BuildAmassSelfEffect(AmassSelfEffectDef def, ICard card, Player controller)
    {
        var amount = def.Amount;
        var tribe = ParseSubtype(def.Tribe);
        return new Effect(
            $"{card.Name}: amass {def.Tribe} {amount}",
            () =>
            {
                Majik.Core.Keywords.AmassAction.Apply(controller, amount, tribe);
            });
    }

    private static IEffect BuildGainLifeSelfEffect(GainLifeSelfEffectDef def, ICard card, Player controller)
    {
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: gain {amount} life",
            () => controller.GainLife(amount));
    }

    private static IEffect BuildMillThenPickEffect(
        MillThenPickFirstMatchingToHandEffectDef def, ICard card, Player controller)
    {
        var amount = def.Amount;
        var types = def.MatchingTypes.Select(ParseType).ToArray();
        return new Effect(
            $"{card.Name}: mill {amount}, pick first matching",
            () =>
            {
                var milled = Majik.Core.Keywords.MillAction.Apply(controller, amount);
                if (types.Length == 0) return;
                var pick = milled.FirstOrDefault(c => types.Any(t => c.HasType(t)));
                if (pick != null)
                {
                    // Move from graveyard to hand. Matches the existing
                    // C# Dredger's Insight behavior — auto-pick (the
                    // "may" opt-out awaits the agent prompt system).
                    controller.Zones.Graveyard.RemoveCard(pick);
                    controller.Zones.Hand.AddCard(pick);
                    pick.SetZone(Majik.Core.Zones.ZoneType.Hand);
                }
            });
    }

    private static IEffect BuildDestroyTargetStubEffect(DestroyTargetStubEffectDef def, ICard card)
    {
        // Stub: matches the existing C# Boseiju Channel-effect deferred
        // behavior. Effect runs (resolution proceeds), but doesn't
        // actually destroy anything — needs the targeting system.
        return new Effect(
            $"{card.Name}: destroy target {def.TargetFilter} (stub — no targeting yet)",
            () => { /* destroy deferred */ });
    }

    private static IEffect BuildUntapTargetStubEffect(UntapTargetStubEffectDef def, ICard card)
    {
        // Stub: mirrors the Boseiju destroy_target_stub deferred behavior.
        // Untapping itself is a supported CR 701.21 action, but choosing
        // the target requires the targeting/prompt system that isn't wired
        // yet, so resolution proceeds as a no-op. Upgrades to a real
        // untap_target effect (Permanent.Untap on the chosen target) once
        // targeting lands, without changing JSON files. Canonical case:
        // Minamo, School at Water's Edge ("Untap target legendary permanent").
        return new Effect(
            $"{card.Name}: untap target {def.TargetFilter} (stub — no targeting yet)",
            () => { /* untap target deferred */ });
    }

    private static IEffect BuildScrySelfEffect(ScrySelfEffectDef def, ICard card, Player controller)
    {
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: scry {amount}",
            () =>
            {
                // CR 701.20 — look at the top N, then choose any subset to put
                // on the bottom; the rest stay on top in chosen order.
                var peeked = Majik.Core.Keywords.ScryAction.Peek(controller, amount);
                if (peeked.Count == 0) return;

                // Consult the registered agent when available; fall back to the
                // all-to-bottom default when none is registered. Exact parallel
                // of BuildSurveilSelfEffect. TODO: remove sync-over-async once
                // IEffect.Execute becomes async.
                var agent = Majik.Core.Players.Agents.AgentRegistry.Get(controller);
                Majik.Core.Keywords.ScryAction.ScryDecision decision;
                if (agent != null)
                {
                    decision = agent.ChooseScryDecisionAsync(null, peeked)
                        .GetAwaiter().GetResult();
                }
                else
                {
                    decision = new Majik.Core.Keywords.ScryAction.ScryDecision(
                        ToBottom: peeked.ToList(),
                        TopOrder: Array.Empty<Majik.Core.Cards.ICard>());
                }
                Majik.Core.Keywords.ScryAction.Apply(controller, amount, decision);
            });
    }

    private static IEffect BuildSurveilSelfEffect(SurveilSelfEffectDef def, ICard card, Player controller)
    {
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: surveil {amount}",
            () =>
            {
                var peeked = Majik.Core.Keywords.SurveilAction.Peek(controller, amount);
                if (peeked.Count == 0) return;

                // Consult the registered agent when available; fall back to
                // the pre-agent default (all-to-graveyard) when none is
                // registered. Mirrors the existing C# Underground Mortuary
                // path. TODO: remove sync-over-async once IEffect.Execute
                // becomes async.
                var agent = Majik.Core.Players.Agents.AgentRegistry.Get(controller);
                Majik.Core.Keywords.SurveilAction.SurveilDecision decision;
                if (agent != null)
                {
                    decision = agent.ChooseSurveilDecisionAsync(null, peeked)
                        .GetAwaiter().GetResult();
                }
                else
                {
                    decision = new Majik.Core.Keywords.SurveilAction.SurveilDecision(
                        ToGraveyard: peeked.ToList(),
                        TopOrder: Array.Empty<Majik.Core.Cards.ICard>());
                }
                Majik.Core.Keywords.SurveilAction.Apply(controller, amount, decision);
            });
    }

    private static IEffect BuildDrawCardEffect(DrawCardEffectDef def, ICard card, Player controller)
    {
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: draw {amount} card(s)",
            () =>
            {
                for (var i = 0; i < amount; i++)
                {
                    var top = controller.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null) return; // empty library — SBAs handle loss elsewhere
                    controller.Zones.Library.RemoveCard(top);
                    controller.Zones.Hand.AddCard(top);
                    top.SetZone(Majik.Core.Zones.ZoneType.Hand);
                }
            });
    }

    private static IEffect BuildPutCounterEffect(PutCounterEffectDef def, ICard card, ReplacementBus? replacements)
    {
        if (!string.Equals(def.Target, "self", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"PutCounterEffectDef.Target '{def.Target}' is not yet supported (v1 = 'self').");
        }
        var counterType = ParseCounterType(def.Counter);
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: put {amount} {def.Counter} counter(s) on self",
            () =>
            {
                if (card is Permanent permanent)
                {
                    // CR 614 — only +1/+1 counter placement currently has
                    // replacements wired (Hardened Scales / Doubling Season).
                    // Other counter types fall through to a direct add.
                    if (counterType == CounterType.PlusOnePlusOne)
                    {
                        CountersService.Add(permanent, counterType, amount, replacements);
                    }
                    else
                    {
                        permanent.Counters.Add(counterType, amount);
                    }
                }
            });
    }

    private static IEffect BuildDealDamageStubEffect(DealDamageStubEffectDef def, ICard card)
    {
        // Matches the existing C# Walking Ballista deferred behavior: the
        // effect runs (resolution proceeds normally) but doesn't route
        // damage to a chosen target — the targeting system isn't wired
        // yet. When the prompt system lands, this stub upgrades to a real
        // 'deal_damage' effect type without breaking JSON files.
        return new Effect(
            $"{card.Name}: deal {def.Amount} damage to {def.Target} (stub — no targeting yet)",
            () => { /* target damage deferred */ });
    }

    private static CounterType ParseCounterType(string raw) => raw switch
    {
        "+1/+1" => CounterType.PlusOnePlusOne,
        "-1/-1" => CounterType.MinusOneMinusOne,
        "Loyalty" => CounterType.Loyalty,
        "Charge" => CounterType.Charge,
        "Defense" => CounterType.Defense,
        "Poison" => CounterType.Poison,
        _ => throw new NotSupportedException($"Counter type '{raw}' is not yet supported."),
    };

    private static CardType ParseType(string raw) =>
        Enum.TryParse<CardType>(raw, ignoreCase: true, out var t)
            ? t
            : throw new ArgumentException($"Unknown card type '{raw}'.", nameof(raw));

    private static CardSupertype ParseSupertype(string raw) =>
        Enum.TryParse<CardSupertype>(raw, ignoreCase: true, out var s)
            ? s
            : throw new ArgumentException($"Unknown card supertype '{raw}'.", nameof(raw));

    private static CardSubtype ParseSubtype(string raw) =>
        Enum.TryParse<CardSubtype>(raw, ignoreCase: true, out var s)
            ? s
            : throw new ArgumentException($"Unknown card subtype '{raw}'.", nameof(raw));

    private static ArgumentException MissingStat(string cardName, string stat) =>
        new($"Card '{cardName}' is missing required '{stat}'.");
}
