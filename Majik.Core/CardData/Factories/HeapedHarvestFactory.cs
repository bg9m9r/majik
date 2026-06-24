using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Heaped Harvest (Bloomburrow, {2}{G}).
///
/// Artifact — Food. Oracle text (verified against Scryfall 2026-06-24):
///   "When this artifact enters and when you sacrifice it, you may search your
///    library for a basic land card, put it onto the battlefield tapped, then
///    shuffle.
///    {2}, {T}, Sacrifice this artifact: You gain 3 life."
///
/// ## Shape source
/// Card identity (name, {2}{G}, Artifact — Food) AND the standard Food
/// sacrifice ability ("{2}, {T}, Sacrifice this artifact: You gain 3 life.")
/// are materialised from the embedded JSON definition
/// (<c>Majik.Core/CardData/Cards/heaped-harvest.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON
/// <see cref="ActivatedAbilityDefinition"/> schema already expresses the
/// <c>{2}</c> mana + <c>{T}</c> + sacrifice-self costs and the
/// <c>gain_life_self</c> effect, so that ability needs no hand-rolled C# —
/// same posture as <see cref="LembasFactory"/> / <see cref="GingerbruteFactory"/>.
///
/// ## Implemented (v1)
/// The "When this artifact enters AND when you sacrifice it" clause is one
/// printed sentence with TWO trigger events feeding ONE effect (CR 603.6a +
/// CR 701.16). It is materialised as two <see cref="TriggeredAbility"/>
/// instances sharing the same tutor body — the engine has no single-ability
/// "fires on either event" surface, and two triggers is the faithful model
/// (each event independently puts a copy of the ability on the stack):
/// <list type="bullet">
///   <item><b>ETB trigger (CR 603.6a)</b>: fires on the enters-the-battlefield
///   self <see cref="CardMovedEvent"/> via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>.</item>
///   <item><b>Reflexive self-sacrifice trigger (CR 701.16)</b>: "when you
///   sacrifice it" fires on the dedicated
///   <see cref="PermanentSacrificedEvent"/> when the sacrificed permanent is
///   THIS artifact. Built off the existing sacrifice-detection surface (the
///   same event the aristocrat triggers consume) — no new engine mechanic.
///   The bus-aware <see cref="Majik.Core.Costs.SacrificeSelfCost"/> (the JSON
///   <c>sacrifice_self</c> cost on the {2}{T}-Sac-gain-3-life ability)
///   publishes this event when paid through the bus seam, so activating the
///   Food sacrifice ability ALSO fires the tutor — exactly as the card
///   reads.</item>
/// </list>
/// Both triggers share <see cref="TutorOneBasicToBattlefieldTappedAsync"/>:
/// search the library for ONE basic land (CR 305.6 — Basic supertype + Land
/// card type), consult the registered <see cref="IPlayerAgent"/> via
/// <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> (CR 701.19a — agent may
/// decline; "you may" + a search that fails to find are both legal), move the
/// pick Library → Battlefield through <see cref="ZoneServiceRegistry"/> so
/// ETB-tapped replacements + <c>CardMovedEvent</c> subscribers fire, apply the
/// printed "tapped" rider after the move (CR 701.18), then shuffle ONCE via
/// <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a). Deterministic
/// first-basic fallback when no agent is registered — same posture as
/// <see cref="SolemnSimulacrumFactory"/> / <see cref="BorderlandRangerFactory"/>.
///
/// ## Deferred (v1)
/// - "You may" decisions auto-accept in v1 (the search consults the agent,
///   which may decline) — consistent with the rest of the tutor factory family.
/// - Tutored basic moves Library → Battlefield without a reveal event — same
///   gap as every tutor factory (<see cref="SolemnSimulacrumFactory"/>,
///   <see cref="BorderlandRangerFactory"/>).
/// </summary>
[CardName("Heaped Harvest")]
public static class HeapedHarvestFactory
{
    public const string CardName = "Heaped Harvest";
    public const string Slug = "heaped-harvest";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Heaped Harvest with no live <see cref="TriggerManager"/>
    /// wiring. Both tutor triggers are attached to the card for shape
    /// inspection but not registered. Suitable for dispatcher / structural
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Heaped Harvest with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, both tutor
    /// triggers are registered so the relevant event places the ability on the
    /// stack automatically (CR 603.3).
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (Artifact — Food, {2}{G}) AND the
        // "{2}, {T}, Sacrifice this artifact: You gain 3 life." activated
        // ability are materialised from the embedded JSON definition.
        var card = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // The shared tutor effect — both triggers below execute this body.
        Effect MakeTutorEffect(string label) => new Effect(
            $"{CardName}: {label} — search a basic land -> battlefield tapped, then shuffle",
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                await TutorOneBasicToBattlefieldTappedAsync(controller, ctx).ConfigureAwait(false);
            });

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this artifact enters … search your library for a basic land
        //    card, put it onto the battlefield tapped, then shuffle."
        // ----------------------------------------------------------------
        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { MakeTutorEffect("when this artifact enters") },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Reflexive self-sacrifice triggered ability — CR 701.16.
        //   "… and when you sacrifice it, search your library for a basic land
        //    card, put it onto the battlefield tapped, then shuffle."
        // Fires on the dedicated PermanentSacrificedEvent when THIS artifact is
        // the sacrificed permanent. The bus-aware SacrificeSelfCost (the JSON
        // sacrifice_self cost on the Food ability) publishes this event when
        // paid through the bus seam, so the {2}{T}-Sac ability ALSO tutors.
        // activeZones includes Graveyard because the sacrificed artifact is
        // already in the graveyard by the time the event publishes (CR 701.16a;
        // ZoneService stamps the zone before publish — Lembas / Aven Fisher
        // posture).
        // ----------------------------------------------------------------
        var sacTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<PermanentSacrificedEvent>(
                (e, _) => ReferenceEquals(e.SacrificedCard, card)),
            effects: new IEffect[] { MakeTutorEffect("when you sacrifice it") },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(sacTrigger);
        triggers?.RegisterTriggeredAbility(sacTrigger);

        return card;
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for ONE basic land card
    /// (CR 305.6 — Basic supertype + Land card type), consult the agent (which
    /// may decline; deterministic first-basic fallback when no agent), move the
    /// pick to the battlefield with the printed "tapped" rider applied after the
    /// move (CR 701.18), then shuffle once (CR 701.20a). Crib of
    /// <see cref="SolemnSimulacrumFactory"/>'s tutor body.
    /// </summary>
    private static async ValueTask TutorOneBasicToBattlefieldTappedAsync(Player player, ResolutionContext ctx)
    {
        bool IsBasicLand(ICard c) =>
            c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic);

        var agent = ctx.Agent ?? AgentRegistry.Get(player);

        var candidates = player.Zones.Library.GetCards().Where(IsBasicLand).ToList();
        ICard? pick = null;
        if (candidates.Count > 0)
        {
            pick = agent != null
                ? await agent.ChooseLibraryPickAsync(ctx.Game, candidates,
                        "basic land card to put onto the battlefield tapped")
                    .ConfigureAwait(false)
                : candidates[0];
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, player);
                if (pick is Permanent perm && !perm.IsTapped) perm.Tap();
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(player);
                if (pick is Permanent perm) perm.Tap();
            }
        }

        // CR 701.20a — shuffle once after the search, even when zero cards were
        // found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, Slug);
    }
}
