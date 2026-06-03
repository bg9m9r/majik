using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
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
    public static ICard Build(CardDef def, Player owner, ReplacementBus? replacements) =>
        Build(def, owner, replacements, continuous: null);

    /// <summary>
    /// Materialize a card from a <see cref="CardDef"/>, threading both the
    /// <see cref="ReplacementBus"/> (CR 614) and the live per-game
    /// <see cref="ContinuousEffectsService"/> to its abilities. The continuous
    /// service is consumed by ability-path verbs that register a CR 613
    /// continuous effect at resolution — currently the <c>gain_control</c>
    /// (Threaten / Zealous Conscripts) family, whose ETB / activated-ability
    /// form installs a <see cref="Majik.Core.Effects.TemporaryControlChangeEffect"/>
    /// + an until-EOT haste grant against this service. This is the ABILITY-path
    /// analogue of the <paramref name="continuous"/> argument
    /// <see cref="BuildSpellDefinitionFromEffects"/> already threads on the SPELL
    /// path. Verbs that need neither service are byte-identical to the legacy
    /// build (<c>null</c> is the no-op default).
    /// </summary>
    public static ICard Build(
        CardDef def, Player owner, ReplacementBus? replacements,
        ContinuousEffectsService? continuous)
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
            card.AddAbility(ability.Build(card, owner, replacements, continuous));
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

    /// <summary>
    /// Compile a fluent <c>.Resolve(...)</c> body into a full
    /// <see cref="SpellDefinition"/> — the targeted-spell tier that previously
    /// required a hand-written factory. Each targeted resolve step
    /// (<see cref="ResolveEffect.Target"/> non-null) emits one
    /// <see cref="TargetRequest"/> in printed order; the returned
    /// <see cref="SpellDefinition.EffectFactory"/> reads the caster's chosen
    /// target for each slot, runs the resolver, and materializes the step onto
    /// the shared <see cref="Majik.Core.Primitives.Fx"/> vocabulary. Untargeted
    /// steps (Mill / Draw / GainLife / AddMana / CreateToken) materialize
    /// directly.
    ///
    /// <para>
    /// This is the one-stop bridge that lets a targeted instant / sorcery be
    /// declared in ~10 fluent lines instead of a ~60-line bespoke
    /// <c>BuildSpellDefinition</c>:
    /// </para>
    /// <code>
    /// public static SpellDefinition BuildSpellDefinition(Func&lt;object, object&gt; resolver) =>
    ///     CardDefRuntime.BuildSpellDefinition(Define(), resolver);
    /// // where Define() = CardDef.Instant("Shock", "{R}")
    /// //                      .Resolve(c => c.DealDamage(2).To(TargetKind.AnyTarget));
    /// </code>
    /// </summary>
    /// <param name="def">A card def carrying a <c>Resolve(...)</c> body.</param>
    /// <param name="resolver">Maps a chosen-target token to the live game
    /// object (Player / Creature / spell on the stack). Pass <c>o =&gt; o</c>
    /// in tests that hand engine objects directly.</param>
    /// <param name="controller">The spell's controller — the recipient of
    /// controller-scoped untargeted steps (Mill / Draw / GainLife / LoseLife /
    /// AddMana / CreateToken). Required when the body has any such step;
    /// omit for purely target-only bodies (Shock-style burn, removal,
    /// counters). When null, the cast flow's
    /// <see cref="ChosenSpellParams.AllPlayers"/> first entry is used as a
    /// best-effort fallback.</param>
    /// <param name="stack">Live stack — required only for <c>Counter</c>
    /// bodies (the countered spell lives there). Null elsewhere.</param>
    /// <param name="zones">Zone service — required only for <c>CreateToken</c>
    /// bodies. Null elsewhere.</param>
    public static SpellDefinition BuildSpellDefinition(
        CardDef def,
        Func<object, object> resolver,
        Player? controller = null,
        Majik.Core.Stack.Stack? stack = null,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(resolver);
        if (def.ResolveBody is null)
        {
            throw new InvalidOperationException(
                $"CardDef '{def.Name}' has no Resolve(...) body — cannot build a SpellDefinition. " +
                "Declare effects via .Resolve(c => ...) before calling BuildSpellDefinition.");
        }

        var steps = def.ResolveBody.Effects;

        // One TargetRequest per targeted step, in printed order. The slot index
        // each step reads at resolution is its position in this list.
        var targetRequests = new List<TargetRequest>();
        // Parallel to `steps`: the target-slot index for a targeted step, or -1
        // for an untargeted step.
        var slotForStep = new int[steps.Count];
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.Target is { } kind)
            {
                slotForStep[i] = targetRequests.Count;
                targetRequests.Add(BuildTargetRequest(kind));
            }
            else
            {
                slotForStep[i] = -1;
            }
        }

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: targetRequests,
            EffectFactory: chosen =>
            {
                // Controller scope for untargeted steps: prefer the explicit
                // caster, else the cast flow's AllPlayers[0] (best-effort), else
                // a throwaway zero-life player so the materializers' null-guards
                // engage instead of NRE-ing in pure-shape tests.
                var liveController = controller
                    ?? (chosen.AllPlayers is { Count: > 0 } all ? all[0] : null)
                    ?? (_fallbackController ??= new Player("(unspecified)", 0));

                var effects = new List<IEffect>(steps.Count);
                for (var i = 0; i < steps.Count; i++)
                {
                    var step = steps[i];
                    var slot = slotForStep[i];
                    object? chosenTarget = null;
                    if (slot >= 0 && slot < chosen.Targets.Count)
                    {
                        var picks = chosen.Targets[slot];
                        if (picks.Count > 0) chosenTarget = picks[0];
                    }

                    effects.Add(MaterializeStep(
                        def, step, liveController, resolver!, chosenTarget, stack, zones));
                }
                return effects;
            });
    }

    [ThreadStatic]
    private static Player? _fallbackController;

    /// <summary>
    /// Build a shared-slot rider effect (CR 608.2g — last-known information).
    /// The host effect at the shared slot runs first in printed order and may
    /// move the target off the battlefield, so the rider snapshots its victim
    /// NOW (resolution start, targets locked, host not yet run) rather than
    /// reading post-host state. An illegal target (not a battlefield permanent /
    /// player) snapshots no victim, so the rider fizzles cleanly with its host.
    /// v1 supports the <see cref="LoseLifeTargetEffectDef"/> rider.
    /// </summary>
    private static IEffect BuildSharedSlotRider(
        EffectDefinition effect, ChosenSpellParams chosen, int slot)
    {
        var picks = slot < chosen.Targets.Count
            ? chosen.Targets[slot]
            : (IReadOnlyList<object>)Array.Empty<object>();
        var pick = picks.Count > 0 ? picks[0] : null;

        return effect switch
        {
            LoseLifeTargetEffectDef lose => BuildLoseLifeRiderSnapshot(lose, pick),
            _ => throw new NotSupportedException(
                $"Shared-slot rider '{effect.GetType().Name}' is not yet supported by CardDefRuntime."),
        };
    }

    private static IEffect BuildLoseLifeRiderSnapshot(LoseLifeTargetEffectDef def, object? pick)
    {
        // CR 608.2g — capture last-known controller before the host effect runs.
        var victim = pick switch
        {
            Player player => player,
            Permanent permanent when permanent.Zone == ZoneType.Battlefield
                => permanent.Controller,
            _ => null,
        };
        var amount = def.Amount;
        return new Effect(
            $"its controller loses {amount} life",
            () => { if (victim != null) Fx.LoseLife(victim, amount); });
    }

    /// <summary>
    /// Declarative SPELL-effect bridge — compile an ability-side
    /// <see cref="EffectDefinition"/> list into a full
    /// <see cref="SpellDefinition"/> so an instant / sorcery reuses the SAME
    /// targeted verbs (<c>return_to_hand</c> / <c>deal_damage</c> /
    /// <c>destroy_target</c> / …) that activated + triggered abilities already
    /// use — no bespoke C# resolve closure. This is the spell analogue of the
    /// ability path: <see cref="ActivatedAbilityDefinition.ToCardDefAbility"/>
    /// pairs each effect's <see cref="EffectDefinition.ToTargetRequest"/> with
    /// its <see cref="EffectDefinition.ToResolveEffect"/> builder, and
    /// <see cref="CardDefAbilityEffects.Materialize"/> threads the chosen target
    /// off <see cref="ResolutionContext.ChosenTargets"/>. Here the spell's cast
    /// flow collects the targets into <see cref="ChosenSpellParams.Targets"/>
    /// instead; this method re-presents the per-effect pick to the SAME shared
    /// effect builder via a thin <see cref="SpellTargetedEffect"/> adapter, so
    /// the resolve logic (and its CR 608.2b illegal-target fizzle) is
    /// byte-identical to the ability path.
    ///
    /// <para>
    /// Each targeted effect (<see cref="EffectDefinition.ToTargetRequest"/> non-
    /// null) contributes one <see cref="TargetRequest"/> in printed order; the
    /// slot it reads at resolution is its index in that list. Untargeted effects
    /// (Draw / GainLife / Scry / …) resolve against the caster directly. Mirrors
    /// the bespoke spell factories (Unsummon / Vapor Snag) the path replaces.
    /// </para>
    /// </summary>
    /// <param name="cardName">The spell's name — woven into effect
    /// descriptions only (the resolve behaviour never reads the card).</param>
    /// <param name="effects">The ability-side effect verbs to resolve in
    /// printed-text order (CR 608.2c).</param>
    /// <param name="replacements">Optional replacement bus for effects that
    /// register CR 614/615 replacements (prevention shields, counters). Null
    /// for the targeted-removal / burn / bounce verbs in scope.</param>
    public static SpellDefinition BuildSpellDefinitionFromEffects(
        string cardName,
        IReadOnlyList<EffectDefinition> effects,
        ReplacementBus? replacements = null,
        ContinuousEffectsService? continuous = null)
    {
        ArgumentNullException.ThrowIfNull(cardName);
        ArgumentNullException.ThrowIfNull(effects);

        // A lightweight stand-in card carrying just the name. The shared effect
        // builders read it only for their Description string — never for resolve
        // behaviour — so a bare Instant is sufficient and keeps this method free
        // of a live cast card (BuildDefinition is called before the cast card
        // exists, exactly like the bespoke spell factories).
        var nameCard = new Instant(cardName, "");

        // One TargetRequest per targeted effect, in printed order. slotForEffect
        // is parallel to `effects`: the spell-target slot a targeted effect reads
        // at resolution, or -1 for an untargeted effect.
        var targetRequests = new List<TargetRequest>();
        var slotForEffect = new int[effects.Count];
        // CR 701.12 fight (source: "target") — whether each effect declared a
        // SECOND contiguous slot (the "other" creature) right after its
        // primary. The adapter below then re-presents both picks.
        var hasExtraSlot = new bool[effects.Count];
        // The slot index of the most-recently declared targeted effect, so a
        // rider verb (SharesPreviousTargetSlot — e.g. Vapor Snag's "its
        // controller loses 1 life") reuses it instead of declaring a new
        // target. -1 until the first targeted effect appears.
        var lastTargetedSlot = -1;
        for (var i = 0; i < effects.Count; i++)
        {
            var effect = effects[i];
            var request = effect.ToTargetRequest();
            if (request is not null)
            {
                slotForEffect[i] = targetRequests.Count;
                lastTargetedSlot = targetRequests.Count;
                targetRequests.Add(request);
                var extra = effect.ToExtraTargetRequest();
                if (extra is not null)
                {
                    hasExtraSlot[i] = true;
                    targetRequests.Add(extra);
                }
            }
            else if (effect.SharesPreviousTargetSlot)
            {
                // Rider — reuse the preceding targeted effect's slot (no new
                // TargetRequest). Falls back to untargeted if it is the first
                // effect (no slot to share).
                slotForEffect[i] = lastTargetedSlot;
            }
            else
            {
                slotForEffect[i] = -1;
            }
        }

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: targetRequests,
            EffectFactory: chosen =>
            {
                // Controller scope for untargeted effects: the cast flow's
                // AllPlayers[0] is the caster (CR 601.2 — the spell's controller),
                // else a throwaway player so untargeted builders' null guards
                // engage in pure-shape tests instead of NRE-ing.
                var controller = (chosen.AllPlayers is { Count: > 0 } all ? all[0] : null)
                    ?? (_fallbackController ??= new Player("(unspecified)", 0));

                var built = new IEffect[effects.Count];
                for (var i = 0; i < effects.Count; i++)
                {
                    var slot = slotForEffect[i];

                    // Shared-slot rider (Vapor Snag's "its controller loses N
                    // life"): the host effect (the bounce) runs first in printed
                    // order and moves the target off the battlefield, so a
                    // resolution-time controller read would see the post-bounce
                    // state. CR 608.2g — "its controller" uses LAST-KNOWN
                    // information from immediately before the target left. We
                    // snapshot it NOW, at resolution start (targets locked, host
                    // not yet run), exactly like the bespoke factory captured the
                    // controller before its zone move. An illegal target (never a
                    // battlefield permanent) snapshots no victim → the rider
                    // fizzles with its host.
                    if (slot >= 0 && effects[i].SharesPreviousTargetSlot)
                    {
                        built[i] = BuildSharedSlotRider(effects[i], chosen, slot);
                        continue;
                    }

                    // The shared ability builder reads its target from
                    // ChosenTargets[targetRequestIndex]; we always present the
                    // pick at index 0 of a single-slot context (the adapter
                    // below), so the builder is invoked with index 0.
                    var inner = effects[i].ToResolveEffect(continuous)(
                        nameCard, controller, replacements,
                        slot >= 0 ? 0 : -1);

                    if (slot < 0)
                    {
                        // Untargeted effect — resolve unchanged.
                        built[i] = inner;
                        continue;
                    }

                    // CR 601.2c — the spell's chosen target for THIS slot. Wrap
                    // the shared effect so at resolution it sees the pick at
                    // ChosenTargets[0], reusing the ability path's CR 608.2b
                    // legality re-check verbatim.
                    var picks = slot < chosen.Targets.Count
                        ? chosen.Targets[slot]
                        : (IReadOnlyList<object>)Array.Empty<object>();

                    if (hasExtraSlot[i])
                    {
                        // CR 701.12 fight — a two-slot verb reads its primary
                        // pick at index 0 and its extra pick at index 1. Present
                        // both contiguous spell slots so the shared builder
                        // (invoked with index 0) sees [fighter, other].
                        var extraSlot = slot + 1;
                        var extraPicks = extraSlot < chosen.Targets.Count
                            ? chosen.Targets[extraSlot]
                            : (IReadOnlyList<object>)Array.Empty<object>();
                        built[i] = new SpellTargetedEffect(inner, picks, extraPicks);
                        continue;
                    }

                    built[i] = new SpellTargetedEffect(inner, picks);
                }
                return built;
            });
    }

    /// <summary>
    /// Adapter that re-presents a spell's per-slot chosen target to a shared
    /// ability <see cref="IEffect"/> at resolution. The wrapped effect reads its
    /// target off <see cref="ResolutionContext.ChosenTargets"/> index 0; this
    /// adapter rebuilds the context with the spell's picks in that slot before
    /// delegating, so the ability effect's resolve + CR 608.2b illegal-target
    /// fizzle run byte-identically on the spell path.
    /// </summary>
    private sealed class SpellTargetedEffect : IEffect
    {
        private readonly IEffect _inner;
        private readonly IReadOnlyList<object> _picks;
        // CR 701.12 fight (source: "target") — the second contiguous slot's
        // picks (the "other" creature), or null for the common single-slot
        // case. When present the inner builder reads slot 0 (primary) + slot 1.
        private readonly IReadOnlyList<object>? _extraPicks;

        internal SpellTargetedEffect(
            IEffect inner,
            IReadOnlyList<object> picks,
            IReadOnlyList<object>? extraPicks = null)
        {
            _inner = inner;
            _picks = picks;
            _extraPicks = extraPicks;
        }

        public string Description => _inner.Description;

        public ValueTask ExecuteAsync(ResolutionContext ctx)
        {
            var chosen = _extraPicks is null
                ? new[] { _picks }
                : new[] { _picks, _extraPicks };
            var scoped = ctx with { ChosenTargets = chosen };
            return _inner.ExecuteAsync(scoped);
        }
    }

    /// <summary>
    /// Map a DSL <see cref="TargetKind"/> onto a live
    /// <see cref="TargetRequest"/> (1..1, with the matching candidate gatherer
    /// + bot intent). Mirrors the hand-written request shapes the bespoke
    /// factories emit (Shock's "any target", Negate's "target noncreature
    /// spell", etc.). Spell-targeting kinds leave the static candidate pool
    /// empty — the cast flow gathers stack spells just as the bespoke
    /// counterspell factories do.
    /// </summary>
    private static TargetRequest BuildTargetRequest(TargetKind kind) => kind switch
    {
        TargetKind.AnyTarget => new TargetRequest(
            "any target", 1, 1, Array.Empty<object>(), BotIntent.Burn,
            CandidateGatherer: AnyTargetCandidates),

        TargetKind.Creature => new TargetRequest(
            "target creature", 1, 1, Array.Empty<object>(), BotIntent.Removal,
            CandidateGatherer: ctx => CreaturesWhere(ctx, _ => true)),

        TargetKind.OpponentCreature => new TargetRequest(
            "target creature an opponent controls", 1, 1, Array.Empty<object>(), BotIntent.Removal,
            CandidateGatherer: ctx => CreaturesWhere(ctx,
                c => !ReferenceEquals(c.Controller, ctx.Self))),

        TargetKind.NonblackCreature => new TargetRequest(
            "target nonblack creature", 1, 1, Array.Empty<object>(), BotIntent.Removal,
            CandidateGatherer: ctx => CreaturesWhere(ctx,
                c => !CardColors.GetColors(c).Contains(ManaColor.Black))),

        TargetKind.NonblackNonartifactCreature => new TargetRequest(
            "target nonblack, nonartifact creature", 1, 1, Array.Empty<object>(), BotIntent.Removal,
            CandidateGatherer: ctx => CreaturesWhere(ctx,
                c => !CardColors.GetColors(c).Contains(ManaColor.Black)
                  && !c.HasType(CardType.Artifact))),

        TargetKind.Permanent => new TargetRequest(
            "target permanent", 1, 1, Array.Empty<object>(), BotIntent.Removal,
            CandidateGatherer: ctx => ctx.AllPlayers
                .SelectMany(p => p.Zones.Battlefield.GetCards())
                .OfType<Permanent>()
                .Cast<object>()
                .ToList()),

        TargetKind.Player => new TargetRequest(
            "target player", 1, 1, Array.Empty<object>(), BotIntent.None,
            CandidateGatherer: ctx => ctx.AllPlayers.Cast<object>().ToList()),

        TargetKind.Opponent => new TargetRequest(
            "target opponent", 1, 1, Array.Empty<object>(), BotIntent.None,
            CandidateGatherer: ctx => ctx.AllPlayers
                .Where(p => !ReferenceEquals(p, ctx.Self))
                .Cast<object>()
                .ToList()),

        TargetKind.CreatureOrPlayer => new TargetRequest(
            "target creature or player", 1, 1, Array.Empty<object>(), BotIntent.Burn,
            CandidateGatherer: ctx => ctx.AllPlayers
                .SelectMany(p => p.Zones.Battlefield.GetCards())
                .OfType<Creature>()
                .Cast<object>()
                .Concat(ctx.AllPlayers.Cast<object>())
                .ToList()),

        // Counter targets a spell on the stack. The bespoke counterspell
        // factories leave LegalCandidates empty (the cast flow gathers stack
        // spells); match that posture.
        TargetKind.Spell => new TargetRequest(
            "target spell", 1, 1, Array.Empty<object>(), BotIntent.Counter),

        TargetKind.NoncreatureSpell => new TargetRequest(
            "target noncreature spell", 1, 1, Array.Empty<object>(), BotIntent.Counter),

        _ => throw new NotSupportedException(
            $"TargetKind '{kind}' is not yet mapped to a TargetRequest by CardDefRuntime."),
    };

    private static IReadOnlyList<object> AnyTargetCandidates(GameContext ctx) =>
        ctx.AllPlayers
            .SelectMany(p => p.Zones.Battlefield.GetCards())
            .Where(c => c.HasType(CardType.Creature) || c.HasType(CardType.Planeswalker))
            .Cast<object>()
            .Concat(ctx.AllPlayers.Cast<object>())
            .ToList();

    private static IReadOnlyList<object> CreaturesWhere(GameContext ctx, Func<Creature, bool> filter) =>
        ctx.AllPlayers
            .SelectMany(p => p.Zones.Battlefield.GetCards())
            .OfType<Creature>()
            .Where(filter)
            .Cast<object>()
            .ToList();

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
                        // CR 119 + CR 306.7 — route through Fx.DealDamageAny so a
                        // Planeswalker target takes loyalty removal (the
                        // "any target" resolution shape Shock / Lightning Bolt
                        // use). For a Player / Creature the result is identical
                        // to the raw binder call. CR 608.2b — a null pick (no /
                        // illegal target) fizzles.
                        var live = targetResolver(chosenTarget);
                        if (live != null) Fx.DealDamageAny(live, step.IntArg);
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
            WheneverYouGainLifeTriggerDef => BuildWheneverYouGainLifeTrigger(card),
            WheneverYouCastSpellTriggerDef cast => BuildWheneverYouCastSpellTrigger(cast, card),
            CastSelfTriggerDef => Triggers.OnCastSelf(card),
            AttacksSelfTriggerDef => Triggers.OnAttackSelf(card),
            DiesSelfTriggerDef => Triggers.OnDies(card),
            AtBeginningOfYourUpkeepTriggerDef =>
                BuildStepBeginTrigger(card, Majik.Core.StateMachine.PhaseStateType.Upkeep),
            AtBeginningOfYourEndStepTriggerDef =>
                BuildStepBeginTrigger(card, Majik.Core.StateMachine.PhaseStateType.End),
            WheneverAnotherCreatureEntersTriggerDef anotherEnters =>
                BuildAnotherCreatureEntersTrigger(anotherEnters, card),
            WheneverAnotherCreatureDiesTriggerDef anotherDies =>
                BuildAnotherCreatureDiesTrigger(anotherDies, card),
            DealsCombatDamageToPlayerSelfTriggerDef =>
                BuildDealsCombatDamageToPlayerSelfTrigger(card),
            WheneverACreatureYouControlExploresTriggerDef =>
                BuildWheneverACreatureYouControlExploresTrigger(card),
            _ => throw new NotSupportedException(
                $"Trigger '{definition.GetType().Name}' is not yet supported by CardDefRuntime."),
        };

    /// <summary>
    /// CR 701.40e / CR 603.1 — "Whenever a creature you control explores, …".
    /// Fires on a <see cref="Majik.Core.Events.CreatureExploredEvent"/> whose
    /// <see cref="Majik.Core.Events.CreatureExploredEvent.Controller"/> is the
    /// trigger's controller (CR 109.5 — "a creature you control"; resolved live
    /// so a control change carries the trigger). The same predicate the
    /// hand-rolled Wildgrowth Walker factory uses.
    /// </summary>
    private static ITriggerCondition BuildWheneverACreatureYouControlExploresTrigger(ICard card) =>
        new EventTriggerCondition<Majik.Core.Events.CreatureExploredEvent>((e, _) =>
        {
            var controller = card.Controller;
            return controller is not null && ReferenceEquals(e.Controller, controller);
        });

    /// <summary>
    /// CR 500.1 / CR 603.1 — "At the beginning of your [step], …". Fires on a
    /// <see cref="Majik.Core.Events.StepStartedEvent"/> for the requested step
    /// whose active player is the trigger's controller. The controller is
    /// resolved live (<c>card.Controller</c>) at fire time so a control change
    /// carries the trigger (CR 109.5) — the same live-controller predicate as
    /// <see cref="BuildWheneverYouGainLifeTrigger"/>; equivalent to
    /// <see cref="Triggers.OnStepBegin"/> with the controller bound late.
    /// </summary>
    private static ITriggerCondition BuildStepBeginTrigger(
        ICard card, Majik.Core.StateMachine.PhaseStateType step) =>
        new EventTriggerCondition<Majik.Core.Events.StepStartedEvent>((e, _) =>
        {
            var controller = card.Controller;
            return controller is not null
                && e.StepType == step
                && ReferenceEquals(e.Player, controller);
        });

    /// <summary>
    /// CR 603.6e — "Whenever another creature [you control] enters, …". Fires on
    /// a <see cref="Majik.Core.Events.CardMovedEvent"/> → Battlefield where the
    /// entering card is a creature OTHER than this permanent. When
    /// <see cref="WheneverAnotherCreatureEntersTriggerDef.YouControlOnly"/> is
    /// set, the entering creature must also be controlled by the trigger's
    /// controller (resolved live, CR 109.5) — equivalent to
    /// <see cref="Triggers.OnAnotherCreatureYouControlEnters"/> with the
    /// controller bound late. Otherwise any creature entering fires it
    /// (<see cref="Triggers.OnAnyCreatureEntersBattlefield"/> plus the self
    /// exclusion).
    /// </summary>
    private static ITriggerCondition BuildAnotherCreatureEntersTrigger(
        WheneverAnotherCreatureEntersTriggerDef def, ICard card)
    {
        var youControlOnly = def.YouControlOnly;
        var includeSelf = def.IncludeSelf;
        var subtype = ParseOptionalSubtype(def.Subtype);
        return new EventTriggerCondition<Majik.Core.Events.CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;
            // "ANOTHER creature" excludes the source unless includeSelf models
            // "this creature OR another …" (CR 603.6e — Mardu Woe-Reaper).
            if (!includeSelf && ReferenceEquals(e.Card, card)) return false;
            if (subtype is not null && !e.Card.HasSubtype(subtype.Value)) return false;
            if (!youControlOnly) return true;
            var controller = card.Controller;
            return controller is not null
                && ReferenceEquals(e.Card.Controller, controller);
        });
    }

    /// <summary>
    /// CR 603.6e / CR 700.4 — "Whenever another creature [you control] dies, …".
    /// The aristocrat death-payoff mirror of
    /// <see cref="BuildAnotherCreatureEntersTrigger"/>. Fires on a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> Battlefield → Graveyard
    /// of a creature OTHER than this permanent, with the optional youControlOnly
    /// (CR 109.5, resolved live) / nontokenOnly (CR 111.7) / subtype (CR 205.3)
    /// filters AND-composed — the same predicate the hand-rolled Blood Artist /
    /// Zulaport Cutthroat / Midnight Reaper factories use.
    /// </summary>
    private static ITriggerCondition BuildAnotherCreatureDiesTrigger(
        WheneverAnotherCreatureDiesTriggerDef def, ICard card)
    {
        var youControlOnly = def.YouControlOnly;
        var nontokenOnly = def.NontokenOnly;
        var subtype = ParseOptionalSubtype(def.Subtype);
        return new EventTriggerCondition<Majik.Core.Events.CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;
            if (ReferenceEquals(e.Card, card)) return false;
            if (nontokenOnly && e.Card is Permanent { IsToken: true }) return false;
            if (subtype is not null && !e.Card.HasSubtype(subtype.Value)) return false;
            if (!youControlOnly) return true;
            var controller = card.Controller;
            return controller is not null
                && ReferenceEquals(e.Card.Controller, controller);
        });
    }

    /// <summary>
    /// CR 510.2 / CR 603.1 — "Whenever this creature deals combat damage to a
    /// player, …". Fires on a
    /// <see cref="Majik.Core.Domain.DomainEvents.CombatDamageDealtEvent"/> whose
    /// <see cref="Majik.Core.Domain.DomainEvents.CombatDamageDealtEvent.Source"/>
    /// is this card and whose target is a player (non-null
    /// <see cref="Majik.Core.Events.DamageDealtEvent.TargetPlayer"/>) — the same
    /// predicate Ragavan's hand-rolled trigger uses. The source is always the
    /// card the ability lives on (self-scoped), so no controller read is needed.
    /// </summary>
    private static ITriggerCondition BuildDealsCombatDamageToPlayerSelfTrigger(ICard card) =>
        new EventTriggerCondition<Majik.Core.Domain.DomainEvents.CombatDamageDealtEvent>((e, _) =>
            ReferenceEquals(e.Source, card) && e.TargetPlayer != null);

    /// <summary>
    /// CR 119.3 — "Whenever you gain life, …". Fires on a
    /// <see cref="Majik.Core.Events.LifeChangedEvent"/> for the trigger's
    /// controller where the life total strictly increased. The controller is
    /// resolved live (<c>card.Controller</c>) at fire time so a control change
    /// carries the trigger (CR 109.5); the same predicate as
    /// <see cref="Triggers.OnLifeGainedByPlayer"/>.
    /// </summary>
    private static ITriggerCondition BuildWheneverYouGainLifeTrigger(ICard card) =>
        new EventTriggerCondition<Majik.Core.Events.LifeChangedEvent>((e, _) =>
        {
            var controller = card.Controller;
            return controller is not null
                && ReferenceEquals(e.Player, controller)
                && e.NewLife > e.PreviousLife;
        });

    /// <summary>
    /// CR 601.2 / 603.1 — "Whenever you cast a [type] spell, …". Fires on a
    /// <see cref="Majik.Core.Domain.DomainEvents.SpellCastEvent"/> whose
    /// spell controller is the trigger's controller (CR 109.5). The optional
    /// <see cref="WheneverYouCastSpellTriggerDef.NoncreatureOnly"/> (CR 112.1)
    /// and <see cref="WheneverYouCastSpellTriggerDef.SpellTypes"/> (any-of,
    /// logical OR) filters are AND-composed onto the controller scope.
    /// Controller resolved live so a control change carries the trigger.
    /// </summary>
    private static ITriggerCondition BuildWheneverYouCastSpellTrigger(
        WheneverYouCastSpellTriggerDef def, ICard card)
    {
        var noncreatureOnly = def.NoncreatureOnly;
        var spellTypes = def.SpellTypes.Select(ParseType).ToArray();
        return new EventTriggerCondition<Majik.Core.Domain.DomainEvents.SpellCastEvent>((e, _) =>
        {
            var controller = card.Controller;
            if (controller is null || !ReferenceEquals(e.Spell.Controller, controller))
            {
                return false;
            }
            if (noncreatureOnly && e.Spell.Card.HasType(CardType.Creature))
            {
                return false;
            }
            return spellTypes.Length == 0 || spellTypes.Any(t => e.Spell.Card.HasType(t));
        });
    }

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
            SacrificeArtifactCostDef sa => Primitives.Costs.SacrificeAnArtifact(sa.Nontoken),
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
        int targetRequestIndex = -1, ContinuousEffectsService? continuous = null) =>
        definition switch
        {
            PutCounterEffectDef put => BuildPutCounterEffect(put, card, replacements),
            DealDamageEffectDef damage => BuildDealDamageEffect(damage, card, targetRequestIndex),
            DrawCardEffectDef draw => BuildDrawCardEffect(draw, card, controller),
            SurveilSelfEffectDef surveil => BuildSurveilSelfEffect(surveil, card, controller),
            ScrySelfEffectDef scry => BuildScrySelfEffect(scry, card, controller),
            DestroyTargetEffectDef destroy => BuildDestroyTargetEffect(destroy, card, targetRequestIndex),
            ExileTargetEffectDef exile => BuildExileTargetEffect(exile, card, targetRequestIndex),
            MayExileTargetCardThenGainLifeEffectDef mayExile =>
                BuildMayExileTargetCardThenGainLifeEffect(mayExile, card, controller, targetRequestIndex),
            ExileUntilLeavesEffectDef exileUntil => BuildExileUntilLeavesEffect(exileUntil, card, controller, targetRequestIndex),
            ReturnToHandEffectDef bounce => BuildReturnToHandEffect(bounce, card, targetRequestIndex),
            UntapTargetEffectDef untap => BuildUntapTargetEffect(untap, card, targetRequestIndex),
            TapTargetEffectDef tap => BuildTapTargetEffect(tap, card, targetRequestIndex),
            PreventDamageTargetEffectDef prevent => BuildPreventDamageTargetEffect(prevent, card, replacements, targetRequestIndex),
            GainControlEffectDef control => BuildGainControlEffect(control, card, controller, targetRequestIndex, continuous),
            FightEffectDef fight => BuildFightEffect(fight, card, targetRequestIndex),
            ExploreSelfEffectDef exploreSelf => BuildExploreSelfEffect(exploreSelf, card, controller),
            ExploreTargetEffectDef explore => BuildExploreTargetEffect(explore, card, targetRequestIndex),
            PumpTargetEffectDef pump => BuildPumpTargetEffect(pump, card, targetRequestIndex),
            PumpSelfEffectDef pumpSelf => BuildPumpSelfEffect(pumpSelf, card),
            GrantKeywordUntilEotTargetEffectDef grant => BuildGrantKeywordUntilEotTargetEffect(grant, card, targetRequestIndex),
            BecomesArtifactTargetEffectDef artifact => BuildBecomesArtifactTargetEffect(artifact, card, targetRequestIndex),
            DamageAndTapEachFlyerOpponentsControlEffectDef flyers =>
                BuildDamageAndTapEachFlyerOpponentsControlEffect(flyers, card),
            GainLifeSelfEffectDef gain => BuildGainLifeSelfEffect(gain, card, controller),
            LoseLifeSelfEffectDef loseSelf => BuildLoseLifeSelfEffect(loseSelf, card, controller),
            LoseLifeTargetEffectDef lose => BuildLoseLifeTargetEffect(lose, card, targetRequestIndex),
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

    private static IEffect BuildLoseLifeSelfEffect(LoseLifeSelfEffectDef def, ICard card, Player controller)
    {
        // CR 119.3 — untargeted "you lose N life". Routes through the shared
        // Fx.LoseLife primitive (Player.LoseLife) against the ability's
        // controller. The mirror of BuildGainLifeSelfEffect.
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: lose {amount} life",
            () => Fx.LoseLife(controller, amount));
    }

    private static IEffect BuildLoseLifeTargetEffect(LoseLifeTargetEffectDef def, ICard card, int targetRequestIndex)
    {
        // CR 119.3 — the targeted life-drain rider. Reads the chosen target off
        // ChosenTargets at the reserved index (its own slot when subject is
        // "target", or the SHARED slot of the preceding targeted effect when
        // subject is "controller"). The life loss lands on:
        //   - the chosen Player directly (a player's "controller" is itself), or
        //   - the chosen Permanent's controller ("its controller loses N life").
        //
        // CR 608.2g — LAST-KNOWN information. The host effect (e.g. the bounce
        // half of a Vapor-Snag-style ability) runs FIRST in printed order and
        // moves the shared target off the battlefield, so by the time this rider
        // runs the permanent is already in its owner's hand. "Its controller"
        // therefore uses the controller the permanent had immediately before it
        // left. We read that from ResolutionContext.SharedSlotControllers, which
        // snapshots each chosen slot's controller at resolution START (gated on
        // Zone == Battlefield) — the ability-path analogue of the SPELL bridge's
        // pre-host snapshot. So a LEGAL bounce still drains, while a target that
        // left in RESPONSE (never captured) fizzles cleanly (CR 608.2b). A
        // Player target ("target player loses N life") drains itself directly
        // and needs no snapshot.
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: target loses {amount} life",
            ctx =>
            {
                var live = ChosenTargetAt(ctx, targetRequestIndex);
                var victim = live switch
                {
                    Player player => player,
                    Permanent => targetRequestIndex >= 0
                        && ctx.SharedSlotControllers.TryGetValue(targetRequestIndex, out var c)
                        ? c
                        : null,
                    _ => null,
                };
                if (victim != null) Fx.LoseLife(victim, amount);
                return ValueTask.CompletedTask;
            });
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
        // ability reserved for this effect; CR 608.2b — re-check the SAME filter
        // predicate at resolution (via TargetFilters.Matches), so a type-gated
        // "destroy target artifact / enchantment / …" instant fizzles cleanly
        // when the chosen object no longer matches its printed type (the mirror
        // of BuildExileTargetEffect's re-check). Fx.MoveToGraveyard(…, Destroy)
        // honours Indestructible (CR 702.12) / regeneration (CR 701.15).
        var filter = def.TargetFilter;
        return new Effect(
            $"{card.Name}: destroy target {filter}",
            ctx =>
            {
                var live = ChosenTargetAt(ctx, targetRequestIndex);
                if (live is Permanent permanent
                    && permanent.Zone == ZoneType.Battlefield
                    && TargetFilters.Matches(filter, permanent))
                {
                    Fx.MoveToGraveyard(permanent, ZoneMoveReason.Destroy);
                }
                return ValueTask.CompletedTask;
            });
    }

    private static IEffect BuildExileTargetEffect(ExileTargetEffectDef def, ICard card, int targetRequestIndex)
    {
        // CR 701.21 — real targeted exile. The mirror of BuildDestroyTargetEffect
        // onto the exile primitive (Fx.MoveToExile). Reads the chosen target off
        // ChosenTargets at the reserved index; CR 608.2b — re-check the SAME
        // filter predicate at resolution (via TargetFilters.Matches) so a
        // conditional filter (nonbasic_land / colour / mana-value) and the
        // graveyard filters (card_in_graveyard / creature_card_in_graveyard)
        // fizzle cleanly when the target no longer matches. Exile bypasses
        // Indestructible (CR 702.12) and regeneration (CR 701.15) — the card
        // moves regardless. Fx.MoveToExile handles every source zone the filters
        // produce (battlefield permanents AND graveyard cards).
        var filter = def.TargetFilter;
        return new Effect(
            $"{card.Name}: exile target {filter}",
            ctx =>
            {
                var live = ChosenTargetAt(ctx, targetRequestIndex);
                if (live is ICard target && TargetFilters.Matches(filter, target))
                {
                    Fx.MoveToExile(target);
                }
                return ValueTask.CompletedTask;
            });
    }

    private static IEffect BuildMayExileTargetCardThenGainLifeEffect(
        MayExileTargetCardThenGainLifeEffectDef def, ICard card, Player controller, int targetRequestIndex)
    {
        // CR 603.6e / CR 701.21 / CR 119.3 — "you may exile target [filter]. If
        // you do, you gain N life." (Mardu Woe-Reaper). The optional target slot
        // is declared with MinTargets: 0 (the "may"), so a declined exile leaves
        // ChosenTargets empty / null at the reserved index. On resolution we
        // re-check the SAME filter predicate (CR 608.2b via TargetFilters.Matches)
        // — a declined OR illegal target means the exile does NOT happen, and the
        // LINKED "If you do" lifegain does not happen either (one indivisible
        // resolution branch). When a legal target is present it is exiled
        // (Fx.MoveToExile handles the graveyard source zone) and the controller
        // then gains N life.
        var filter = def.TargetFilter;
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: may exile target {filter}, then gain {amount} life",
            ctx =>
            {
                var live = ChosenTargetAt(ctx, targetRequestIndex);
                if (live is ICard target && TargetFilters.Matches(filter, target))
                {
                    Fx.MoveToExile(target);
                    controller.GainLife(amount);
                }
                return ValueTask.CompletedTask;
            });
    }

    private static IEffect BuildExploreSelfEffect(
        ExploreSelfEffectDef def, ICard card, Player controller)
    {
        // CR 701.40 — the self explore verb. The exploring permanent is the
        // ability's source; it explores Count times in sequence (Count: 2 =
        // Jadelight Ranger's "explores, then it explores again"). The +1/+1
        // counter (non-land branch, CR 701.40c) lands on the source itself, and
        // a CreatureExploredEvent is published per explore so "Whenever a
        // creature you control explores" payoffs fire (CR 701.40e). Resolution
        // re-resolves the source's live controller (a control change since the
        // ability was put on the stack carries, CR 701.40a). This is the
        // declarative form of the shared ExploreEtb body (PR #2237) — the SAME
        // ExploreAction primitive Seekers' Squire / Merfolk Branchwalker run.
        var count = Math.Max(1, def.Count);
        return new Effect(
            count == 1 ? $"{card.Name}: explores" : $"{card.Name}: explores {count}x",
            async ctx =>
            {
                var explorerController = (card as Permanent)?.Controller ?? controller;
                for (var i = 0; i < count; i++)
                {
                    await Majik.Core.Keywords.ExploreAction.ExploreAsync(
                        creature: card,
                        controller: explorerController,
                        agent: ctx.Agent ?? AgentRegistry.Get(explorerController),
                        game: ctx.Game,
                        replacements: null,
                        eventBus: null,
                        zones: ZoneServiceRegistry.Get(explorerController),
                        ct: ctx.Ct).ConfigureAwait(false);
                }
            });
    }

    private static IEffect BuildExploreTargetEffect(
        ExploreTargetEffectDef def, ICard card, int targetRequestIndex)
    {
        // CR 701.40 — the targeted explore verb. Reads the chosen creature off
        // ChosenTargets at the reserved index and explores it under ITS
        // controller (CR 701.40a — the exploring permanent's controller reveals
        // the top card; the +1/+1 counter, if any, lands on the exploring
        // creature). The keep-on-top / graveyard choice (CR 701.40c) consults the
        // resolving context's agent (falling back to the registry, then
        // keep-on-top); a CreatureExploredEvent is published afterwards so
        // "Whenever a creature you control explores" payoffs fire (CR 701.40e).
        // CR 608.2b — an illegal target at resolution (the chosen creature has
        // left the battlefield) fizzles cleanly: no explore. The shared
        // ExploreAction primitive (PR #2237) is the SAME body the ETB-explore
        // factories (Seekers' Squire / Merfolk Branchwalker) run.
        var filter = def.TargetFilter;
        return new Effect(
            $"{card.Name}: target {filter} explores",
            async ctx =>
            {
                var live = ChosenTargetAt(ctx, targetRequestIndex);
                if (live is not Creature target
                    || target.Zone != ZoneType.Battlefield
                    || !TargetFilters.Matches(filter, target))
                {
                    return;
                }

                // CR 701.40a — explore under the exploring permanent's own
                // controller (a control change since announcement carries here).
                var controller = target.Controller;
                if (controller is null) return;

                await Majik.Core.Keywords.ExploreAction.ExploreAsync(
                    creature: target,
                    controller: controller,
                    agent: ctx.Agent ?? AgentRegistry.Get(controller),
                    game: ctx.Game,
                    replacements: null,
                    eventBus: null,
                    zones: ZoneServiceRegistry.Get(controller),
                    ct: ctx.Ct).ConfigureAwait(false);
            });
    }

    private static IEffect BuildPumpTargetEffect(
        PumpTargetEffectDef def, ICard card, int targetRequestIndex)
    {
        // CR 611 / CR 514.2 — the targeted +X/+X (signed: also −X/−X) verb.
        // Reads the chosen creature off ChosenTargets at the reserved index and
        // registers a Layer-7c PumpUntilEndOfTurnEffect on the creature's OWN
        // ActiveEffects, so it auto-expires at the cleanup step — the same
        // posture the fluent PumpUntilEndOfTurn MaterializeStep uses. CR 608.2b
        // — an illegal target at resolution (left the battlefield) fizzles
        // cleanly: no modifier. ActiveEffects null (pure-shape test path) →
        // silent no-op (mirrors GainControlEffectDef / DismemberFactory).
        var filter = def.TargetFilter;
        var p = def.Power;
        var t = def.Toughness;
        return new Effect(
            $"{card.Name}: target {filter} gets {(p < 0 ? "" : "+")}{p}/{(t < 0 ? "" : "+")}{t} until EOT",
            ctx =>
            {
                var live = ChosenTargetAt(ctx, targetRequestIndex);
                if (live is Creature creature
                    && creature.Zone == ZoneType.Battlefield
                    && TargetFilters.Matches(filter, creature)
                    && creature.ActiveEffects != null)
                {
                    creature.ActiveEffects.Register(
                        new PumpUntilEndOfTurnEffect(creature, p, t));
                }
                return ValueTask.CompletedTask;
            });
    }

    private static IEffect BuildPumpSelfEffect(PumpSelfEffectDef def, ICard card)
    {
        // CR 611 / CR 514.2 — the Subject=self +X/+X (signed: also −X/−X) verb.
        // Registers a Layer-7c PumpUntilEndOfTurnEffect on the SOURCE creature's
        // OWN ActiveEffects (no target slot) — the same posture the fluent
        // PumpUntilEndOfTurn MaterializeStep uses, so the modifier auto-expires
        // at the cleanup step and stacks additively per activation (CR 611.2c).
        // Source not a battlefield creature / ActiveEffects null (pure-shape
        // test path) → silent no-op, mirroring BuildPumpTargetEffect.
        var p = def.Power;
        var t = def.Toughness;
        return new Effect(
            $"{card.Name}: gets {(p < 0 ? "" : "+")}{p}/{(t < 0 ? "" : "+")}{t} until EOT",
            () =>
            {
                if (card is Creature creature
                    && creature.Zone == ZoneType.Battlefield
                    && creature.ActiveEffects != null)
                {
                    creature.ActiveEffects.Register(
                        new PumpUntilEndOfTurnEffect(creature, p, t));
                }
            });
    }

    private static IEffect BuildGrantKeywordUntilEotTargetEffect(
        GrantKeywordUntilEotTargetEffectDef def, ICard card, int targetRequestIndex)
    {
        // CR 613.1c / CR 514.2 — the targeted "gains [keyword] until end of
        // turn" verb. Reads the chosen creature off ChosenTargets at the
        // reserved index and registers a Layer-6 GrantKeywordUntilEndOfTurnEffect
        // on the creature's OWN ActiveEffects (auto-expires at cleanup). The
        // SAME until-EOT grant the GainControlEffectDef haste rider and the
        // Temur Battle Rage / Berserk pump family use. CR 608.2b — an illegal
        // target at resolution fizzles cleanly. ActiveEffects null (pure-shape
        // test path) → silent no-op.
        var filter = def.TargetFilter;
        var keyword = def.Keyword;
        return new Effect(
            $"{card.Name}: target {filter} gains {keyword} until EOT",
            ctx =>
            {
                var live = ChosenTargetAt(ctx, targetRequestIndex);
                if (live is Creature creature
                    && creature.Zone == ZoneType.Battlefield
                    && TargetFilters.Matches(filter, creature)
                    && creature.ActiveEffects != null)
                {
                    creature.ActiveEffects.Register(
                        new GrantKeywordUntilEndOfTurnEffect(creature, keyword));
                }
                return ValueTask.CompletedTask;
            });
    }

    private static IEffect BuildBecomesArtifactTargetEffect(
        BecomesArtifactTargetEffectDef def, ICard card, int targetRequestIndex)
    {
        // CR 613.1d / CR 514.2 — the Liquimetal Coating / Liquimetal Torque
        // "target [permanent] becomes an artifact in addition to its other types
        // until end of turn" verb. Reads the chosen permanent off ChosenTargets
        // at the reserved index and registers the EOT-expiring Layer-4
        // LiquimetalCoatingAddArtifactEffect (the ADD union keeps the printed
        // types present) on the permanent's OWN ActiveEffects, so it expires at
        // cleanup. CR 608.2b — an illegal target at resolution (left the
        // battlefield, or no longer matches the filter) fizzles cleanly.
        // ActiveEffects null (pure-shape test path) → silent no-op, mirroring the
        // hand-rolled LiquimetalCoatingFactory.
        var filter = def.TargetFilter;
        return new Effect(
            $"{card.Name}: target {filter} becomes an artifact until EOT",
            ctx =>
            {
                var live = ChosenTargetAt(ctx, targetRequestIndex);
                if (live is Permanent permanent
                    && permanent.Zone == ZoneType.Battlefield
                    && TargetFilters.Matches(filter, permanent)
                    && permanent.ActiveEffects != null)
                {
                    permanent.ActiveEffects.Register(
                        new LiquimetalCoatingAddArtifactEffect(permanent));
                }
                return ValueTask.CompletedTask;
            });
    }

    /// <summary>
    /// CR 607 (linked abilities) / CR 603.6e ("until" duration) / CR 610.3
    /// (return) — the declarative Banishing Light / Oblivion Ring / Glass
    /// Casket / Portable Hole / Leonin Relic-Warder mechanism.
    ///
    /// <para>
    /// This runs at card-build time (it is an effect-builder, invoked while the
    /// owning <c>etb_self</c> ability is materialized for the live
    /// <paramref name="card"/>). It does two things:
    /// </para>
    /// <list type="number">
    ///   <item>Creates a per-instance closure (<c>exiled</c> + <c>exiledOwner</c>)
    ///   shared between the two linked abilities.</item>
    ///   <item>Builds the linked leaves-the-battlefield (LTB) triggered ability
    ///   and attaches it to the SAME card via <c>card.AddAbility</c>, so when
    ///   the card enters the battlefield <see cref="TriggerManager.BindCard"/>
    ///   auto-registers BOTH abilities (matching the hand-rolled
    ///   <see cref="Majik.Core.CardData.Factories.BanishingLightFactory"/>
    ///   posture exactly).</item>
    /// </list>
    /// <para>
    /// It returns the ETB effect: at resolution it reads the chosen target off
    /// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/>,
    /// re-checks the composed legality (CR 608.2b — still on the battlefield,
    /// still matches the base filter, an opponent controls it /
    /// <c>excludeSelf</c> / mana value cap), exiles it (CR 701.21), and records
    /// it + its owner in the shared closure. The LTB effect, when the source
    /// leaves the battlefield, returns the SAME remembered object to its
    /// <b>owner's</b> battlefield under its owner's control (CR 110.2). If the
    /// source already left (closure already returned, ref cleared) or the
    /// exiled object has since left exile (CR 603.6e — the linked "until" return
    /// finds nothing), the LTB no-ops cleanly.
    /// </para>
    /// </summary>
    private static IEffect BuildExileUntilLeavesEffect(
        ExileUntilLeavesEffectDef def, ICard card, Player controller, int targetRequestIndex)
    {
        // ----------------------------------------------------------------
        // Shared per-instance closure: ETB writes, LTB reads. One Banishing
        // Light only ever exiles one card per ETB resolution (the printed
        // "target" is singular); a fresh ICard identity on re-entry (CR 400.7)
        // starts its own closure.
        // ----------------------------------------------------------------
        ICard? exiled = null;
        Player? exiledOwner = null;

        var filter = def.TargetFilter;
        var opponentOnly = def.OpponentControlsOnly;
        var excludeSelf = def.ExcludeSelf;
        var maxMv = def.MaxManaValue;

        // CR 608.2b — the composed resolution-time legality re-check. Mirrors
        // the hand-rolled factories (BanishingLight / GlassCasket / PortableHole
        // / OblivionRing): base filter (via TargetFilters.Matches) + "an opponent
        // controls" (CR 109.5) + "another" (exclude self) + mana-value cap
        // (CR 202.3). The source controller is read live so a control change
        // carries the ability (CR 109.5).
        bool IsLegalTarget(Permanent target)
        {
            if (target.Zone != ZoneType.Battlefield) return false;
            if (!TargetFilters.Matches(filter, target)) return false;
            if (excludeSelf && ReferenceEquals(target, card)) return false;
            if (opponentOnly)
            {
                var sourceController = card.Controller ?? controller;
                if (ReferenceEquals(target.Controller, sourceController)) return false;
            }
            if (maxMv is int cap && target is Card mvCard
                && mvCard.ManaCostValue.TotalValue > cap)
            {
                return false;
            }
            return true;
        }

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6e / CR 610.3. Fires whenever the
        // source moves OUT of the battlefield (any destination — covers dies +
        // bounce + flicker, matching "leaves the battlefield" wording, same
        // posture as the hand-rolled siblings). Attached now so BindCard
        // auto-registers it when the source enters the battlefield.
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<Majik.Core.Events.CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{card.Name}: return the exiled card to the battlefield under its owner's control (CR 610.3)",
            () =>
            {
                if (exiled == null || exiledOwner == null) return;
                // CR 603.6e / CR 400.7 — if the exiled card has since left exile
                // (extraction, processed by Eldrazi, etc.), the linked return
                // finds nothing. The ref is cleared after a successful return so
                // a second LTB (a re-entered, then re-left source shares no
                // closure; this guards a double-fire of the SAME instance).
                if (exiled.Zone != ZoneType.Exile)
                {
                    exiled = null;
                    exiledOwner = null;
                    return;
                }

                exiledOwner.Zones.Exile.RemoveCard(exiled);
                exiledOwner.Zones.Battlefield.AddCard(exiled);
                exiled.SetZone(ZoneType.Battlefield);
                // CR 110.2 — "under its owner's control" maps Controller := Owner
                // on the way back.
                if (exiled is Card returned) returned.ChangeController(exiledOwner);

                // CR 603.6e — the "until" return happens once; clear so a
                // subsequent LTB of the same instance no-ops.
                exiled = null;
                exiledOwner = null;
            });

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: controller,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            // CR 603.6d — LTB triggers see the permanent as it last existed on
            // the battlefield (same "looks back" posture as the hand-rolled
            // siblings).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);

        // ----------------------------------------------------------------
        // ETB effect — CR 603.6a / CR 701.21. Exile the chosen target and
        // record it + its owner for the linked LTB return.
        // ----------------------------------------------------------------
        return new Effect(
            def.BuildEffectDescription(card.Name),
            ctx =>
            {
                var live = ChosenTargetAt(ctx, targetRequestIndex);
                if (live is not Permanent target || !IsLegalTarget(target))
                {
                    return ValueTask.CompletedTask;
                }

                var targetOwner = target.Owner;
                Fx.MoveToExile(target);

                exiled = target;
                exiledOwner = targetOwner;
                return ValueTask.CompletedTask;
            });
    }

    private static IEffect BuildReturnToHandEffect(ReturnToHandEffectDef def, ICard card, int targetRequestIndex)
    {
        // CR 701.20 — real targeted bounce. Reads the chosen permanent off
        // ChosenTargets at the reserved index and returns it to its owner's
        // hand via Fx.BounceToHand. CR 608.2b — fizzle (no-op) if the target
        // is no longer a battlefield permanent at resolution. Exact parallel
        // of BuildDestroyTargetEffect / BuildUntapTargetEffect — the only new
        // piece is the declarative wiring onto the pre-existing Fx primitive.
        var filter = def.TargetFilter;
        return new Effect(
            $"{card.Name}: return target {filter} to its owner's hand",
            ctx =>
            {
                // CR 608.2b — re-check the SAME filter predicate at resolution
                // (via TargetFilters.Matches) so a type-gated bounce
                // ("nonland permanent" / "creature" / …) fizzles cleanly when
                // the chosen object no longer matches — the mirror of the
                // destroy/exile re-check.
                var live = ChosenTargetAt(ctx, targetRequestIndex);
                if (live is Permanent permanent
                    && permanent.Zone == ZoneType.Battlefield
                    && TargetFilters.Matches(filter, permanent))
                {
                    Fx.BounceToHand(permanent);
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

    private static IEffect BuildGainControlEffect(
        GainControlEffectDef def, ICard card, Player controller, int targetRequestIndex,
        ContinuousEffectsService? continuous)
    {
        // CR 613.2 / CR 514.2 — the Threaten / Act of Treason / Zealous
        // Conscripts family. Reads the chosen PERMANENT off ChosenTargets at the
        // reserved index and, until end of turn, swaps its real controller to
        // the spell/ability controller via a TemporaryControlChangeEffect on the
        // live continuous-effects service (control reverts at cleanup). Composes
        // the standard Threaten rider: untap the permanent (so a stolen creature
        // can attack) and — for a creature — grant it haste until end of turn
        // (CR 302.6 — a permanent whose control changed this turn is sick).
        //
        // The target is a PERMANENT, not just a creature: Zealous Conscripts
        // reads "gain control of target permanent" and can steal an artifact /
        // enchantment / land / planeswalker. The control swap + untap apply to
        // any permanent; the haste grant is a Layer-6 keyword grant that only
        // applies to a creature (a non-creature can hold a haste marker harm-
        // lessly but the engine's until-EOT keyword grant is creature-typed, so
        // it is wired only for creatures — non-creatures need no haste anyway).
        //
        // CR 608.2b — an illegal target at resolution (the permanent has left
        // the battlefield) fizzles entirely. Without a live service (pure-shape
        // test path) the control swap no-ops, like ArchmagesCharmFactory's
        // single-arg posture.
        var untap = def.Untap;
        var gainsHaste = def.GainsHaste;
        // CR 601.2b / 603.4 — the optional reflexive "you may pay {cost}. If you
        // do, …" rider (Eldrazi Obligator). Parsed once at build time; null when
        // the control change is mandatory (Threaten / Zealous Conscripts).
        var optionalCost = string.IsNullOrWhiteSpace(def.OptionalManaCost)
            ? null
            : Majik.Core.ValueObjects.ManaCost.Parse(def.OptionalManaCost);
        var cardName = card.Name;
        return new Effect(
            $"{card.Name}: gain control of target {def.TargetFilter} until end of turn"
                + (optionalCost != null ? $" (may pay {def.OptionalManaCost})" : ""),
            async ctx =>
            {
                if (continuous == null) return;
                if (ChosenTargetAt(ctx, targetRequestIndex) is not Permanent permanent
                    || permanent.Zone != ZoneType.Battlefield)
                {
                    return;
                }

                // CR 601.2b — the optional reflexive payment. Prompt the agent
                // yes/no; on "yes" attempt the {cost} payment via the shared
                // Player.PayMana primitive. A decline OR an unpayable cost skips
                // the ENTIRE "if you do" clause (control swap + untap + haste) —
                // CR 601.2b, an optional cost that can't be paid isn't paid.
                if (optionalCost != null)
                {
                    var agent = ctx.Agent
                        ?? Majik.Core.Players.Agents.AgentRegistry.Get(controller);
                    var wantsToPay = agent != null
                        && await agent.ChooseYesNoAsync(
                                ctx.Game,
                                $"Pay {optionalCost} to gain control of {permanent.Name}?",
                                cardName,
                                ctx.Ct)
                            .ConfigureAwait(false);
                    if (!wantsToPay) return;
                    if (!controller.ManaPool.CanPay(optionalCost)) return;
                    if (!controller.PayMana(optionalCost)) return;
                }

                // CR 613.2 — only swap if we don't already control it.
                if (!ReferenceEquals(permanent.Controller, controller))
                {
                    continuous.Register(new TemporaryControlChangeEffect(permanent, controller));
                }

                // CR 701.21 — "Untap that permanent."
                if (untap && permanent.IsTapped)
                {
                    permanent.Untap();
                }

                // "It gains haste until end of turn." (Layer 6 keyword grant,
                // CR 514.2 expiry — reuses the shared until-EOT haste grant.)
                // Only a creature can carry the engine's haste keyword grant;
                // a stolen non-creature needs no haste (it has nothing to attack
                // with), so the rider is creature-gated.
                if (gainsHaste && permanent is Creature creature)
                {
                    continuous.Register(new GrantKeywordUntilEndOfTurnEffect(creature, "Haste"));
                }
            });
    }

    private static IEffect BuildTapTargetEffect(TapTargetEffectDef def, ICard card, int targetRequestIndex)
    {
        // CR 701.21a — real targeted tap, the mirror of BuildUntapTargetEffect.
        // Reads the chosen permanent off ChosenTargets at the reserved index
        // and taps it via Fx.Tap. Tapping an already-tapped permanent is a
        // no-op (CR 701.21b — Permanent.Tap is idempotent). CR 608.2b — fizzle
        // if it is no longer a battlefield permanent at resolution. The only
        // new piece is the declarative wiring onto the pre-existing Fx.Tap
        // primitive.
        return new Effect(
            $"{card.Name}: tap target {def.TargetFilter}",
            ctx =>
            {
                var live = ChosenTargetAt(ctx, targetRequestIndex);
                // CR 701.21b — tapping an already-tapped permanent is a no-op
                // (Permanent.Tap throws on an already-tapped permanent, so the
                // guard models the "taps with no effect" rule).
                if (live is Permanent permanent
                    && permanent.Zone == ZoneType.Battlefield
                    && !permanent.IsTapped)
                {
                    Fx.Tap(permanent);
                }
                return ValueTask.CompletedTask;
            });
    }

    private static IEffect BuildDamageAndTapEachFlyerOpponentsControlEffect(
        DamageAndTapEachFlyerOpponentsControlEffectDef def, ICard card)
    {
        // Thundermaw Hellkite ETB — the group-apply form of the single-target
        // deal_damage + tap_target verbs (CR 109.5 "opponents", CR 702.9 Flying,
        // CR 701.21a Tap). Untargeted (CR 608.2 — affects each member of a
        // defined set), so no ChosenTargets read. At resolution it:
        //   1. Snapshots every battlefield creature with the effective Flying
        //      keyword (CR 613.1f — printed OR granted) controlled by a player
        //      OTHER than this ability's controller (CR 109.5 "your opponents").
        //      The snapshot is taken BEFORE any mutation so the "those creatures"
        //      tap clause acts on the SAME set the damage hit (CR 700.3).
        //   2. Deals Amount damage to each via Fx.DealDamageAny(source=card),
        //      then taps each surviving battlefield member via Fx.Tap.
        // A creature that dies to the damage SBA before the tap pass is skipped
        // for the tap (it has left the battlefield) — tapping it would be a
        // meaningless no-op anyway (CR 701.21b).
        var amount = def.Amount;
        return new Effect(
            $"{card.Name}: deal {amount} damage to each creature with flying your opponents control, then tap those creatures",
            ctx =>
            {
                // Resolve the controller live so a control change carries the
                // ability (CR 109.5); fall back to the resolution controller.
                var controller = (card as Permanent)?.Controller ?? ctx.Controller;
                if (controller == null || ctx.Game == null) return ValueTask.CompletedTask;

                var source = card as Creature;

                // Step 1 — snapshot the set (opponents' battlefield flyers).
                var flyers = new List<Creature>();
                foreach (var player in ctx.Game.AllPlayers)
                {
                    if (ReferenceEquals(player, controller)) continue;
                    var battlefield = player.Zones?.Battlefield;
                    if (battlefield == null) continue;
                    foreach (var c in battlefield.GetCards().OfType<Creature>())
                    {
                        if (c.HasEffectiveKeyword("Flying")) flyers.Add(c);
                    }
                }

                // Step 2 — damage each, then tap each survivor.
                foreach (var flyer in flyers)
                {
                    Fx.DealDamageAny(flyer, amount, source);
                }
                foreach (var flyer in flyers)
                {
                    if (flyer.Zone == ZoneType.Battlefield && !flyer.IsTapped)
                    {
                        Fx.Tap(flyer);
                    }
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

    private static IEffect BuildFightEffect(FightEffectDef def, ICard card, int targetRequestIndex)
    {
        // CR 701.12 — Fight. Two creatures each deal damage equal to their
        // power to the other simultaneously, routed through the shared
        // Fx.Fight primitive (which honours deathtouch CR 702.2b + lifelink
        // CR 702.15a; the lethal-damage SBA runs afterward — CR 704).
        //
        // source "self": the FIGHTER is this card; the single chosen target
        //   (at targetRequestIndex) is the OTHER creature.
        // source "target": the FIGHTER is the chosen creature at
        //   targetRequestIndex; the OTHER creature is the NEXT slot
        //   (targetRequestIndex + 1) declared via ToExtraTargetRequest.
        //
        // CR 608.2b / 701.12c — a fight needs BOTH creatures present on the
        // battlefield at resolution; if either is gone/illegal the whole fight
        // fizzles (no damage either way).
        var isTargetSource =
            string.Equals(def.Source, "target", StringComparison.OrdinalIgnoreCase);
        var otherFilter = def.TargetFilter;
        var fighterFilter = def.ControllerTargetFilter ?? def.TargetFilter;

        return new Effect(
            $"{card.Name}: fight ({def.Source})",
            ctx =>
            {
                Creature? fighter;
                Creature? other;

                if (isTargetSource)
                {
                    fighter = AsLegalFightCreature(
                        ChosenTargetAt(ctx, targetRequestIndex), fighterFilter);
                    other = AsLegalFightCreature(
                        ChosenTargetAt(ctx, targetRequestIndex + 1), otherFilter);
                }
                else
                {
                    // self-source: the fighter is this card; it must still be a
                    // creature on the battlefield (CR 701.12c).
                    fighter = card is Creature self
                              && self.Zone == ZoneType.Battlefield
                        ? self
                        : null;
                    other = AsLegalFightCreature(
                        ChosenTargetAt(ctx, targetRequestIndex), otherFilter);
                }

                if (fighter != null && other != null)
                {
                    Fx.Fight(fighter, other);
                }
                return ValueTask.CompletedTask;
            });
    }

    /// <summary>
    /// CR 608.2b / 701.12c — coerce a chosen target into a live fight-legal
    /// creature: it must be a <see cref="Creature"/> on the battlefield that
    /// still matches the printed <paramref name="filter"/>. Returns
    /// <c>null</c> otherwise so the fight fizzles.
    /// </summary>
    private static Creature? AsLegalFightCreature(object? chosen, string filter) =>
        chosen is Creature c
        && c.Zone == ZoneType.Battlefield
        && TargetFilters.Matches(filter, c)
            ? c
            : null;

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

    /// <summary>Parse an optional subtype/tribal filter — <c>null</c> or empty
    /// means "no subtype restriction" (returns <c>null</c>).</summary>
    private static CardSubtype? ParseOptionalSubtype(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : ParseSubtype(raw);
}
