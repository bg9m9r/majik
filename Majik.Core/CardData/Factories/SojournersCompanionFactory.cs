using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sojourner's Companion (Modern Horizons 2, {6}).
///
/// Artifact Creature — Thopter Knight 4/4. Oracle text:
///   "Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)
///    {2}, {T}, Sacrifice Sojourner's Companion: Search your library for a
///    basic land card, put it onto the battlefield tapped, then shuffle."
///
/// ## Implemented (v1)
/// - 4/4 Artifact Creature — Thopter Knight with printed mana cost {6}.
///   <see cref="Card.AddCardType"/> additively flags the Artifact type so
///   <c>HasType(Artifact)</c> + <c>HasType(Creature)</c> both pass — same
///   shape as <see cref="FrogmiteFactory"/> / <see cref="MyrEnforcerFactory"/>.
/// - <b>Affinity for artifacts (CR 702.40 / CR 117.7)</b>: wired via
///   <see cref="CostReductionAbility.AffinityFor"/>(<see cref="CardType.Artifact"/>).
///   At six artifacts the generic floors to {0} (CR 117.7c) — same dream
///   as Myr Enforcer one mana cheaper, gated on the {T}+sac tutor body
///   instead of vanilla beats. A <see cref="KeywordAbility"/> "Affinity"
///   marker is attached for keyword-scan callers.
/// - <b>{2}, {T}, Sacrifice ~: tutor a basic land -> battlefield tapped</b>
///   — single <see cref="ActivatedAbility"/> with three costs:
///   <see cref="ManaCostCost"/>("{2}") + <see cref="AdditionalCost.Tap"/>
///   on the Companion + <see cref="AdditionalCost.Sacrifice"/> on the
///   Companion itself. Resolution:
///   <list type="number">
///     <item><description>Sacrifice the Companion (battlefield ->
///       owner's graveyard) — mirrors <see cref="ExpeditionMapFactory"/>'s
///       <c>SacrificeSelf</c> closure since the engine's generic
///       <see cref="AdditionalCost.Sacrifice"/> payment is currently a
///       no-op stub.</description></item>
///     <item><description>Consult the controller's
///       <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> for the basic
///       land choice (CR 701.19a; deterministic first-basic fallback when
///       no agent registered). Basic-land predicate matches
///       <see cref="SpellTemplates.Templates.Search.SearchSpellFactory"/>'s
///       basic-land names per CR 305.6.</description></item>
///     <item><description>Move the pick Library -> Battlefield, then tap
///       it (printed-"tapped" rider — same posture as
///       <see cref="RampantGrowthFactory"/>).</description></item>
///     <item><description>Shuffle via <see cref="LibraryShuffle.ShuffleLibrary"/>
///       (CR 701.20a — publishes <c>LibraryShuffledEvent</c> when a bus
///       is registered).</description></item>
///   </list>
///   Decline-to-find is legal: agent returning null = clean no-op past
///   the sac+shuffle (CR 701.19a). Empty basic-land pile = clean no-op
///   past the sac+shuffle as well.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal-event emission</b>: the tutored basic moves Library ->
///   Battlefield without publishing a reveal event. Same gap as every
///   other tutor factory (Expedition Map, Stoneforge Mystic, Sylvan
///   Scrying, Rampant Growth).
/// - <b>Sacrifice payment side effects</b>: see <see cref="ExpeditionMapFactory"/>
///   — the engine's generic <see cref="AdditionalCost"/> sacrifice
///   payment is a no-op stub. The effect closure performs the
///   move-to-graveyard directly so behaviour is observable. Remove the
///   explicit move-to-graveyard once <see cref="AdditionalCost.Pay"/>
///   performs the sacrifice itself.
/// - <b>ETB-tapped replacements</b>: when a
///   <see cref="ZoneServiceRegistry"/> entry is wired the closure delegates
///   the move so ETB-tapped replacements + ETB triggers fire on the
///   tutored basic; the shape-test path falls back to raw zone mutation
///   + post-move <c>Tap()</c>. Mirrors
///   <see cref="SpellTemplates.Templates.Search.SearchSpellFactory.SearchLandToBattlefieldSpell"/>.
/// </summary>
[CardName("Sojourner's Companion")]
public static class SojournersCompanionFactory
{
    public const string CardName = "Sojourner's Companion";
    public const string PrintedManaCost = "{6}";
    public const string TutorActivationCost = "{2}";
    public const int Power = 4;
    public const int Toughness = 4;

    // Basic land names per CR 305.6 — local copy keeps the factory free
    // of an internal-template dependency (SearchSpellFactory exposes its
    // BasicLandNames as `private`).
    private static readonly HashSet<string> BasicLandNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" };

    /// <summary>
    /// Construct Sojourner's Companion owned and controlled by
    /// <paramref name="owner"/>. Wires the Affinity-for-artifacts cost
    /// reducer + keyword marker + the {2}, {T}, Sac tutor activated
    /// ability.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Thopter, CardSubtype.Knight });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType lookups + colour identity see both
        // types (mirrors Arcbound Ravager / Walking Ballista /
        // Frogmite / Myr Enforcer).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // Affinity for artifacts (CR 702.40 / CR 117.7).
        card.AddAbility(CostReductionAbility.AffinityFor(CardType.Artifact));
        card.AddAbility(new KeywordAbility("Affinity", card, owner));

        // ----------------------------------------------------------------
        // {2}, {T}, Sacrifice Sojourner's Companion: Search your library
        // for a basic land card, put it onto the battlefield tapped, then
        // shuffle. CR 602 — activated ability with three costs.
        // CR 701.19a — search consults the agent (null = decline; legal).
        // CR 701.20a — shuffle after the search via LibraryShuffle.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: tutor a basic land -> battlefield tapped + sac self",
            async ctx =>
            {
                SacrificeSelf(card, owner);

                var candidates = owner.Zones.Library.GetCards()
                    .Where(c => c.HasType(CardType.Land) && BasicLandNames.Contains(c.Name))
                    .ToList();

                if (candidates.Count == 0)
                {
                    // CR 701.19a — no candidate is a legal outcome; still
                    // shuffle per CR 701.20a since the search occurred.
                    LibraryShuffle.ShuffleLibrary(owner, "sojourners-companion");
                    return;
                }

                var agent = ctx.Agent ?? AgentRegistry.Get(owner);
                ICard? pick = agent != null
                    ? (await agent.ChooseLibraryPickAsync( ctx: ctx.Game,
                            candidates: candidates,
                            kindLabel: "basic land card").ConfigureAwait(false))
                    : candidates[0];

                if (pick != null)
                {
                    // Route through ZoneService when wired (so ETB-tapped
                    // replacements + ETB triggers fire on the tutored
                    // basic). Fall back to raw zone mutation + post-move
                    // Tap() on the shape-test path. Mirrors
                    // SearchSpellFactory.SearchLandToBattlefieldSpell.
                    var zones = ZoneServiceRegistry.Get(owner);
                    if (zones != null)
                    {
                        zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, owner);
                        if (pick is Permanent permRouted && !permRouted.IsTapped)
                            permRouted.Tap();
                    }
                    else
                    {
                        owner.Zones.Library.RemoveCard(pick);
                        owner.Zones.Battlefield.AddCard(pick);
                        pick.SetZone(ZoneType.Battlefield);
                        if (pick is Permanent perm) perm.Tap();
                    }
                }

                // CR 701.20a — shuffle after the search resolves.
                LibraryShuffle.ShuffleLibrary(owner, "sojourners-companion");
            });

        var tutorAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(TutorActivationCost),
                AdditionalCost.Tap(card),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { tutorEffect });

        card.AddAbility(tutorAbility);

        return card;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="card"/> from the battlefield to its
    /// owner's graveyard. Idempotent. Mirrors
    /// <see cref="ExpeditionMapFactory"/>'s SacrificeSelf shape (the
    /// engine's generic <see cref="AdditionalCost"/> sacrifice payment is
    /// currently a no-op stub).
    /// </summary>
    private static void SacrificeSelf(Creature card, Player owner)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
