using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData;

/// <summary>
/// THE definition of "build a real deck card": repo shell + named-factory
/// routing + binder chain + owner/service wiring. <see cref="Build"/> covers
/// both steps a real match runs — name → typed shell (the
/// <c>RealDeckLoader</c> materialize step, via
/// <see cref="DeckCardShellBuilder"/>) and shell → fully-built card (the
/// <see cref="Majik.Core.Api.GameFacade.Create"/> binder/factory chain, via
/// <see cref="BuildFromShell"/>). GameFacade's deck path delegates here; the
/// bot's determinization sampler uses it so sampled opponent cards are built
/// EXACTLY like live-deck cards. Extracted from GameFacade — no behavior
/// change.
///
/// <para>Castability note for instants/sorceries: the card does NOT carry its
/// <see cref="Majik.Core.Game.SpellDefinition"/> — TurnDriver resolves it at
/// cast time BY NAME via <see cref="ScryfallCardFactory.LookupSpellDefinition"/>.
/// The card-side castability surface this builder guarantees is the correct
/// runtime type + name + mana cost.</para>
/// </summary>
public static class DeckCardBuilder
{
    /// <summary>
    /// Build a fully-functional deck card from a card <paramref name="name"/>:
    /// resolve the seed entity via <paramref name="repo"/>, materialize the
    /// typed shell (<see cref="DeckCardShellBuilder"/> — same step the prod
    /// deck loader runs), then run the shell through the GameFacade
    /// binder/factory chain (<see cref="BuildFromShell"/>). Throws
    /// <see cref="ArgumentException"/> for a name the repo does not know —
    /// callers that want a degraded vanilla shell for unknown names handle
    /// that themselves.
    /// </summary>
    public static ICard Build(
        string name,
        Player owner,
        ICardRepository repo,
        ReplacementBus replacements,
        ContinuousEffectsService effects,
        TriggerManager? triggers,
        ZoneService? zones,
        IEventBus? eventBus,
        bool routeThroughNamedFactories)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(repo);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Card name required", nameof(name));

        var entity = repo.GetByName(name)
            ?? throw new ArgumentException($"unknown card: {name}", nameof(name));
        var shell = DeckCardShellBuilder.Build(entity);
        return BuildFromShell(shell, owner, repo, replacements, effects,
            routeThroughNamedFactories, triggers, zones, eventBus);
    }

    /// <summary>
    /// Produce the live deck-card instance for <paramref name="shell"/>.
    ///
    /// <para>Default path (binder chain): set owner on the incoming shell and
    /// run <see cref="BindCardAbilities"/> on it — historical behaviour.</para>
    ///
    /// <para>Routed path (<see cref="Majik.Core.Api.GameFacade.RouteThroughNamedFactories"/> on AND
    /// <see cref="Majik.Core.CardData.Factories.ImplementedCardNames.HasRealFactory"/>
    /// AND the card is NOT a Land): build a fresh instance via the card's
    /// <c>[CardName]</c> factory so its bespoke abilities (closures that
    /// capture the factory's own card as <c>source</c>) function in prod. The
    /// returned instance REPLACES the shell. We then re-stamp the color
    /// indicator the shell loader applied (CR 202.2c) and overlay only the
    /// additive/generic binders (keywords, mana, and the ETB
    /// replacement/counter/copy binders) with a dedup guard. We deliberately
    /// do NOT run the triggered-ability / saga / affinity / loyalty binders
    /// on routed cards — the factory owns those bespoke abilities and the
    /// binders would double-add ETB triggers.</para>
    /// </summary>
    public static ICard BuildFromShell(
        ICard shell,
        Player owner,
        ICardRepository? cardRepo,
        ReplacementBus replacements,
        ContinuousEffectsService effects,
        bool routeThroughNamedFactories,
        TriggerManager? triggers = null,
        ZoneService? zones = null,
        IEventBus? eventBus = null)
    {
        // CR 712.3 / 712.4 — Modal Double-Faced Card: real cast-either-face
        // (deferral #3). The seed stores MDFCs under the composite name
        // "Front // Back"; the per-face factories register only their face
        // names. When the FRONT face has a real factory and is NOT a land
        // (the spell-front + land-back Modern cycle: Sink into Stupor,
        // Valakut Awakening, Shatterskull Smashing, …), build the card through
        // the FRONT factory so it carries the cast-either-face MdfcState (the
        // back-face descriptor whose builder materializes a fully-wired back
        // land instance when the controller chooses it). The front factory
        // sets the card's Name to the front face — GetByName resolves that
        // back to the composite entity via its "Front // ..." prefix scan, so
        // the oracle binder + IsImplemented derivation are unaffected.
        if (routeThroughNamedFactories
            && TrySplitMdfcFrontFace(shell.Name, out var mdfcFrontName)
            && Majik.Core.CardData.Factories.ImplementedCardNames.HasRealFactory(mdfcFrontName))
        {
            var frontEntity = cardRepo?.GetByName(mdfcFrontName);
            // The composite entity's TypeLine is "Front // Back" (e.g.
            // "Instant // Land"); parse only the FRONT half so a land BACK
            // doesn't mark the front as a land.
            var frontTypeLine = frontEntity?.TypeLine is { } tl
                ? (tl.IndexOf(" // ", StringComparison.Ordinal) is var ti && ti >= 0 ? tl[..ti] : tl)
                : null;
            var frontIsLand = frontTypeLine != null
                && Majik.Core.CardData.TypeLineParser.Parse(frontTypeLine)
                    .Types.Contains(Majik.Core.Cards.Types.CardType.Land);
            if (!frontIsLand)
            {
                // CR 613.7c — route through the effects-aware overload so a
                // front-face factory that registers a continuous
                // LordStaticEffect / AttachedBoostEffect (lord / anthem /
                // equipment / aura) wires it against the live per-game service.
                // Returns the single-arg result for fronts with no such
                // overload — behaviourally identical for them.
                var mdfcBuilt = Majik.Core.CardData.NamedCardFactory.Create(mdfcFrontName, owner, effects);
                if (mdfcBuilt is Majik.Core.Cards.Creature mdfcCreature)
                {
                    mdfcCreature.ActiveEffects = effects;
                }
                if (frontEntity != null)
                {
                    if (mdfcBuilt is Majik.Core.Cards.Card mdfcConcrete)
                    {
                        var colors = Majik.Core.Cards.CardColors.ParseScryfallColors(frontEntity.Colors);
                        if (colors.Count > 0) mdfcConcrete.SetColorIndicator(colors);
                    }
                    OverlayAdditiveBinders(mdfcBuilt, frontEntity, owner, replacements, effects);
                }
                return mdfcBuilt;
            }
        }

        if (routeThroughNamedFactories
            && !shell.HasType(Majik.Core.Cards.Types.CardType.Land)
            && Majik.Core.CardData.Factories.ImplementedCardNames.HasRealFactory(shell.Name))
        {
            // APPROACH B — instance swap. Build the card through its factory
            // (owner == controller, matching what GameFacade already assigns).
            // CR 613.7c — route through the effects-aware overload so a factory
            // that registers a continuous LordStaticEffect / AttachedBoostEffect
            // (lord / anthem / equipment / aura) wires it against the live
            // per-game ContinuousEffectsService. Without this the static effect
            // was silently dropped in production: the factory's single-arg
            // Create never touches the service, so the lord built nothing live
            // (Empyrean Eagle / Leyline of the Guildpact / Dryad of the Ilysian
            // Grove / every LordStaticEffect anthem). Names with no effects-aware
            // overload fall back to the single-arg build — identical result.
            var built = Majik.Core.CardData.NamedCardFactory.Create(shell.Name, owner, effects);

            // Hook the CES so creatures get layer-system P/T (mirrors
            // BindCardAbilities). The effects-aware overload registers the
            // lord's OWN static but does not set ActiveEffects on the built
            // card itself, so it must still be wired for the card to READ its
            // own (and others') layer P/T.
            if (built is Majik.Core.Cards.Creature creature)
            {
                creature.ActiveEffects = effects;
            }

            var entity = cardRepo?.GetByName(shell.Name);
            if (entity != null)
            {
                // Re-apply the color indicator the shell loader stamped
                // (CR 202.2c) — the factory may not. SetColorIndicator is
                // idempotent; the union with mana-cost pips is duplicate-safe.
                if (built is Majik.Core.Cards.Card concrete)
                {
                    var colors = Majik.Core.Cards.CardColors.ParseScryfallColors(entity.Colors);
                    if (colors.Count > 0) concrete.SetColorIndicator(colors);
                }

                OverlayAdditiveBinders(built, entity, owner, replacements, effects);

                // CR 714.2b — the [CardName] factory attached a SagaState with
                // no live runtime services (NamedCardFactory.Create takes only
                // the owner), so chapters would resolve synchronously and the
                // chapter-I/III triggers + rummage prompt would be inert in a
                // real match. Re-bind the Saga with the live TriggerManager /
                // ZoneService / EventBus so chapter abilities go on the stack
                // (an opponent can respond) and the Fable rummage prompts the
                // controller's agent. Idempotent: SagaBinder.Bind overwrites
                // the SagaState in place.
                if (built is Majik.Core.Cards.Permanent sagaPerm
                    && sagaPerm.SagaState != null
                    && triggers != null)
                {
                    Majik.Core.CardData.SagaBinder.Bind(
                        built, entity, effects, zones, triggers, eventBus);
                }
            }

            return built;
        }

        shell.SetOwner(owner);
        BindCardAbilities(shell, owner, cardRepo, replacements, effects,
            triggers, zones, eventBus);

        // Stamp IsVanillaShell ONLY on the non-routed binder-chain path. A card
        // built through its [CardName] factory (the routed return above) is
        // implemented by definition — its behaviour may live in off-card
        // effects (continuous / replacement / CDA registered on a live service,
        // not as card.Abilities), which the classifier cannot see, so stamping
        // there false-flags cards like Blood Moon / Wild Nacatl / Pithing
        // Needle. Only lands + factory-less cards reach here, where the card is
        // fully bound and the classification is authoritative. Uses the same
        // shared classifier as ScryfallCardFactory.Create.
        if (cardRepo != null)
        {
            var entity = cardRepo.GetByName(shell.Name);
            if (entity != null
                && Majik.Core.CardData.VanillaShellClassifier.IsLikelyVanillaShell(shell, entity))
            {
                (shell as Majik.Core.Cards.Card)?.MarkAsVanillaShell();
            }
        }

        return shell;
    }

    /// <summary>
    /// CR 712 — split a composite "Front // Back" card name into its FRONT
    /// face. Returns false (with <paramref name="frontName"/> = the input)
    /// for single-faced names. Used to route MDFC fronts through their
    /// per-face <c>[CardName]</c> factory so the cast-either-face MdfcState is
    /// attached in production (deferral #3).
    /// </summary>
    private static bool TrySplitMdfcFrontFace(string name, out string frontName)
    {
        var idx = name?.IndexOf(" // ", StringComparison.Ordinal) ?? -1;
        if (idx < 0)
        {
            frontName = name ?? string.Empty;
            return false;
        }
        frontName = name![..idx];
        return true;
    }

    /// <summary>
    /// Overlay ONLY the additive/generic binders onto a factory-built card,
    /// each guarded so a keyword or mana ability the factory already added is
    /// never doubled. Runs the ETB replacement/counter/copy chain (which the
    /// factory may not register for non-land permanents). Does NOT run the
    /// triggered-ability / saga / affinity / loyalty binders — the factory
    /// owns those.
    /// </summary>
    private static void OverlayAdditiveBinders(
        ICard card,
        CardEntity entity,
        Player controller,
        ReplacementBus replacements,
        ContinuousEffectsService effects)
    {
        // CR 205.1b — preserve EVERY printed card type. A [CardName] factory
        // builds a single concrete subclass (Creature / Land / …) that only
        // registers its OWN primary type, so a dual-type card built through
        // the factory route loses its secondary type unless the factory
        // remembered to AddCardType it (most do not — Esper Sentinel's
        // Artifact was silently dropped). Additively flag any parsed type the
        // built card is missing, so every artifact-creature / artifact-land /
        // enchantment-land is correctly typed in prod regardless of whether
        // its factory bothered. AddCardType is idempotent — a type the factory
        // already added is a no-op. Composite (DFC / split / adventure) rows
        // build as their FRONT face, so parse only the front half; adding the
        // back face's types here would mistype the front (e.g. a spell-front
        // // land-back MDFC must not become a Land).
        if (card is Card typedCard)
        {
            var typeLine = entity.TypeLine ?? "";
            var splitIdx = typeLine.IndexOf(" // ", StringComparison.Ordinal);
            var frontTypeLine = splitIdx >= 0 ? typeLine[..splitIdx] : typeLine;
            foreach (var t in Majik.Core.CardData.TypeLineParser.Parse(frontTypeLine).Types)
            {
                typedCard.AddCardType(t);
            }
        }

        // Snapshot the keyword + mana abilities the factory already attached
        // so the binders' additions can be deduped.
        var existingKeywords = card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hadManaAbility = card.Abilities.OfType<Majik.Core.Abilities.IManaAbility>().Any();

        // KeywordBinder — dedup any keyword the factory already added.
        var beforeKeyword = card.Abilities.OfType<KeywordAbility>().Count();
        KeywordBinder.Bind(card, entity, controller, effects);
        DedupKeywordAbilities(card, existingKeywords, beforeKeyword);

        // OracleManaBinder — if the factory already gave the card a mana
        // ability, skip the binder entirely (it cannot know the factory's
        // intent and would double the source).
        if (!hadManaAbility)
        {
            OracleManaBinder.Bind(card, entity, controller);
        }

        // ETB replacement / counter / copy chain — generic, additive, and not
        // something a non-land factory typically registers. Mirrors
        // ScryfallCardFactory so the two binder entry points don't diverge.
        if (!ShockLandBinder.Bind(card, entity, replacements) &&
            !SubtypeEntersTappedBinder.Bind(card, entity, replacements) &&
            !ConditionalEntersTappedBinder.Bind(card, entity, replacements))
        {
            EntersTappedBinder.Bind(card, entity, replacements);
        }
        EntersWithCountersBinder.Bind(card, entity, replacements);
        EntersAsCopyBinder.Bind(card, entity, replacements, effects);

        // CR 614.12 — "as this enters, choose a color" for the NON-LAND members
        // of the family (Coldsteel Heart's artifact, Utopia Sprawl's Aura). Their
        // [CardName] factory stashed a ColorChoice holder in ColorChoiceRegistry;
        // register the agent-prompting ChooseColorReplacement so the controller
        // picks a colour as the permanent enters and the dynamic mana ability /
        // trigger reads that pick (the land members use ChooseColorLandBinder from
        // the binder chain). No-op when no holder was stashed.
        ChooseColorPermanentBinder.Bind(card, replacements);
    }

    /// <summary>
    /// Remove any <see cref="KeywordAbility"/> the binder just added whose
    /// keyword the factory had already attached (snapshot in
    /// <paramref name="preexisting"/>). <paramref name="boundBefore"/> is the
    /// keyword-ability count before the binder ran, so we only inspect the
    /// abilities the binder appended.
    /// </summary>
    private static void DedupKeywordAbilities(
        ICard card, HashSet<string> preexisting, int boundBefore)
    {
        if (card is not Card concrete) return;
        var keywordAbilities = card.Abilities.OfType<KeywordAbility>().ToList();
        for (var i = keywordAbilities.Count - 1; i >= boundBefore; i--)
        {
            if (preexisting.Contains(keywordAbilities[i].Keyword))
            {
                concrete.RemoveAbility(keywordAbilities[i]);
            }
        }
    }

    /// <summary>
    /// Attaches abilities to a card after its owner is set. When
    /// <paramref name="cardRepo"/> is provided the full binder pipeline
    /// runs (KeywordBinder, OracleManaBinder, AffinityBinder, SagaBinder,
    /// OracleTriggeredAbilityBinder, ShockLandBinder). Without a repo only
    /// the basic-land mana path fires — preserving pre-existing behaviour.
    ///
    /// <paramref name="replacements"/> is the game's own ReplacementBus and
    /// is always non-null when called from <see cref="Majik.Core.Api.GameFacade.Create"/>; ShockLandBinder
    /// registers onto it unconditionally whenever the repo returns a matching
    /// shock-land entity (CR 614).
    /// </summary>
    private static void BindCardAbilities(
        ICard card,
        Player controller,
        ICardRepository? cardRepo,
        ReplacementBus replacements,
        ContinuousEffectsService effects,
        TriggerManager? triggers = null,
        ZoneService? zones = null,
        IEventBus? eventBus = null)
    {
        // Every creature consults the game's CES for current P/T and keywords
        // (CR 613). Hook it up regardless of repo presence so vanilla creatures
        // still get layer-system computation when other effects target them.
        if (card is Majik.Core.Cards.Creature creature)
        {
            creature.ActiveEffects = effects;
        }

        if (cardRepo != null)
        {
            var entity = cardRepo.GetByName(card.Name);
            if (entity != null)
            {
                KeywordBinder.Bind(card, entity, controller, effects);
                OracleManaBinder.Bind(card, entity, controller);
                AffinityBinder.Bind(card, entity);
                // CR 714.2b — pass live runtime services so Saga chapter
                // abilities route through the stack (responder priority window)
                // and the Fable rummage prompts the controller's agent.
                SagaBinder.Bind(card, entity, effects, zones, triggers, eventBus);
                foreach (var trig in OracleTriggeredAbilityBinder.Bind(
                    card, entity, controller, allPlayers: null, eventBus: eventBus))
                {
                    card.AddAbility(trig);
                }
                // ETB replacement chain (CR 614). Reconciles a long-standing
                // asymmetry with ScryfallCardFactory: BindCardAbilities used to
                // register ONLY the shock-land replacement, so subtype /
                // conditional / unconditional enters-tapped lands (surveil
                // lands, "this land enters tapped", check lands) entered
                // UNTAPPED in real matches. Run the full short-circuit chain
                // (Shock → Subtype → Conditional → Unconditional) plus the
                // independent counter / copy binders so the prod card-build
                // path matches the factory path exactly.
                if (!ShockLandBinder.Bind(card, entity, replacements) &&
                    !SubtypeEntersTappedBinder.Bind(card, entity, replacements) &&
                    !ConditionalEntersTappedBinder.Bind(card, entity, replacements))
                {
                    EntersTappedBinder.Bind(card, entity, replacements);
                }
                EntersWithCountersBinder.Bind(card, entity, replacements);
                EntersAsCopyBinder.Bind(card, entity, replacements, effects);
                OracleLandActivatedAbilityBinder.Bind(card, entity, controller);
                // Generic utility-land activated abilities (scry / draw / +1/+1
                // counter / token / damage / gain-life / return-from-graveyard /
                // destroy-target-land). Lands are NEVER routed through their
                // [CardName] factory, so these abilities were DEAD in prod —
                // this binder is the ONLY path that makes them fire in a real
                // match (v1-deferrals #12). Runs AFTER the fetch/Horizon binder
                // (those patterns are claimed first) and BEFORE ManlandBinder.
                LandActivatedAbilityBinder.Bind(card, entity, controller, effects, triggers);
                // Manland (creature-land) animate + Restless attack triggers.
                // Lands are NEVER routed through their [CardName] factory (the
                // factory instance-swap is gated on !shell.HasType(Land)), so
                // the per-card manland factories are dead in production — this
                // binder is the ONLY path that gives a real-match manland its
                // animate ability + attack trigger. Reuses the shared
                // AnimateLandEffect / keyword primitives the factories use.
                ManlandBinder.Bind(card, entity, controller, effects, triggers);
                OracleLoyaltyAbilityBinder.Bind(card, entity, controller);
                // CR 305.7 — additive land-retype static ("Each land is a
                // [basic] in addition to its other land types"): Urborg, Tomb
                // of Yawgmoth / Yavimaya, Cradle of Growth. Lands are never
                // routed through their [CardName] factory (FactoryRouting), so
                // the factory's effects-aware overload that wires this static
                // is never reached in prod — this binder wires it against the
                // live per-game CES + event bus instead.
                AdditiveLandSubtypeBinder.Bind(card, entity, effects, eventBus);
                return;
            }
        }

        // Fallback: no repo or unknown card — attach basic-land mana only.
        OracleManaBinder.BindBasicLandMana(card, controller);
    }
}
