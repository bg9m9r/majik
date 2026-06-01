using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// Runtime materializer for the fluent <see cref="CardDef"/> DSL. Takes a
/// <see cref="CardDef"/> + <paramref name="owner"/> and produces a fully
/// wired <see cref="ICard"/> ready for the engine.
///
/// <para>
/// This is the bridge between the declarative shape produced by a
/// factory's <c>CardDef Define()</c> method and the engine's hand-rolled
/// card hierarchy (<see cref="Creature"/> / <see cref="Instant"/> / …).
/// </para>
///
/// <para>
/// Resolve bodies declared via <c>Resolve(c => …)</c> are not consumed
/// here — they live on <see cref="CardDef.ResolveBody"/> and are
/// surfaced to spell-cast paths via <see cref="BuildSpellResolveEffects"/>.
/// The card-shape pass + resolve-body pass are decoupled so vanilla
/// shape-only DSL usage doesn't pay for the resolve interpreter.
/// </para>
///
/// ## Shared primitive vocabulary (PLAN 03)
///
/// Resolve steps compile to the shared primitive set that also backs the
/// JSON <see cref="CardDefinitionFactory"/>:
/// <see cref="Majik.Core.Primitives.Fx"/> (effects),
/// <see cref="Majik.Core.Primitives.Costs"/> (costs),
/// <see cref="Majik.Core.Abilities.Triggers"/> (triggers). The canonical
/// home is <c>Majik.Core/Primitives/</c> — the older TODOs pointed at a
/// never-built <c>Majik.Core/Effects/Primitives/</c>. As the primitives
/// grow coverage, each remaining inline branch in
/// <see cref="MaterializeStep"/> converges onto an <c>Fx.*</c> call where
/// the result is byte-identical; the call-site signature never moves.
/// </summary>
public static class CardDefRuntime
{
    /// <summary>
    /// Materialize a card from a <see cref="CardDef"/>. The card is fully
    /// owner/controller-set and has keyword markers / mana abilities
    /// attached per the def. Resolve bodies (spells) are not wired here
    /// — callers route those through <see cref="BuildSpellResolveEffects"/>
    /// at cast time.
    /// </summary>
    public static ICard Build(CardDef def, Player owner) =>
        Build(def, owner, replacements: null);

    /// <summary>
    /// Materialize a card from a <see cref="CardDef"/>, optionally routing
    /// the def's <see cref="CardDef.Abilities"/> +1/+1 counter placements
    /// through the supplied <see cref="ReplacementBus"/> (CR 614). This is
    /// the overload the JSON path
    /// (<see cref="CardDefinitionFactory.Build(CardDefinition, Player, ReplacementBus?)"/>)
    /// routes through after <see cref="CardDefinition.ToCardDef"/>.
    /// </summary>
    public static ICard Build(CardDef def, Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(owner);

        var supertypes = def.Supertypes.ToArray();
        var subtypes = def.Subtypes.ToArray();

        ICard card = def.PrimaryType switch
        {
            CardType.Instant => new Instant(def.Name, def.ManaCost, supertypes, subtypes),
            CardType.Sorcery => new Sorcery(def.Name, def.ManaCost, supertypes, subtypes),
            CardType.Creature => new Creature(
                def.Name, def.ManaCost,
                def.Power ?? throw MissingStat(def.Name, "power"),
                def.Toughness ?? throw MissingStat(def.Name, "toughness"),
                supertypes, subtypes),
            CardType.Artifact => new Artifact(def.Name, def.ManaCost, supertypes, subtypes),
            CardType.Enchantment => new Enchantment(def.Name, def.ManaCost, supertypes, subtypes),
            CardType.Land => new Land(def.Name, supertypes, subtypes),
            CardType.Planeswalker => new Planeswalker(
                def.Name, def.ManaCost,
                def.Loyalty ?? throw MissingStat(def.Name, "loyalty"),
                supertypes, subtypes),
            _ => throw new NotSupportedException(
                $"CardDef primary type '{def.PrimaryType}' is not yet supported by CardDefRuntime."),
        };

        if (card is Card concrete)
        {
            foreach (var t in def.AdditionalTypes)
            {
                concrete.AddCardType(t);
            }
        }

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 202.2c — printed colour indicator. Stamped on the concrete Card
        // so CardColors.GetColors honours it (Dryad Arbor: no mana cost,
        // indicator says green). Skipped when the def lists no indicator
        // codes (the default for the overwhelming majority of cards). Mirrors
        // the legacy CardDefinitionFactory.Build colour-indicator pass so the
        // JSON path is byte-identical after ToCardDef().
        if (card is Card concreteForIndicator && def.ColorIndicator.Count > 0)
        {
            var indicator = new List<ManaColor>(def.ColorIndicator.Count);
            foreach (var letter in def.ColorIndicator)
            {
                indicator.Add(ParseColorLetter(letter));
            }
            concreteForIndicator.SetColorIndicator(indicator);
        }

        foreach (var keyword in def.Keywords)
        {
            card.AddAbility(new KeywordAbility(keyword, card, owner));
        }

        foreach (var produces in def.ManaAbilities)
        {
            card.AddAbility(new ManaAbility(card, owner, ManaCost.Parse(produces)));
        }

        // PLAN 03 S2 — canonical activated / triggered / mana abilities
        // mapped from the JSON CardDefinition union by ToCardDef(). This is
        // the single interpreter that turns each CardDefAbility into a live
        // engine ability; the per-ability cost / effect / trigger builders
        // (which delegate to Costs.* / Fx.* / Triggers.*) run here against the
        // live card.
        foreach (var ability in def.Abilities)
        {
            card.AddAbility(ability.Build(card, owner, replacements));
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

    /// <summary>
    /// Compile the resolve-body declared by a <c>Resolve(...)</c> call into
    /// the engine's <see cref="IEffect"/> list. Returns an empty list when
    /// the def has no body.
    ///
    /// <para>
    /// Targets are not resolved here — targeted effects close over the
    /// supplied <paramref name="targetResolver"/>, called at resolve time
    /// with the chosen target object surfaced by the caller's
    /// <see cref="Majik.Core.Game.GameContext"/>. This matches the existing
    /// per-card factory shape (<see cref="LightningHelixFactory.BuildSpellDefinition"/>).
    /// </para>
    ///
    /// <para>
    /// When a body has more than one effect, they run in printed-text order
    /// inside a single resolution per CR 608.2c.
    /// </para>
    /// </summary>
    /// <param name="def">The card def whose body to materialize.</param>
    /// <param name="controller">The spell's controller (life-gain target,
    /// mana-pool owner, etc.).</param>
    /// <param name="targetResolver">Maps a chosen-target token (the value
    /// passed by the caller's targeting system) to the live game object
    /// (Player, Creature, …). Pass <c>t => t</c> when the chosen token
    /// already IS the live object (shape-only tests, untargeted bodies).</param>
    public static IReadOnlyList<IEffect> BuildSpellResolveEffects(
        CardDef def,
        Player controller,
        Func<object?, object?> targetResolver,
        object? chosenTarget = null,
        Majik.Core.Stack.Stack? stack = null,
        Majik.Core.Services.ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(targetResolver);

        if (def.ResolveBody is null) return Array.Empty<IEffect>();

        var result = new List<IEffect>(def.ResolveBody.Effects.Count);
        foreach (var step in def.ResolveBody.Effects)
        {
            result.Add(MaterializeStep(def, step, controller, targetResolver, chosenTarget, stack, zones));
        }
        return result;
    }

    private static IEffect MaterializeStep(
        CardDef def,
        ResolveEffect step,
        Player controller,
        Func<object?, object?> targetResolver,
        object? chosenTarget,
        Majik.Core.Stack.Stack? stack,
        Majik.Core.Services.ZoneService? zones)
    {
        // PLAN 03 — each branch converges onto the shared Fx/Costs/Triggers
        // vocabulary in Majik.Core/Primitives/ where the result is
        // byte-identical (Mill / DestroyTarget / Counter already do). The
        // remaining inline branches keep their exact boundary semantics
        // (e.g. no ≤0 guard) until the matching primitive is proven
        // behaviour-neutral for them.
        switch (step.Kind)
        {
            case ResolveEffectKind.DealDamage:
                return new Effect(
                    $"{def.Name}: deal {step.IntArg} damage to {step.Target}",
                    () =>
                    {
                        var live = targetResolver(chosenTarget);
                        if (live != null) OracleSpellBinder.DealDamage(live, step.IntArg);
                    });

            case ResolveEffectKind.PumpUntilEndOfTurn:
                return new Effect(
                    $"{def.Name}: +{step.IntArg}/+{step.IntArg2} until EOT to {step.Target}",
                    () =>
                    {
                        // CR 514.2 — register a +P/+T effect that expires at
                        // end of turn. Layer 7c modify, registered on the
                        // target's ActiveEffects. ActiveEffects null (shape
                        // tests) → silently no-op; same posture as
                        // DismemberFactory / MutagenicGrowthFactory.
                        var live = targetResolver(chosenTarget);
                        if (live is not Creature creature) return;
                        if (creature.Zone != ZoneType.Battlefield) return;
                        if (creature.ActiveEffects == null) return;
                        creature.ActiveEffects.Register(
                            new PumpUntilEndOfTurnEffect(creature, step.IntArg, step.IntArg2));
                    });

            case ResolveEffectKind.DestroyTarget:
                return new Effect(
                    $"{def.Name}: destroy {step.Target}",
                    () =>
                    {
                        // CR 701.7 — "destroy" effect. MoveToGraveyard with
                        // ZoneMoveReason.Destroy routes through the binder's
                        // indestructible (CR 702.12) / regeneration (CR
                        // 701.15) gate so neither is double-applied.
                        // CR 608.2b — illegal-target check at resolution.
                        var live = targetResolver(chosenTarget);
                        if (live is not Permanent permanent) return;
                        if (permanent.Zone != ZoneType.Battlefield) return;
                        Fx.MoveToGraveyard(permanent, ZoneMoveReason.Destroy);
                    });

            case ResolveEffectKind.Mill:
                return new Effect(
                    $"{def.Name}: mill {step.IntArg}",
                    () => Fx.Mill(controller, step.IntArg));

            case ResolveEffectKind.DrawCards:
                return new Effect(
                    $"{def.Name}: draw {step.IntArg} card(s)",
                    () =>
                    {
                        for (var i = 0; i < step.IntArg; i++)
                        {
                            var top = controller.Zones.Library.GetCards().FirstOrDefault();
                            if (top == null) return;
                            controller.Zones.Library.RemoveCard(top);
                            controller.Zones.Hand.AddCard(top);
                            top.SetZone(ZoneType.Hand);
                        }
                    });

            case ResolveEffectKind.GainLife:
                return new Effect(
                    $"{def.Name}: gain {step.IntArg} life",
                    () => controller.GainLife(step.IntArg));

            case ResolveEffectKind.LoseLife:
                return new Effect(
                    $"{def.Name}: lose {step.IntArg} life",
                    () => controller.LoseLife(step.IntArg));

            case ResolveEffectKind.Counter:
                return new Effect(
                    $"{def.Name}: counter target {step.Target}",
                    () =>
                    {
                        // CR 701.5 — counter target spell. Requires the live
                        // stack reference (passed by callers wiring real
                        // gameplay); shape-only tests pass null and the
                        // effect silently no-ops. CR 608.2b — illegal-target
                        // check at resolution: target must still be a spell
                        // on the stack, and the NoncreatureSpell filter
                        // gates creature spells out.
                        if (stack is null) { _ = targetResolver(chosenTarget); return; }
                        var live = targetResolver(chosenTarget);
                        if (live is not ISpell spell) return;
                        if (step.Target == TargetKind.NoncreatureSpell
                            && spell.Card.HasType(CardType.Creature)) return;
                        Fx.Counter(stack, spell);
                    });

            case ResolveEffectKind.AddMana:
                return new Effect(
                    $"{def.Name}: add {step.StringArg} to mana pool",
                    () => controller.AddManaToPool(ManaCost.Parse(step.StringArg ?? "")));

            case ResolveEffectKind.CreateToken:
                return new Effect(
                    $"{def.Name}: create token",
                    () =>
                    {
                        // CR 111 / CR 111.4 — token shape (name, P/T,
                        // subtypes, keywords, colour identity) lives on the
                        // TokenBlueprint payload. Route through
                        // TokenFactory.CreateOnBattlefield so ETB triggers
                        // fire and the token's colour is stamped via
                        // SetTokenColors.
                        if (step.Payload is not TokenBlueprint blueprint) return;
                        var spec = new TokenFactory.TokenSpec(
                            blueprint.Name,
                            blueprint.Power,
                            blueprint.Toughness,
                            blueprint.Subtypes,
                            blueprint.Keywords,
                            blueprint.Colors);
                        TokenFactory.CreateOnBattlefield(spec, controller, zones);
                    });

            default:
                throw new NotSupportedException(
                    $"ResolveEffectKind '{step.Kind}' is not yet supported by CardDefRuntime.");
        }
    }

    private static ArgumentException MissingStat(string cardName, string stat) =>
        new($"CardDef '{cardName}' is missing required '{stat}'.");

    // ====================================================================
    // JSON-ability materializers (PLAN 03 S2).
    //
    // The cost / effect / trigger / mana-ability builders below were moved
    // here verbatim from CardDefinitionFactory so this class is the ONE
    // interpreter the plan calls for. The JSON CardDefinition union maps onto
    // CardDefAbility builders (via ToCost() / ToResolveEffect() / ToTrigger()
    // / ToManaBuilder() on the union types) that call straight into these —
    // byte-for-byte the same logic that ran before the reroute, so the
    // runtime cards are identical (behaviour-neutral).
    // ====================================================================

    internal static ManaAbility BuildJsonManaAbility(ManaAbilityDefinition mana, ICard card, Player controller)
    {
        var produced = ManaCost.Parse(mana.Produces);

        if (string.IsNullOrWhiteSpace(mana.Cost))
        {
            return new ManaAbility(card, controller, produced);
        }

        if (card is not Permanent permanent)
        {
            throw new InvalidOperationException(
                $"Card '{card.Name}' is not a Permanent — cannot pay {{T}} for a mana ability with an additional cost.");
        }

        var extra = ManaCost.Parse(mana.Cost);
        return new ManaAbility(
            source: permanent,
            controller: controller,
            manaGenerated: produced,
            canActivateCheck: () => !permanent.IsTapped && controller.ManaPool.CanPay(extra),
            additionalCostPayer: p => p.PayMana(extra));
    }

    internal static ITriggerCondition BuildJsonTrigger(TriggerDefinition definition, ICard card) =>
        definition switch
        {
            EnterBattlefieldSelfTriggerDef => Triggers.OnEnterBattlefieldSelf(card),
            CardLeavesYourGraveyardTriggerDef gy => BuildCardLeavesYourGraveyardTrigger(gy, card),
            _ => throw new NotSupportedException(
                $"Trigger '{definition.GetType().Name}' is not yet supported by CardDefRuntime."),
        };

    private static ITriggerCondition BuildCardLeavesYourGraveyardTrigger(
        CardLeavesYourGraveyardTriggerDef def, ICard card)
    {
        var types = def.CardTypes.Select(ParseType).ToArray();
        return new EventTriggerCondition<Majik.Core.Events.CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Graveyard) return false;
            // "Your" graveyard — the controller of this trigger's source card.
            var triggerController = card.Controller;
            if (triggerController is null || !ReferenceEquals(e.Card.Owner, triggerController))
            {
                return false;
            }
            return types.Length == 0 || types.Any(t => e.Card.HasType(t));
        });
    }

    internal static ICost BuildJsonCost(CostDefinition definition, ICard card) =>
        definition switch
        {
            ManaCostDef mana => Primitives.Costs.Mana(mana.Amount),
            RemoveCounterCostDef rc => BuildRemoveCounterCost(rc, card),
            TapSelfCostDef => BuildTapSelfCost(card),
            SacrificeSelfCostDef => BuildSacrificeSelfCost(card),
            DiscardSelfCostDef => Primitives.Costs.DiscardSelf(card),
            _ => throw new NotSupportedException(
                $"Cost '{definition.GetType().Name}' is not yet supported by CardDefRuntime."),
        };

    private static ICost BuildTapSelfCost(ICard card)
    {
        if (card is not Permanent permanent)
        {
            throw new InvalidOperationException(
                $"Card '{card.Name}' is not a Permanent — cannot pay {{T}} as a cost.");
        }
        return Primitives.Costs.TapSelf(permanent);
    }

    private static ICost BuildSacrificeSelfCost(ICard card)
    {
        if (card is not Permanent permanent)
        {
            throw new InvalidOperationException(
                $"Card '{card.Name}' is not a Permanent — cannot pay 'sacrifice this' as a cost.");
        }
        return Primitives.Costs.SacrificeSelf(permanent);
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
        return Primitives.Costs.RemovePlusOnePlusOneCounter(permanent, def.Amount);
    }

    internal static IEffect BuildJsonEffect(
        EffectDefinition definition, ICard card, Player controller, ReplacementBus? replacements,
        int targetRequestIndex = -1) =>
        definition switch
        {
            PutCounterEffectDef put => BuildPutCounterEffect(put, card, replacements),
            DealDamageEffectDef damage => BuildDealDamageEffect(damage, card, targetRequestIndex),
            DrawCardEffectDef draw => BuildDrawCardEffect(draw, card, controller),
            SurveilSelfEffectDef surveil => BuildSurveilSelfEffect(surveil, card, controller),
            ScrySelfEffectDef scry => BuildScrySelfEffect(scry, card, controller),
            DestroyTargetEffectDef destroy => BuildDestroyTargetEffect(destroy, card, targetRequestIndex),
            UntapTargetEffectDef untap => BuildUntapTargetEffect(untap, card, targetRequestIndex),
            PreventDamageTargetEffectDef prevent => BuildPreventDamageTargetEffect(prevent, card, replacements, targetRequestIndex),
            GainLifeSelfEffectDef gain => BuildGainLifeSelfEffect(gain, card, controller),
            MillThenPickFirstMatchingToHandEffectDef mp => BuildMillThenPickEffect(mp, card, controller),
            ConniveSelfEffectDef connive => BuildConniveSelfEffect(connive, card),
            AmassSelfEffectDef amass => BuildAmassSelfEffect(amass, card, controller),
            _ => throw new NotSupportedException(
                $"Effect '{definition.GetType().Name}' is not yet supported by CardDefRuntime."),
        };

    /// <summary>
    /// Read the single chosen target from the resolving context at
    /// <paramref name="targetRequestIndex"/> (the slot the owning ability's
    /// <c>TargetRequests</c> reserved for this effect). Returns <c>null</c>
    /// when the index is out of range or the agent supplied no pick — the
    /// caller then fizzles cleanly (CR 608.2b). 1..1 requests carry exactly
    /// one object, so we take element [0].
    /// </summary>
    private static object? ChosenTargetAt(Majik.Core.Abilities.ResolutionContext ctx, int targetRequestIndex)
    {
        if (targetRequestIndex < 0) return null;
        var chosen = ctx.ChosenTargets;
        if (targetRequestIndex >= chosen.Count) return null;
        var slot = chosen[targetRequestIndex];
        return slot.Count == 0 ? null : slot[0];
    }

    private static IEffect BuildConniveSelfEffect(ConniveSelfEffectDef def, ICard card)
    {
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: connive x{amount}",
            () =>
            {
                if (card is not Creature creature) return;
                Fx.Connive(creature, amount);
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
                var milled = Fx.Mill(controller, amount);
                if (types.Length == 0) return;
                var pick = milled.FirstOrDefault(c => types.Any(t => c.HasType(t)));
                if (pick != null)
                {
                    controller.Zones.Graveyard.RemoveCard(pick);
                    controller.Zones.Hand.AddCard(pick);
                    pick.SetZone(ZoneType.Hand);
                }
            });
    }

    private static IEffect BuildDestroyTargetEffect(DestroyTargetEffectDef def, ICard card, int targetRequestIndex)
    {
        // PLAN 01 (Slice F) — real targeted destroy. The chosen permanent is
        // read off the resolving ability's ChosenTargets via the index the
        // ability reserved for this effect; CR 608.2b — re-check legality at
        // resolution (target must still be a battlefield permanent) and fizzle
        // otherwise. Fx.MoveToGraveyard(…, Destroy) honours Indestructible
        // (CR 702.12) / regeneration (CR 701.15).
        return new Effect(
            $"{card.Name}: destroy target {def.TargetFilter}",
            ctx =>
            {
                var live = ChosenTargetAt(ctx, targetRequestIndex);
                if (live is Permanent permanent && permanent.Zone == ZoneType.Battlefield)
                {
                    Fx.MoveToGraveyard(permanent, ZoneMoveReason.Destroy);
                }
                return ValueTask.CompletedTask;
            });
    }

    private static IEffect BuildUntapTargetEffect(UntapTargetEffectDef def, ICard card, int targetRequestIndex)
    {
        // PLAN 01 (Slice F) — real targeted untap (CR 701.21). Reads the chosen
        // permanent off ChosenTargets at the reserved index; CR 608.2b — fizzle
        // if it is no longer a battlefield permanent.
        return new Effect(
            $"{card.Name}: untap target {def.TargetFilter}",
            ctx =>
            {
                var live = ChosenTargetAt(ctx, targetRequestIndex);
                if (live is Permanent permanent && permanent.Zone == ZoneType.Battlefield)
                {
                    permanent.Untap();
                }
                return ValueTask.CompletedTask;
            });
    }

    private static IEffect BuildPreventDamageTargetEffect(
        PreventDamageTargetEffectDef def, ICard card, ReplacementBus? replacements, int targetRequestIndex)
    {
        // PLAN 01 (Slice F) — real targeted damage prevention (CR 615). Reads
        // the chosen creature off ChosenTargets at the reserved index and
        // registers a per-turn prevention shield bound to it on the
        // controller-attached ReplacementBus. CR 608.2b — fizzle (no shield)
        // if the target is no longer a battlefield creature, or if no bus is
        // attached. The shield auto-expires at cleanup via IEndOfTurnExpirable.
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: prevent next {amount} damage to target {def.TargetFilter} this turn",
            ctx =>
            {
                if (replacements != null
                    && ChosenTargetAt(ctx, targetRequestIndex) is Creature creature
                    && creature.Zone == ZoneType.Battlefield)
                {
                    replacements.Register(
                        new PreventNextNDamageToCreatureShield(creature, amount));
                }
                return ValueTask.CompletedTask;
            });
    }

    private static IEffect BuildScrySelfEffect(ScrySelfEffectDef def, ICard card, Player controller)
    {
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: scry {amount}",
            async ctx =>
            {
                var peeked = Majik.Core.Keywords.ScryAction.Peek(controller, amount);
                if (peeked.Count == 0) return;

                // PLAN 01 (Slice D) — prompt the live agent off the resolution
                // context; fall back to the registry, then all-to-bottom.
                var agent = ctx.Agent ?? Majik.Core.Players.Agents.AgentRegistry.Get(controller);
                Majik.Core.Keywords.ScryAction.ScryDecision decision;
                if (agent != null)
                {
                    decision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked, ctx.Ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    decision = new Majik.Core.Keywords.ScryAction.ScryDecision(
                        ToBottom: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }
                Fx.Scry(controller, amount, decision);
            });
    }

    private static IEffect BuildSurveilSelfEffect(SurveilSelfEffectDef def, ICard card, Player controller)
    {
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: surveil {amount}",
            async ctx =>
            {
                var peeked = Majik.Core.Keywords.SurveilAction.Peek(controller, amount);
                if (peeked.Count == 0) return;

                // PLAN 01 (Slice D) — prompt the live agent off the resolution
                // context; fall back to the registry, then all-to-graveyard.
                var agent = ctx.Agent ?? Majik.Core.Players.Agents.AgentRegistry.Get(controller);
                Majik.Core.Keywords.SurveilAction.SurveilDecision decision;
                if (agent != null)
                {
                    decision = await agent.ChooseSurveilDecisionAsync(ctx.Game, peeked, ctx.Ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    decision = new Majik.Core.Keywords.SurveilAction.SurveilDecision(
                        ToGraveyard: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
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
                    top.SetZone(ZoneType.Hand);
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

    private static IEffect BuildDealDamageEffect(DealDamageEffectDef def, ICard card, int targetRequestIndex)
    {
        // PLAN 01 (Slice F) — real targeted damage. Reads the chosen target off
        // ChosenTargets at the reserved index and routes through
        // Fx.DealDamageAny (Player / Creature / Planeswalker — CR 115.3 /
        // 306.7). CR 608.2b — a null pick (no/illegal target) fizzles.
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: deal {amount} damage to {def.Target}",
            ctx =>
            {
                var live = ChosenTargetAt(ctx, targetRequestIndex);
                if (live != null) Fx.DealDamageAny(live, amount);
                return ValueTask.CompletedTask;
            });
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

    private static CardSubtype ParseSubtype(string raw) =>
        Enum.TryParse<CardSubtype>(raw, ignoreCase: true, out var s)
            ? s
            : throw new ArgumentException($"Unknown card subtype '{raw}'.", nameof(raw));
}
