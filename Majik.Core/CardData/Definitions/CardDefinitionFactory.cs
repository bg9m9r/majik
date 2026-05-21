using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
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
    public static ICard Build(CardDefinition definition, Player owner)
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

        foreach (var ability in definition.Abilities)
        {
            card.AddAbility(BuildAbility(ability, card, owner));
        }

        return card;
    }

    private static IAbility BuildAbility(AbilityDefinition definition, ICard card, Player controller) =>
        definition switch
        {
            ManaAbilityDefinition mana => new ManaAbility(card, controller, ManaCost.Parse(mana.Produces)),
            ActivatedAbilityDefinition activated => BuildActivatedAbility(activated, card, controller),
            TriggeredAbilityDefinition triggered => BuildTriggeredAbility(triggered, card, controller),
            _ => throw new NotSupportedException(
                $"Ability '{definition.GetType().Name}' is not yet supported by CardDefinitionFactory."),
        };

    private static TriggeredAbility BuildTriggeredAbility(
        TriggeredAbilityDefinition definition, ICard card, Player controller)
    {
        var condition = BuildTrigger(definition.Trigger, card);
        var effects = definition.Effects.Select(e => BuildEffect(e, card, controller)).ToArray();
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
            _ => throw new NotSupportedException(
                $"Trigger '{definition.GetType().Name}' is not yet supported by CardDefinitionFactory."),
        };

    private static ActivatedAbility BuildActivatedAbility(
        ActivatedAbilityDefinition definition, ICard card, Player controller)
    {
        var costs = definition.Costs.Select(c => BuildCost(c, card)).ToArray();
        var effects = definition.Effects.Select(e => BuildEffect(e, card, controller)).ToArray();
        // NOTE: SorcerySpeed is informational on the definition; the runtime
        // ActivatedAbility doesn't yet carry a SorcerySpeed flag, so the
        // restriction is preserved in JSON for the future without enforcement
        // here. Matches the existing C# Walking Ballista deferred note.
        return new ActivatedAbility(
            source: card,
            controller: controller,
            costs: costs,
            effects: effects);
    }

    private static ICost BuildCost(CostDefinition definition, ICard card) =>
        definition switch
        {
            ManaCostDef mana => new ManaCostCost(mana.Amount),
            RemoveCounterCostDef rc => BuildRemoveCounterCost(rc, card),
            TapSelfCostDef => BuildTapSelfCost(card),
            SacrificeSelfCostDef => BuildSacrificeSelfCost(card),
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

    private static IEffect BuildEffect(EffectDefinition definition, ICard card, Player controller) =>
        definition switch
        {
            PutCounterEffectDef put => BuildPutCounterEffect(put, card),
            DealDamageStubEffectDef stub => BuildDealDamageStubEffect(stub, card),
            DrawCardEffectDef draw => BuildDrawCardEffect(draw, card, controller),
            SurveilSelfEffectDef surveil => BuildSurveilSelfEffect(surveil, card, controller),
            _ => throw new NotSupportedException(
                $"Effect '{definition.GetType().Name}' is not yet supported by CardDefinitionFactory."),
        };

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

    private static IEffect BuildPutCounterEffect(PutCounterEffectDef def, ICard card)
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
                    permanent.Counters.Add(counterType, amount);
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
