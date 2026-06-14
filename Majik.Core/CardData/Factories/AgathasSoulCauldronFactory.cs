using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Agatha's Soul Cauldron (Wilds of Eldraine).
///
/// Legendary Artifact — {2}. Oracle text:
///   "You may spend mana as though it were mana of any color to activate
///    abilities of creatures you control.
///    Creatures you control with +1/+1 counters on them have all activated
///    abilities of all creature cards exiled with Agatha's Soul Cauldron.
///    {T}: Exile target card from a graveyard. When a creature card is
///    exiled this way, put a +1/+1 counter on target creature you control."
///
/// ## Implemented
/// - Correct name, type (Legendary Artifact), mana cost ({2}), owner/controller.
/// - <b>{T}: Exile target card from a graveyard</b> activated ability with
///   real targeting: "target card from a graveyard" ranges over EVERY
///   player's graveyard, and "target creature you control" is collected as
///   the optional counter recipient. On resolution the chosen card is exiled
///   from whichever graveyard holds it; if it is a creature card it is
///   imprinted (CR 702.49) and the chosen creature you control gets a +1/+1
///   counter.
/// - <b>Static: mana-colour-substitution</b> (CR 609.4b): "you may spend mana
///   as though it were mana of any color to activate abilities of creatures
///   you control" is wired as a
///   <see cref="Majik.Core.Costs.ManaColorSubstitutionPermission"/> with
///   <see cref="Majik.Core.Costs.ManaSpendPurpose.ActivateCreatureAbilities"/>.
///   The reusable payment-time substitution primitive (shared with Robber of
///   the Rich's clause) folds a creature ability's coloured pips into generic
///   when this permission is active, so a
///   <see cref="Majik.Core.Costs.ManaColorSubstitutableManaCost"/> used as the
///   mana component of a creature's activated ability accepts any colour. The
///   mana value is unchanged (CR 106.6) — only which mana qualifies widens.
/// - <b>Static: ability-grant via imprint (MANA abilities)</b> (CR 613.1f /
///   702.49): "creatures you control with +1/+1 counters on them have all
///   activated abilities of all creature cards exiled with Agatha's Soul
///   Cauldron." Wired via the Layer-6 group-grant primitive
///   (<see cref="Majik.Core.Effects.GrantAbilityToGroupStaticEffect"/> /
///   <see cref="Majik.Core.Effects.GrantAbilityToGroupLifecycle"/>):
///   <list type="bullet">
///     <item><b>scope</b> — every <see cref="Creature"/> the Cauldron
///       controller controls that currently has at least one +1/+1 counter.</item>
///     <item><b>abilityFactory(bearer)</b> — for each creature card imprinted
///       on the Cauldron, the engine RE-HOMES that card's "{T}: Add …" mana
///       ability to <paramref name="bearer"/>: it parses the imprinted card's
///       oracle text (<see cref="Majik.Core.CardData.OracleManaBinder.ParseTapManaCosts"/>)
///       and builds fresh <see cref="ManaAbility"/> instances whose source is
///       the BEARER. A re-homed mana ability taps the bearer (not the exiled
///       card) and adds mana to the bearer-controller's pool, which is the only
///       sound re-home: the engine's abilities are closures over their source
///       (<see cref="ManaAbility.Source"/>), and a mana ability is fully
///       reconstructable from oracle text against a new source.</item>
///   </list>
///   Live membership is recomputed as creatures gain / lose counters or
///   enter / leave (CR 611.2c); on each imprint the grant is
///   <see cref="GrantAbilityToGroupLifecycle.Refresh"/>ed so existing bearers
///   pick up the new card's abilities; removing the Cauldron revokes every
///   grant (CR 613.6e).
///
/// - <b>Static: ability-grant via imprint (NON-mana activated abilities)</b>
///   (CR 613.1f / 702.49). Two complementary mechanisms re-home an imprinted
///   creature's non-mana activated abilities to each bearer, every granted
///   ability sourced on the bearer (cost taps/sacrifices the bearer; the effect
///   acts on the bearer — "this creature" = bearer):
///   <list type="bullet">
///     <item><b>PRIMARY — RebindTo the REAL ability</b> (CR 707.2). For each
///       imprinted creature, its ACTUAL
///       <see cref="ActivatedAbility"/>s (everything except mana abilities) are
///       re-homed via <see cref="ActivatedAbility.RebindTo"/>, which re-sources
///       the costs (Stage 1) and reuses the real effect objects. This covers
///       WHATEVER abilities the card actually has — not just oracle-parseable
///       shapes — so it generalises far past the firebreathing/pinger/sac set.
///       It runs ONLY for abilities the build path marked
///       <see cref="ActivatedAbility.RebindSafe"/> = true, i.e. those whose
///       every effect reads its source/subject off the live
///       <see cref="Majik.Core.Abilities.ResolutionContext"/> rather than a
///       captured permanent. All DATA-DRIVEN (CardDef/JSON) activated abilities
///       qualify: their self-source verbs (pump / connive / explore) were
///       migrated to read <see cref="Majik.Core.Abilities.ResolutionContext.Source"/>,
///       and the rest are scoped to the controller or to chosen targets.</item>
///     <item><b>FALLBACK — oracle-rebuild</b> via
///       <see cref="Majik.Core.CardData.OracleActivatedAbilityBinder"/> (the
///       non-mana sibling of
///       <see cref="Majik.Core.CardData.OracleManaBinder.ParseTapManaCosts"/>).
///       Used ONLY when RebindTo produced nothing for a card — i.e. the
///       creature's real abilities are bespoke <c>[CardName]</c>-factory
///       closures (not <see cref="ActivatedAbility.RebindSafe"/>) or it carries
///       no engine-built ability at all (oracle-text-only). It reconstructs the
///       soundly-rebuildable shapes from oracle text: firebreathing / self-pump
///       ("{cost}: This creature gets ±X/±Y until end of turn" — including a
///       SIGNED/negative delta as on Aetherling / Canyon Crab / Flowstone), self-keyword
///       grant ("{cost}: This creature gains &lt;simple keyword&gt; until end of
///       turn"), self-counter ("{cost}: Put a/N +1/+1 counter(s) on this
///       creature"), regenerate-self ("{cost}: Regenerate this creature" — River
///       Boa / Drudge Skeletons / Wall of Bone, reminder text tolerated),
///       pinger ("{cost}: This creature deals N damage to …"), and
///       sacrifice-self pinger ("Sacrifice this creature: It deals N damage to
///       …"). Cost grammar: any ", "-separated list of generic / coloured mana
///       pips and {T}.</item>
///   </list>
///
/// ## Deferred (precise remaining gap)
/// - <b>Bespoke <c>[CardName]</c>-factory activated abilities</b> whose effect
///   closures still capture the original card (so they are NOT
///   <see cref="ActivatedAbility.RebindSafe"/>) AND whose oracle text is outside
///   the fallback's soundly-reconstructable set (firebreathing / self-pump /
///   self-keyword / self-counter / regenerate-self / pinger / sac-pinger). For
///   such an imprinted creature the grant emits nothing for
///   those abilities rather than re-home a closure that would tap/affect the
///   EXILED card. As more bespoke effects migrate to read
///   <see cref="Majik.Core.Abilities.ResolutionContext.Source"/> (and their
///   factory marks the ability <see cref="ActivatedAbility.RebindSafe"/>), the
///   RebindTo path covers them automatically. Bespoke factories have begun
///   opting in: <see cref="LotlethTrollFactory"/>'s discard-pump (a bespoke
///   <c>DiscardACreatureCardCost</c> the oracle fallback cannot reconstruct) +
///   {B} regenerate now read
///   <see cref="Majik.Core.Abilities.ResolutionContext.Source"/> and are
///   <see cref="ActivatedAbility.RebindSafe"/>, so the RebindTo path re-homes
///   the REAL abilities to a bearer. The bespoke <b>regenerate-self</b> batch
///   has joined: <see cref="SkithiryxTheBlightDragonFactory"/> (its printed
///   "Regenerate Skithiryx" names the creature, so the oracle fallback's
///   "Regenerate this creature" form CANNOT reconstruct it — only RebindTo of
///   the real ability re-homes it), <see cref="MortivoreFactory"/> and
///   <see cref="RiverBoaFactory"/> now read
///   <see cref="Majik.Core.Abilities.ResolutionContext.Source"/> and are
///   <see cref="ActivatedAbility.RebindSafe"/>. The variable-X mv-sweep batch
///   has joined: <see cref="SteelHellkiteFactory"/>'s "{X}: Destroy each nonland
///   permanent with mv X whose controller was dealt combat damage by this
///   creature this turn" now keys its combat-victim tracker by the
///   damage-SOURCE permanent and reads its victim set + X off the live
///   <see cref="Majik.Core.Abilities.ResolutionContext"/>
///   (<see cref="Majik.Core.Abilities.ResolutionContext.Source"/> +
///   <see cref="Majik.Core.Abilities.ResolutionContext.ChosenX"/>), so RebindTo
///   re-homes the REAL sweep to a bearer and it destroys permanents whose
///   controller the BEARER damaged — never the exiled card's stale linkage. The
///   residual is now confined to
///   the remaining un-migrated bespoke-factory closures — every data-driven
///   activated ability is covered. A correct partial beats a broken "all".
///   Tracked: v1-deferrals.
/// </summary>
[CardName("Agatha's Soul Cauldron")]
public static class AgathasSoulCauldronFactory
{
    /// <summary>
    /// Shape-only constructor — Agatha's Soul Cauldron with correct identity,
    /// the {T} exile ability, imprint storage, and the mana-colour-substitution
    /// static, but NO live ability-grant (no continuous-effects service to
    /// register against). Suitable for factory-shape / naming tests.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, effects: null, eventBus: null, allPlayersProvider: null, oracleLookup: null);

    /// <summary>
    /// Production effects-aware overload matched by the source generator's
    /// instance-swap dispatch (the routed prod build requires this exact
    /// <c>Create(Player, ContinuousEffectsService)</c> signature). Wires the
    /// Layer-6 ability-grant static against the live service, taking the event
    /// bus + whole-battlefield player roster from the service so the grant
    /// tracks creatures entering / leaving / changing control / gaining counters
    /// (CR 611.2c). Oracle text for imprinted creatures is resolved from the
    /// embedded card pool.
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? effects)
        => Create(
            owner,
            effects,
            eventBus: effects?.EventBus,
            allPlayersProvider: effects?.PlayersProvider,
            oracleLookup: null);

    /// <summary>
    /// Fully-wired Agatha's Soul Cauldron.
    /// </summary>
    /// <param name="owner">Owner + initial controller.</param>
    /// <param name="effects">Continuous-effects service the Layer-6 group grant
    /// registers against. Null ⇒ no live grant (card-shape only).</param>
    /// <param name="eventBus">Bus for <see cref="CardMovedEvent"/> so the grant
    /// tracks the Cauldron + group members entering / leaving play.</param>
    /// <param name="allPlayersProvider">Whole-board roster so "creatures you
    /// control" enumerates a stolen creature you control but an opponent owns
    /// (CR 110.2 / 700.6). Null ⇒ the controller's own battlefield only.</param>
    /// <param name="oracleLookup">Resolves an imprinted card's
    /// <see cref="CardEntity"/> by name (for its oracle text). Null ⇒ the
    /// embedded card pool (constructed lazily, once). Injectable for tests so a
    /// synthetic imprinted card can carry oracle text without the full seed.</param>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        System.Func<System.Collections.Generic.IEnumerable<Player>?>? allPlayersProvider,
        System.Func<string, CardEntity?>? oracleLookup)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Legendary Artifact — the printed supertype must be set here: the
        // routed prod build path (NamedCardFactory.Create + OverlayAdditiveBinders)
        // overlays only keyword/mana/ETB binders, NOT supertypes, so a factory
        // that omits Legendary would lose the legend rule (CR 704.5j).
        var cauldron = new Artifact(
            "Agatha's Soul Cauldron", "{2}",
            supertypes: new[] { CardSupertype.Legendary });
        cauldron.SetOwner(owner);
        cauldron.SetController(owner);

        // ----------------------------------------------------------------
        // "You may spend mana as though it were mana of any color to activate
        // abilities of creatures you control." (CR 609.4b.)
        // ----------------------------------------------------------------
        cauldron.AddAbility(new ManaColorSubstitutionPermission(
            cauldron, owner, ManaSpendPurpose.ActivateCreatureAbilities));

        // ----------------------------------------------------------------
        // "Creatures you control with +1/+1 counters on them have all
        // activated abilities of all creature cards exiled with Agatha's Soul
        // Cauldron." (CR 613.1f Layer-6 ability-grant; CR 702.49 imprint.)
        //
        // Only the MANA-ability slice is granted — see the type doc's Deferred
        // note. The lifecycle is captured so each imprint Refreshes it.
        // ----------------------------------------------------------------
        GrantAbilityToGroupLifecycle? grantLifecycle = null;
        if (effects != null)
        {
            var lookup = oracleLookup ?? DefaultOracleLookup;

            var players = allPlayersProvider ?? effects.PlayersProvider;
            var membership = players != null
                ? BattlefieldGroupGatherer.WholeBattlefield(players)
                : (System.Func<System.Collections.Generic.IEnumerable<Permanent>>)(
                    () => ControllerBattlefield(cauldron));

            grantLifecycle = new GrantAbilityToGroupLifecycle(
                cauldron,
                effects,
                eventBus,
                // CR 613.1f scope: creatures the Cauldron controller controls
                // that currently bear a +1/+1 counter.
                scope: p => p is Creature
                            && ReferenceEquals(p.Controller, cauldron.Controller)
                            && p.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne) > 0,
                abilityFactory: bearer => BuildGrantedAbilities(cauldron, bearer, lookup),
                membershipProvider: membership,
                // CR 702.49 — when the Cauldron LEAVES the battlefield, detach
                // the imprint linkage: the imprinted cards STAY in exile (they
                // do NOT return) but lose their back-link, so a client no longer
                // renders them under the (now-gone) Cauldron and a fresh Cauldron
                // does not pick them up. The grant itself is already revoked by
                // the lifecycle before this fires.
                onLeaveBattlefield: () => DetachImprints(cauldron));
            grantLifecycle.Attach();
        }

        // ----------------------------------------------------------------
        // {T}: Exile target card from a graveyard.
        // When a creature card is exiled this way, put a +1/+1 counter on
        // target creature you control.
        // ----------------------------------------------------------------
        ActivatedAbility? tapAbility = null;

        var exileEffect = new Effect(
            "Agatha's Soul Cauldron: exile target card from a graveyard, then counter if creature",
            () =>
            {
                if (tapAbility == null) return;
                var chosen = tapAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not ICard target) return;

                // CR 608.2b — the card must still be in a graveyard at
                // resolution. Exile it from whichever graveyard holds it.
                var gyOwner = target.Owner;
                if (gyOwner == null || target.Zone != ZoneType.Graveyard) return;

                gyOwner.Zones.Graveyard.RemoveCard(target);
                gyOwner.Zones.Exile.AddCard(target);
                target.SetZone(ZoneType.Exile);

                if (target.HasType(CardType.Creature))
                {
                    // CR 702.49 — imprint: record this creature card on the
                    // Cauldron so the ability-grant static can reference it, and
                    // set the card-side back-link to THIS Cauldron instance so a
                    // client can render the exiled card under it.
                    cauldron.AddImprinted(target);
                    target.SetExiledWith(cauldron.InstanceId);

                    // CR 611.2c — the set of granted abilities just changed
                    // (a new imprinted creature) although no member entered /
                    // left the group. Re-home the new card's mana abilities to
                    // every current bearer.
                    grantLifecycle?.Refresh();

                    // "put a +1/+1 counter on target creature you control" —
                    // the recipient chosen up front (request index 1). No-op
                    // when none was chosen (no legal creature).
                    if (chosen.Count > 1 && chosen[1].Count > 0
                        && chosen[1][0] is Creature recipient)
                    {
                        recipient.Counters.Add(
                            Majik.Core.Counters.CounterType.PlusOnePlusOne, 1);

                        // A creature that just GAINED its first counter newly
                        // matches the scope; reconcile membership so it picks up
                        // the grant immediately (CR 611.2c).
                        grantLifecycle?.Refresh();
                    }
                }
            });

        tapAbility = new ActivatedAbility(
            source: cauldron,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(cauldron) },
            effects: new IEffect[] { exileEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "exile target card from a graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.None,
                    // "a graveyard" — any player's graveyard (CR 109 / 400.3).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Graveyard.GetCards())
                        .Cast<object>()
                        .ToList()),
                new TargetRequest(
                    Description: "target creature you control (counter recipient)",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.None,
                    // "creature you control" — the Cauldron controller's creatures.
                    CandidateGatherer: ctx =>
                    {
                        var controller = cauldron.Controller ?? owner;
                        return controller.Zones.Battlefield.GetCards()
                            .OfType<Creature>()
                            .Cast<object>()
                            .ToList();
                    }),
            });

        cauldron.AddAbility(tapAbility);

        return cauldron;
    }

    /// <summary>
    /// Build the abilities granted to one <paramref name="bearer"/> by the
    /// imprint static: for EACH creature card imprinted on
    /// <paramref name="cauldron"/>, re-home that card's activated abilities to
    /// the bearer (CR 613.1f / 605.1a). Two re-source-able binders run against
    /// the imprinted card's oracle text:
    /// <list type="bullet">
    ///   <item><b>MANA abilities</b> — "{T}: Add …" via
    ///     <see cref="OracleManaBinder.ParseTapManaCosts"/>; rebuilt as fresh
    ///     <see cref="ManaAbility"/> instances sourced on the bearer.</item>
    ///   <item><b>NON-mana activated abilities</b> — firebreathing / self-pump
    ///     ("{cost}: This creature gets +X/+Y until end of turn"), self-keyword
    ///     grants ("{cost}: This creature gains &lt;simple keyword&gt; until end
    ///     of turn"), self-counter ("{cost}: Put a/N +1/+1 counter(s) on this
    ///     creature"), regenerate-self ("{cost}: Regenerate this creature"),
    ///     draw-a-card ("{cost}: Draw N cards"), gain-life ("{cost}: You gain N
    ///     life"), pingers ("{cost}: This creature deals N damage to …"), and
    ///     sacrifice-self pingers ("Sacrifice this creature: It deals N damage
    ///     to …") via
    ///     <see cref="OracleActivatedAbilityBinder.RebuildActivatedAbilities"/>;
    ///     rebuilt as fresh <see cref="ActivatedAbility"/> instances whose cost
    ///     taps/sacrifices the BEARER and whose effect references the BEARER
    ///     ("this creature" = bearer).</item>
    /// </list>
    /// Every granted ability is sourced on the bearer, so it acts on the bearer
    /// (taps/sacrifices/pumps the bearer; mana goes to the bearer-controller's
    /// pool) — never the exiled card. Bespoke activated abilities the
    /// <see cref="OracleActivatedAbilityBinder"/> can't soundly rebuild are
    /// SKIPPED, not emitted broken (see its soundness boundary).
    /// </summary>
    private static IReadOnlyList<IAbility> BuildGrantedAbilities(
        Artifact cauldron,
        Permanent bearer,
        System.Func<string, CardEntity?> oracleLookup)
    {
        var controller = bearer.Controller ?? cauldron.Controller;
        if (controller == null) return Array.Empty<IAbility>();

        var granted = new List<IAbility>();
        foreach (var imprinted in cauldron.ImprintedCards)
        {
            // Only CREATURE cards exiled with the Cauldron grant abilities.
            if (!imprinted.HasType(CardType.Creature)) continue;

            var entity = oracleLookup(imprinted.Name);
            var oracleText = entity?.OracleText;

            // MANA slice — "{T}: Add …" re-homed to the bearer. (Mana abilities
            // are IManaAbility, not ActivatedAbility, so they are NOT picked up
            // by the RebindTo pass below; they keep their oracle-rebuild path,
            // which reconstructs them soundly against the bearer.)
            foreach (var manaCost in OracleManaBinder.ParseTapManaCosts(oracleText))
            {
                granted.Add(new ManaAbility(bearer, controller, manaCost));
            }

            // ----------------------------------------------------------------
            // NON-mana activated abilities.
            //
            // STAGE 2/3 — PREFER re-homing the imprinted creature's REAL
            // activated abilities via ActivatedAbility.RebindTo (CR 707.2): this
            // covers WHATEVER abilities the card actually has, not just the
            // oracle-parseable shapes, and reuses the real effect objects. It is
            // only sound when the ability re-sources itself, which the
            // ActivatedAbility.RebindSafe flag asserts (data-driven CardDef
            // abilities, whose self-source verbs read ResolutionContext.Source).
            // We rebind those and remember which oracle clauses they cover so the
            // oracle-rebuild fallback below does not ALSO emit a duplicate.
            // ----------------------------------------------------------------
            var rebound = imprinted.Abilities
                .OfType<ActivatedAbility>()
                .Where(a => a is not IManaAbility && a.RebindSafe)
                .Select(a => a.RebindTo(bearer, controller))
                .ToList();

            var anyRebound = rebound.Count > 0;
            granted.AddRange(rebound);

            // FALLBACK — for imprinted creatures whose real abilities are NOT
            // RebindSafe (bespoke [CardName]-factory closures that capture the
            // exiled card) or that carry no engine-built abilities at all (e.g.
            // a card loaded oracle-text-only), reconstruct the soundly-rebuildable
            // non-mana shapes — firebreathing / pinger / sac-pinger — from oracle
            // text, re-homed to the bearer. We only run this when RebindTo
            // produced nothing for this card, so a CardDef creature is granted
            // its real ability exactly once (no double-grant), while a bespoke /
            // oracle-only creature still gets the parseable partial.
            if (!anyRebound)
            {
                foreach (var ability in OracleActivatedAbilityBinder.RebuildActivatedAbilities(
                             oracleText, bearer, controller))
                {
                    granted.Add(ability);
                }
            }
        }
        return granted;
    }

    /// <summary>
    /// CR 702.49 leave-the-battlefield teardown for the imprint linkage. When
    /// the Cauldron leaves the battlefield the exiled cards STAY in exile (they
    /// do NOT return — leaving them where they are is correct), but they lose
    /// their link to the Cauldron: clear each imprinted card's
    /// <see cref="Card.ExiledWith"/> back-link, then clear the Cauldron's own
    /// imprint list. After this, the cards are plain exile — no client renders
    /// them under the gone Cauldron, and a fresh Cauldron (a different instance
    /// with its own empty imprint list + its own per-instance grant) never
    /// grants their abilities.
    /// </summary>
    private static void DetachImprints(Artifact cauldron)
    {
        foreach (var imprinted in cauldron.ImprintedCards)
        {
            imprinted.ClearExiledWith();
        }
        cauldron.ClearImprinted();
    }

    // Lazily-constructed embedded card pool: building it loads ~22k rows, so do
    // it once and only when a real (non-injected) lookup is actually needed.
    private static readonly Lazy<EmbeddedCardRepository> SharedRepo =
        new(() => new EmbeddedCardRepository());

    private static CardEntity? DefaultOracleLookup(string name)
        => SharedRepo.Value.GetByName(name);

    /// <summary>
    /// Default candidate set when no whole-board roster is wired: the Cauldron
    /// controller's own battlefield. (A "creatures you control" match for a
    /// stolen creature you control but an opponent owns needs every player's
    /// battlefield — supplied by the whole-battlefield gatherer.)
    /// </summary>
    private static IEnumerable<Permanent> ControllerBattlefield(Artifact cauldron)
    {
        var controller = cauldron.Controller;
        if (controller == null) return Array.Empty<Permanent>();
        return controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => p.Zone == ZoneType.Battlefield);
    }
}
