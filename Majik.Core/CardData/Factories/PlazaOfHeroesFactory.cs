using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Plaza of Heroes (Dominaria United).
///
/// Land. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {T}: Add one mana of any color. Spend this mana only to cast a
///    legendary spell.
///    {T}: Add one mana of any color among legendary permanents you
///    control.
///    {3}, {T}, Exile this land: Target legendary creature gains hexproof
///    and indestructible until end of turn."
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed supertypes/subtypes — Plaza of Heroes is
///   a non-legendary "Land", no basic-land subtype) loaded from
///   <c>Majik.Core/CardData/Cards/plaza-of-heroes.json</c> via
///   <see cref="CardDefinitionFactory"/>. Same JSON-identity posture as
///   <see cref="ForbiddenOrchardFactory"/> /
///   <see cref="DelightedHalflingFactory"/>; the any-colour fan-out and the
///   activated protection ability are attached in C# because the data-only
///   schema can't express them.
/// - <b>{T}: Add {C}</b> — vanilla unrestricted <see cref="ManaAbility"/>
///   (CR 605.1, no stack). {C} buckets as Generic +1 via
///   <see cref="ManaCost.Parse"/>, same as Vault of the Archangel / Karn's
///   Bastion.
/// - <b>{T}: Add one mana of any color. Spend this mana only to cast a
///   legendary spell.</b> — five <see cref="ManaAbility"/> instances (one per
///   WUBRG), each stamped with a shared "legendary spell"
///   <see cref="SpendRestriction"/> (CR 205.4a / 106.4). Identical posture to
///   <see cref="DelightedHalflingFactory"/>'s legendary-only mana — the
///   payment-gate enforcement (filtering tagged pool entries off a
///   non-legendary cost) is deferred until <see cref="ManaPool"/> grows
///   per-slot tags; today the restriction is observational metadata on the
///   ability (see <see cref="SpendRestriction"/> xmldoc). All
///   SpendRestriction cards unlock together.
/// - <b>{T}: Add one mana of any color among legendary permanents you
///   control.</b> — five unrestricted <see cref="ManaAbility"/> instances
///   (one per WUBRG), each gated by a <c>canActivateCheck</c> that returns
///   <c>true</c> only when that colour appears among the colours
///   (CR 105 / CR 202.2) of the legendary permanents the controller controls
///   (CR 106.6 — "any color among permanents you control" reads the colour
///   set of the qualifying permanents at activation time). With no qualifying
///   legendary permanent the mode is uncastable for every colour.
/// - <b>{3}, {T}, Exile this land: Target legendary creature gains hexproof
///   and indestructible until end of turn.</b> — an ordinary
///   <see cref="ActivatedAbility"/> (CR 602, uses the stack; NOT a mana
///   ability). Cost stack: <see cref="ManaCostCost"/>("{3}") +
///   <see cref="AdditionalCost.Tap"/> + a self-exile cost. The self-exile
///   zone move (Battlefield → Exile) is performed in the effect closure
///   because the generic additional-cost pay path has no exile primitive —
///   same posture as <see cref="SentinelTotemFactory"/> (which reuses
///   <see cref="AdditionalCost.Sacrifice"/> as the placeholder cost and does
///   the real zone move in the closure). The ability carries a single 1..1
///   "target legendary creature" <see cref="TargetRequest"/> (CR 603.3d). On
///   resolve (CR 608.2b illegal-target guard first) it registers a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> for "Hexproof"
///   (CR 702.11b) and one for "Indestructible" (CR 702.12b) against the
///   target's own <see cref="Permanent.ActiveEffects"/> layer service
///   (CR 613 Layer 6), both expiring at cleanup (CR 514.2). Mirrors
///   <see cref="TamiyosSafekeepingFactory"/>'s hexproof+indestructible grant.
///
/// ## v1 simplifications / deferrals
/// - <b>Legendary-only spend-gate</b>: the second mode's
///   <see cref="SpendRestriction"/> is not yet enforced by the payment
///   resolver (shared deferral with Delighted Halfling / Cavern of Souls).
/// - <b>Colour-identity activation snapshot</b>: the third mode reads "any
///   color among legendary permanents you control" at activation time
///   (CR 106.6). The land's source must be on the battlefield with a live
///   <see cref="ZoneServiceRegistry"/>/owner battlefield to enumerate — the
///   gate walks the controller's battlefield zone.
/// - <b>Target-selection legendary predicate</b>: the protection ability's
///   <see cref="TargetRequest.Description"/> conveys "target legendary
///   creature" but the engine's structural target filtering does not yet
///   enforce the legendary/creature predicate from the description string.
///   The resolve body double-checks at resolution (CR 608.2b) and skips the
///   grants when the chosen target is not a legendary creature on the
///   battlefield with a live effects service — same posture as
///   <see cref="TamiyosSafekeepingFactory"/>.
/// </summary>
[CardName("Plaza of Heroes")]
public static class PlazaOfHeroesFactory
{
    public const string CardName = "Plaza of Heroes";
    public const string Slug = "plaza-of-heroes";

    /// <summary>Protection ability mana cost — CR 602.</summary>
    public const string ProtectionCost = "{3}";

    /// <summary>Granted keyword — CR 702.11 Hexproof.</summary>
    public const string GrantedHexproof = "Hexproof";

    /// <summary>Granted keyword — CR 702.12 Indestructible.</summary>
    public const string GrantedIndestructible = "Indestructible";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    // CR 106.4 — "Spend this mana only to cast a legendary spell." Shared
    // static restriction so every "any colour" ManaAbility stamps the same
    // by-reference predicate (SpendRestriction equality is delegate-by-ref;
    // reusing one instance keeps the five abilities structurally equal). Same
    // predicate shape as Delighted Halfling.
    private static readonly SpendRestriction LegendaryOnly =
        new("legendary spell",
            spell => spell.Card.HasSupertype(CardSupertype.Legendary));

    private static readonly (string Pip, ManaColor Color)[] Wubrg =
    {
        ("W", ManaColor.White),
        ("U", ManaColor.Blue),
        ("B", ManaColor.Black),
        ("R", ManaColor.Red),
        ("G", ManaColor.Green),
    };

    /// <summary>
    /// Construct Plaza of Heroes as a plain Land with all four ability shapes
    /// attached. The protection ability's resolution registers the
    /// until-end-of-turn hexproof + indestructible grants against the targeted
    /// legendary creature's own layer service and self-exiles the land.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}  (CR 605.1 — mana ability, no stack; unrestricted)
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {T}: Add one mana of any color. Spend this mana only to cast a
        // legendary spell.  (Five WUBRG ManaAbility instances, each carrying
        // the shared LegendaryOnly SpendRestriction — CR 106.4. Same shape as
        // Delighted Halfling; payment-gate enforcement deferred.)
        // ----------------------------------------------------------------
        foreach (var (pip, _) in Wubrg)
        {
            land.AddAbility(new ManaAbility(
                land, owner, ManaCost.Parse(pip),
                canActivateCheck: null,
                spendRestriction: LegendaryOnly));
        }

        // ----------------------------------------------------------------
        // {T}: Add one mana of any color among legendary permanents you
        // control.  (CR 106.6 — five WUBRG ManaAbility instances, each gated
        // by a canActivateCheck requiring that colour to appear among the
        // controller's legendary permanents at activation time. Unrestricted
        // mana — no SpendRestriction.)
        // ----------------------------------------------------------------
        foreach (var (pip, color) in Wubrg)
        {
            var gatedColor = color; // capture per-iteration
            // The custom canActivateCheck REPLACES the default !IsTapped gate
            // (ManaAbility.CanActivate), so it must also assert the land is
            // untapped — CR 302.6 / 605.3a (the {T} cost can't be paid by a
            // tapped permanent).
            land.AddAbility(new ManaAbility(
                land, owner, ManaCost.Parse(pip),
                canActivateCheck: () =>
                    !land.IsTapped
                    && ColorAmongLegendaryPermanents(owner, gatedColor),
                spendRestriction: null));
        }

        // ----------------------------------------------------------------
        // {3}, {T}, Exile this land: Target legendary creature gains hexproof
        // and indestructible until end of turn.
        //
        // CR 602 — ordinary activated ability (uses the stack); NOT a mana
        // ability. Cost: {3} mana + {T} + self-exile. The self-exile zone
        // move is performed in the effect closure (AdditionalCost has no
        // exile primitive) — Sacrifice is the placeholder cost, mirroring
        // Sentinel Totem. On resolve the targeted legendary creature gains
        // Hexproof (CR 702.11b) + Indestructible (CR 702.12b) until cleanup
        // (CR 514.2 / CR 613 Layer 6).
        // ----------------------------------------------------------------
        ActivatedAbility? protection = null;

        var protectionEffect = new Effect(
            $"{CardName}: target legendary creature gains hexproof and indestructible until end of turn (exile this land)",
            () =>
            {
                // Self-exile (cost) — idempotent if already moved.
                ExileSelf(land, owner);

                var target = ResolveTargetCreature(protection);
                if (target is null) return; // no target chosen / illegal → no-op
                GrantKeywords(target);
            });

        protection = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ProtectionCost),
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land), // models the self-exile cost; zone move in effect closure
            },
            effects: new IEffect[] { protectionEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    "target legendary creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(protection);

        return land;
    }

    /// <summary>
    /// CR 106.6 — does <paramref name="color"/> appear among the colours
    /// (CR 105 / CR 202.2) of the legendary permanents
    /// <paramref name="controller"/> controls? Walks the controller's
    /// battlefield zone for legendary permanents and unions their colour sets.
    /// </summary>
    private static bool ColorAmongLegendaryPermanents(Player controller, ManaColor color)
    {
        foreach (var permanent in controller.Zones.Battlefield.GetCards().OfType<Permanent>())
        {
            if (!permanent.HasSupertype(CardSupertype.Legendary)) continue;
            if (!ReferenceEquals(permanent.Controller, controller)) continue;
            if (CardColors.GetColors(permanent).Contains(color)) return true;
        }
        return false;
    }

    /// <summary>
    /// Self-exile (the "Exile this land" portion of the cost). Moves the land
    /// from Battlefield → Exile. Idempotent if already exiled.
    /// </summary>
    private static void ExileSelf(Land land, Player owner)
    {
        if (land.Zone != ZoneType.Battlefield) return;

        var holder = land.Controller ?? owner;
        var ownerOfLand = land.Owner ?? owner;
        holder.Zones.Battlefield.RemoveCard(land);
        ownerOfLand.Zones.Exile.AddCard(land);
        land.SetZone(ZoneType.Exile);
    }

    /// <summary>
    /// Resolve the chosen "target legendary creature" from the ability's
    /// <see cref="ActivatedAbility.ChosenTargets"/>. Returns <c>null</c> when
    /// no target was chosen or the target is no longer a legendary creature on
    /// the battlefield with a live effects service (CR 608.2b illegal-target
    /// guard).
    /// </summary>
    private static Creature? ResolveTargetCreature(ActivatedAbility? ability)
    {
        if (ability is null
            || ability.ChosenTargets.Count == 0
            || ability.ChosenTargets[0].Count == 0)
        {
            return null;
        }

        if (ability.ChosenTargets[0][0] is not Creature creature) return null;
        if (creature.Zone != ZoneType.Battlefield) return null;
        if (!creature.HasSupertype(CardSupertype.Legendary)) return null;
        return creature;
    }

    private static void GrantKeywords(Creature creature)
    {
        if (creature.ActiveEffects == null) return;

        // CR 702.11b — Hexproof. Layer-6 keyword grant, EOT expiry (CR 514.2).
        creature.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(creature, GrantedHexproof));

        // CR 702.12b — Indestructible. Layer-6 keyword grant, EOT expiry.
        creature.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(creature, GrantedIndestructible));
    }
}
