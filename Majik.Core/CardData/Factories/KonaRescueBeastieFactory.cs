using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kona, Rescue Beastie (Duskmourn: House of Horror,
/// {3}{G}).
///
/// Legendary Creature — Beast Survivor 4/3. Oracle text (verified against
/// Scryfall 2026-06-24):
///   "Survival — At the beginning of your second main phase, if Kona is tapped,
///    you may put a permanent card from your hand onto the battlefield."
///
/// The card's base shape (name, Legendary Creature, Beast + Survivor subtypes,
/// {3}{G}, 4/3) is materialised from the embedded JSON definition
/// (<c>kona-rescue-beastie.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Survival trigger is layered on
/// here because the JSON <c>AbilityDefinition</c> schema expresses neither a
/// second-main-phase intervening-if Survival trigger nor a "put a permanent card
/// from your hand onto the battlefield" effect.
///
/// ## Implemented (v1)
/// - 4/3 Legendary Creature — Beast Survivor at printed cost {3}{G}, owner /
///   controller wired.
/// - <b>Survival — second-main-phase put-permanent-from-hand (CR 603.1 / 603.4 /
///   115.2)</b> — a <see cref="TriggeredAbility"/> on
///   <see cref="Triggers.OnStepBegin"/> with
///   <see cref="StepStateType.PostCombatMain"/> (the controller's own second /
///   post-combat main phase). "Survival" is reminder-text flavour for the
///   CR 603.4 <b>intervening-if</b> "if Kona is tapped": the trigger's
///   <see cref="TriggeredAbility.InterveningIf"/> re-checks
///   <see cref="Permanent.IsTapped"/> both when it would be put on the stack AND
///   at resolution, so a Kona untapped in response does nothing. On resolution
///   (and only when the controller's agent takes the "you may"): pick a permanent
///   card (CR 110.4a — artifact / creature / enchantment / land / planeswalker)
///   from the controller's hand and move it hand → battlefield. The move routes
///   through <see cref="ZoneService.MoveCardAsync"/> when supplied so ETB
///   triggers / replacements on the put permanent fire (CR 603.6a / CR 614).
///   Same put-from-hand shape as <see cref="SakuraTribeScoutFactory"/> /
///   <see cref="CultivatorColossusFactory"/>, but the candidate filter is any
///   permanent card rather than only lands.
///
/// ## Single-arg dispatcher path
/// The <see cref="Create(Player)"/> overload attaches the Survival trigger
/// structurally (correct card shape for factory-shape / dispatch tests); the
/// trigger is NOT registered with a <see cref="TriggerManager"/> and no
/// <see cref="ZoneService"/> is wired. Production callers use the full overload.
/// </summary>
[CardName("Kona, Rescue Beastie")]
public static class KonaRescueBeastieFactory
{
    public const string CardName = "Kona, Rescue Beastie";
    public const string Slug = "kona-rescue-beastie";
    public const int Power = 4;
    public const int Toughness = 3;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Kona with no live wiring. The Survival trigger attaches
    /// structurally; it is NOT enrolled with a <see cref="TriggerManager"/> and
    /// no <see cref="ZoneService"/> is threaded. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct a fully-wired Kona, Rescue Beastie.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager for the Survival trigger. May be
    /// null — the trigger attaches structurally but isn't enrolled.</param>
    /// <param name="zoneService">Threaded into the hand → battlefield move so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> fires and any ETB
    /// triggers / replacements on the put permanent resolve (CR 603.6a /
    /// CR 614). May be null (raw-zone move fallback).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary Creature,
        // Beast + Survivor subtypes, {3}{G}, 4/3). The JSON carries no abilities —
        // the Survival trigger is layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Survival — second-main-phase put-permanent-from-hand. CR 603.1
        // (turn-based trigger) / CR 603.4 (intervening-if) / CR 115.2 (put onto
        // the battlefield without being cast) / CR 110.4a (permanent card).
        //   "Survival — At the beginning of your second main phase, if Kona is
        //    tapped, you may put a permanent card from your hand onto the
        //    battlefield."
        // "Survival" is reminder-text flavour for the intervening-if. The second
        // main phase is StepStateType.PostCombatMain.
        // ----------------------------------------------------------------
        var survivalEffect = new Effect(
            $"{CardName}: Survival — may put a permanent card from your hand onto the battlefield",
            ctx => ResolveSurvivalAsync(card, owner, zoneService, ctx));

        var survivalTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.PostCombatMain),
            effects: new IEffect[] { survivalEffect },
            // CR 603.4 — intervening-if "if Kona is tapped", re-checked both when
            // the trigger would be put on the stack (CanBePutOnStack) and on
            // resolution. A Kona untapped in response does nothing.
            interveningIf: () => card.Zone == ZoneType.Battlefield && card.IsTapped,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(survivalTrigger);
        triggers?.RegisterTriggeredAbility(survivalTrigger);

        return card;
    }

    /// <summary>
    /// CR 603.5 — the optional Survival instruction at resolution. Re-checks the
    /// intervening-if (CR 603.4) defensively, prompts the controller's agent for
    /// the "you may", and on a yes picks a permanent card (CR 110.4a) from the
    /// controller's hand and moves it hand → battlefield (CR 115.2). A decline /
    /// no eligible permanent card / illegal pick moves nothing. Public so tests /
    /// bots can drive resolution directly.
    /// </summary>
    public static async ValueTask ResolveSurvivalAsync(
        Creature card, Player owner, ZoneService? zoneService, ResolutionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(owner);

        // CR 603.4 — the intervening-if is re-checked on resolution too.
        if (card.Zone != ZoneType.Battlefield || !card.IsTapped) return;

        var controller = card.Controller ?? owner;

        // Candidate set: every permanent card (CR 110.4a) in the controller's
        // hand. Instants / sorceries are not permanent cards.
        var candidates = controller.Zones.Hand.GetCards()
            .Where(IsPermanentCard)
            .ToList();
        if (candidates.Count == 0) return; // No permanent card → "may" no-op.

        var agent = ctx.Agent ?? AgentRegistry.Get(controller);

        // "You may" — CR 117.1a optional gesture. Smart agent path via
        // ChooseYesNoAsync(BotIntent.CheatIntoPlay); no-agent fallback auto-accepts
        // (matches the v1 posture every "may" put-from-hand factory uses —
        // Sakura-Tribe Scout / Cultivator Colossus / Stoneforge Mystic).
        if (agent != null)
        {
            var optIn = await agent.ChooseYesNoAsync(
                    "Survival — put a permanent card from your hand onto the battlefield?",
                    BotIntent.CheatIntoPlay, ctx.Ct).ConfigureAwait(false);
            if (!optIn) return;
        }

        // Pick which permanent card. Agent-driven via ChooseFromHandAsync with
        // candidates pre-filtered to permanent cards; no-agent fallback takes the
        // first deterministically (mirrors Sakura-Tribe Scout).
        ICard? pick;
        if (agent != null)
        {
            pick = await agent.ChooseFromHandAsync(controller, candidates, BotIntent.CheatIntoPlay)
                .ConfigureAwait(false);
            // CR 608.2b — re-validate the agent's pick at resolution.
            if (pick == null || !candidates.Contains(pick)) return;
        }
        else
        {
            pick = candidates[0];
        }

        // Hand → battlefield. Prefer ZoneService so ETB triggers + replacements
        // on the put permanent fire (CR 603.6a / CR 614 — async so a prompting
        // ETB replacement, e.g. a shock land, awaits the controller's agent off
        // the ResolutionContext). Raw zone manipulation fallback for shape tests.
        if (zoneService != null)
        {
            await zoneService.MoveCardAsync(
                pick, ZoneType.Hand, ZoneType.Battlefield, ctx, controller)
                .ConfigureAwait(false);
        }
        else
        {
            controller.Zones.Hand.RemoveCard(pick);
            controller.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(controller);
        }
    }

    /// <summary>
    /// CR 110.4a — a permanent card is an artifact, battle, creature,
    /// enchantment, land, or planeswalker card. Instants and sorceries are not
    /// permanent cards. (The engine's <see cref="CardType"/> enum doesn't model
    /// Battle, so the predicate covers the five permanent types it does.)
    /// </summary>
    public static bool IsPermanentCard(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.HasType(CardType.Artifact)
            || card.HasType(CardType.Creature)
            || card.HasType(CardType.Enchantment)
            || card.HasType(CardType.Land)
            || card.HasType(CardType.Planeswalker);
    }
}
