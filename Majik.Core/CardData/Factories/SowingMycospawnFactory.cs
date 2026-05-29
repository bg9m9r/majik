using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sowing Mycospawn (Modern Horizons 3, {3}{G}).
///
/// Creature — Eldrazi Fungus 3/3. Oracle text (Scryfall, verified):
///   "Devoid (This card has no color.)
///    Kicker {1}{C} (You may pay an additional {1}{C} as you cast this spell.)
///    When you cast this spell, search your library for a land card,
///        put it onto the battlefield, then shuffle.
///    When you cast this spell, if it was kicked, exile target land."
///
/// ## Implemented (v1)
///
/// - 3/3 Creature — Eldrazi Fungus at <c>{3}{G}</c>.
///
/// - <b>Devoid (CR 702.114)</b> — printed keyword. The factory stamps
///   <see cref="Card.IsDevoid"/> on the card; <see cref="CardColors.GetColors"/>
///   short-circuits to the empty set when the flag is set, so a tutor
///   like Green Sun's Zenith or a "target colored permanent" gatherer
///   (Devourer of Destiny's cast trigger) sees Sowing Mycospawn as
///   colorless even though its mana cost contains a {G} pip.
///   <see cref="KeywordAbility"/> marker (Keyword = "Devoid") is also
///   attached for ability-scan observability (mirrors the Reach /
///   Annihilator pattern).
///
/// - <b>Kicker {1}{C} (CR 702.33)</b> — real <see cref="KickerAdditionalCost"/>
///   primitive, mirroring <see cref="OrimSChantFactory.BuildAdditionalCost"/>.
///   <see cref="Card.WasKicked"/> is stamped at cast-announcement by
///   <see cref="KickerAdditionalCost.Pay"/> and cleared post-resolution
///   by <see cref="SpellCastFlow"/>'s cleanup effect.
///
/// - <b>Cast trigger A — "When you cast this spell, search your library
///   for a land card, put it onto the battlefield, then shuffle."
///   (CR 603.6a / CR 603.10)</b> — triggered ability over
///   <see cref="SpellCastEvent"/> filtered to self-cast
///   (<c>ReferenceEquals(e.Spell.Card, card)</c>); active in
///   <see cref="ZoneType.Stack"/> because the spell is on the stack at
///   cast time (Devourer of Destiny posture). On resolution: delegates
///   to <see cref="SearchSpellFactory.SearchLandToBattlefieldSpell"/>
///   with <c>kindRaw = "land"</c> and <c>tapped = false</c> — same
///   tutor template used by <see cref="RampantGrowthFactory"/> /
///   <see cref="SylvanScryingFactory"/>; the "any land" predicate is
///   broader than basic (Sowing Mycospawn searches for ANY land card —
///   tutored shock land / fetch land / utility land all qualify, CR
///   305 — and lands are not basic-restricted).
///
/// - <b>Cast trigger B — "When you cast this spell, if it was kicked,
///   exile target land." (CR 603.6a / CR 603.4 / CR 701.21)</b> —
///   second triggered ability over <see cref="SpellCastEvent"/> with
///   the same self-cast filter, but the trigger registers a
///   1..1 "target land" <see cref="TargetRequest"/> only — the
///   "if it was kicked" check is the <see cref="TriggeredAbility.InterveningIf"/>
///   condition (CR 603.4 — intervening-if is checked when the trigger
///   first looks to fire AND again at resolution). The check reads
///   <see cref="Card.WasKicked"/> off the cast card; non-kicked casts
///   never register on the stack. The gatherer scans every player's
///   battlefield and yields any permanent with
///   <see cref="CardType.Land"/>. On resolution rechecks Land +
///   on-battlefield (CR 608.2b) and exiles the target via
///   <see cref="ZoneService.MoveCard"/> (Battlefield → Exile) when
///   supplied; raw zone manipulation otherwise.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Abilities attached;
///   triggers not registered; exile uses raw zone manipulation.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> —
///   fully wired. Cast triggers register with <paramref name="triggers"/>;
///   exile routes through <paramref name="zones"/>; land tutor pulls
///   the ZoneService off <see cref="ZoneServiceRegistry"/> via the
///   shared search template.
///
/// ## Analogues
/// - Devoid: NEW marker on <see cref="Card.IsDevoid"/>; consumed by
///   <see cref="CardColors.GetColors"/>.
/// - Kicker: <see cref="OrimSChantFactory"/> / <see cref="BurstLightningFactory"/>.
/// - Cast trigger (self-cast SpellCastEvent / stack-active): Devourer
///   of Destiny.
/// - Land tutor: <see cref="RampantGrowthFactory"/> via
///   <see cref="SearchSpellFactory.SearchLandToBattlefieldSpell"/>.
/// - Exile target land: <see cref="WorldBreakerFactory"/>'s ETB exile
///   posture, generalised to "any land" (not just nonbasic).
/// </summary>
[CardName("Sowing Mycospawn")]
public static class SowingMycospawnFactory
{
    public const string CardName = "Sowing Mycospawn";
    public const string PrintedManaCost = "{3}{G}";
    public const string KickerCostText = "{1}{C}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>Construct Sowing Mycospawn with no live wiring. Abilities
    /// are attached for shape observability; cast triggers aren't
    /// registered with any <see cref="TriggerManager"/>; the kicked
    /// exile uses raw zone manipulation. Suitable for dispatcher /
    /// structural tests.</summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>Construct Sowing Mycospawn with optional runtime services.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the kicked-cast-trigger exile
    /// routes through <see cref="ZoneService.MoveCard"/> so
    /// <see cref="CardMovedEvent"/> publishes for any zone-change
    /// subscribers. The always-cast-trigger land tutor pulls its
    /// ZoneService off <see cref="ZoneServiceRegistry"/> via the
    /// shared search template.</param>
    /// <param name="triggers">When supplied, both cast triggers
    /// register with the manager so SpellCastEvent lands them on the
    /// stack automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Eldrazi, CardSubtype.Fungus });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.114 — Devoid. Stamp the IsDevoid flag so
        // CardColors.GetColors returns empty regardless of the {G} pip
        // in the mana cost; also attach the KeywordAbility marker so a
        // keyword scan ("does this card have Devoid?") observes it
        // without round-tripping through the color predicate.
        card.SetDevoid(true);
        card.AddAbility(new KeywordAbility(DevoidKeyword, card, owner));

        // ----------------------------------------------------------------
        // Cast trigger A — "When you cast this spell, search your
        //   library for a land card, put it onto the battlefield, then
        //   shuffle." CR 603.6a / CR 603.10.
        // Fires while Sowing Mycospawn is on the stack (SpellCastEvent
        // is published as the spell moves to the stack), so
        // activeZones = Stack — same posture as Devourer of Destiny.
        // The land tutor body delegates to the shared search template
        // (kind = "land", tapped = false).
        // ----------------------------------------------------------------
        var tutorCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) => ReferenceEquals(e.Spell.Card, card));

        var tutorEffect = new Effect(
            $"{CardName}: tutor any land -> battlefield (cast trigger)",
            () => ResolveLandTutor(owner));

        var tutorTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: tutorCondition,
            effects: new IEffect[] { tutorEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Stack },
            // No external target — the land is chosen during the tutor
            // search, not as a TargetRequest (CR 701.19a, not CR 115).
            targetRequests: Array.Empty<TargetRequest>());

        card.AddAbility(tutorTrigger);
        triggers?.RegisterTriggeredAbility(tutorTrigger);

        // ----------------------------------------------------------------
        // Cast trigger B — "When you cast this spell, if it was kicked,
        //   exile target land." CR 603.6a / CR 603.4 / CR 701.21.
        // Same self-cast filter as trigger A; the "if it was kicked"
        // clause is an intervening-if condition (CR 603.4 — checked
        // both at trigger-event and at resolution). Non-kicked casts
        // never put this trigger on the stack.
        // ----------------------------------------------------------------
        TriggeredAbility? exileTrigger = null;
        var exileCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) => ReferenceEquals(e.Spell.Card, card));

        var exileEffect = new Effect(
            $"{CardName}: exile target land (kicked cast trigger)",
            () => ResolveExileLand(exileTrigger, owner, zones));

        exileTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: exileCondition,
            effects: new IEffect[] { exileEffect },
            // CR 603.4 — intervening "if" clause. Reads Card.WasKicked
            // off the cast card; the cast-time stamp from
            // KickerAdditionalCost.Pay is still live here because
            // SpellCastFlow appends the kicker-cleanup effect AFTER the
            // resolution body runs (and this is a trigger ON the cast,
            // not the spell's printed body — see SpellCastFlow ordering).
            interveningIf: () => card.WasKicked,
            activeZones: new[] { ZoneType.Stack },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // CR 305 — any Land permanent is a legal target
                    // (basic or nonbasic; the printed text is plain
                    // "target land", not "target nonbasic land").
                    CandidateGatherer: GatherLands),
            });

        card.AddAbility(exileTrigger);
        triggers?.RegisterTriggeredAbility(exileTrigger);

        return card;
    }

    // -------------------------------------------------------------------
    // Public constants / helpers
    // -------------------------------------------------------------------

    /// <summary>CR 702.114 — the Devoid keyword marker string used by
    /// the <see cref="KeywordAbility"/> the factory attaches for
    /// ability-scan discoverability.</summary>
    public const string DevoidKeyword = "Devoid";

    /// <summary>Build the <see cref="IAdditionalCost"/> for Sowing
    /// Mycospawn's kicker {1}{C}. Mirrors
    /// <see cref="OrimSChantFactory.BuildAdditionalCost"/>.</summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }

    // -------------------------------------------------------------------
    // Resolution helpers
    // -------------------------------------------------------------------

    private static List<object> GatherLands(GameContext ctx)
    {
        var pool = new List<object>();
        foreach (var p in ctx.AllPlayers)
        {
            foreach (var c in p.Zones.Battlefield.GetCards())
            {
                if (c.HasType(CardType.Land)) pool.Add(c);
            }
        }
        return pool;
    }

    private static void ResolveLandTutor(Player owner)
    {
        // Reuse the shared "search for a land, put onto battlefield"
        // template (CR 701.19a search + CR 701.20a shuffle). tapped=false
        // honours Sowing Mycospawn's printed text — no tapped rider.
        // The template builds a SpellDefinition; we materialise its
        // effects and execute them inline (the trigger isn't a spell
        // cast, so we don't go through SpellCastFlow).
        var spellDef = SearchSpellFactory.SearchLandToBattlefieldSpell(
            owner, kindRaw: "land", tapped: false);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);
        foreach (var ef in spellDef.EffectFactory(chosen))
        {
            ef.Execute();
        }
    }

    private static void ResolveExileLand(
        TriggeredAbility? exileTrigger,
        Player owner,
        ZoneService? zones)
    {
        if (exileTrigger == null) return;
        var chosen = exileTrigger.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return;

        if (chosen[0][0] is not ICard target) return;
        // CR 608.2b — illegal-on-resolution check.
        if (!target.HasType(CardType.Land)) return;
        if (target.Zone != ZoneType.Battlefield) return;

        ExilePermanent(target, owner, zones);
    }

    private static void ExilePermanent(ICard permCard, Player owner, ZoneService? zones)
    {
        // CR 701.21 — exile is NOT a destroy effect; indestructible
        // permanents are exiled normally. Route through ZoneService when
        // supplied so CardMovedEvent fires (Containment Priest et al.).
        if (zones != null)
        {
            zones.MoveCard(permCard, ZoneType.Battlefield, ZoneType.Exile);
            return;
        }
        var permController = permCard.Controller ?? permCard.Owner;
        permController?.Zones.Battlefield.RemoveCard((Card)permCard);
        var exileOwner = permCard.Owner ?? owner;
        exileOwner.Zones.Exile.AddCard((Card)permCard);
        if (permCard is Card c) c.SetZone(ZoneType.Exile);
    }
}
