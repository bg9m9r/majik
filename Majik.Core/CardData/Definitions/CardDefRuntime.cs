using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
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
    public static ICard Build(CardDef def, Player owner)
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

        foreach (var keyword in def.Keywords)
        {
            card.AddAbility(new KeywordAbility(keyword, card, owner));
        }

        foreach (var produces in def.ManaAbilities)
        {
            card.AddAbility(new ManaAbility(card, owner, ManaCost.Parse(produces)));
        }

        return card;
    }

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
}
