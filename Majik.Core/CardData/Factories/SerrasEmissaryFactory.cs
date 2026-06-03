using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Serra's Emissary (Dominaria United Commander,
/// {4}{W}{W}{W}, 7/7 Angel).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "Flying"
///   "As this creature enters, choose a card type."
///   "You and creatures you control have protection from the chosen card
///    type."
///
/// ## Implemented (v1 — full)
/// - <b>Creature shape</b> 7/7 Angel, {4}{W}{W}{W}, with Flying.
/// - <b>"As this creature enters, choose a card type" (CR 614.12 /
///   702.16)</b> — an ETB <see cref="TriggeredAbility"/>; on resolution the
///   controller's agent picks one card type (via the declarative
///   <see cref="IPlayerAgent.ChooseAsync"/> PickOne sink). With no agent the
///   v1 default is <see cref="CardType.Creature"/> (the most relevant
///   protection in practice).
/// - <b>Player half — "You ... have protection from the chosen card type"
///   (CR 702.16)</b> — registers the chosen type into
///   <see cref="Majik.Core.Rules.PlayerStaticAbilities"/> (player-target
///   legality, read by <see cref="ActionValidator"/> /
///   <see cref="Majik.Core.Targeting.TargetLegality"/>) AND a
///   <see cref="PreventDamageToPlayerFromCardTypeShield"/> on the controller's
///   <see cref="ReplacementBus"/> (the damage half, CR 702.16e). Both self-gate
///   on the Emissary's zone / current controller.
/// - <b>Creatures half — "...and creatures you control have protection from
///   the chosen card type"</b> — a Layer-6
///   <see cref="GrantAbilityToGroupStaticEffect"/> granting a
///   <see cref="ProtectionAbility"/> (the chosen type's plural quality) to
///   every creature the controller controls, with live membership; the
///   existing creature-side protection seams
///   (<see cref="Majik.Core.Rules.Protection.HasProtectionFromCardType"/> in
///   combat / targeting) read it. Wired via
///   <see cref="GrantAbilityToGroupLifecycle"/> so it registers / revokes with
///   the Emissary's battlefield presence.
///
/// ## v1 gaps
/// - <b>The card-type choice is locked at ETB</b> and not re-promptable; no
///   "you may change it" surface exists (none printed). When rebuilt without a
///   <see cref="ContinuousEffectsService"/> / <see cref="ReplacementBus"/>
///   (shape paths) the protection clauses are skipped.
/// </summary>
[CardName("Serra's Emissary")]
public static class SerrasEmissaryFactory
{
    public const string CardName = "Serra's Emissary";
    public const string PrintedManaCost = "{4}{W}{W}{W}";
    public const int Power = 7;
    public const int Toughness = 7;

    /// <summary>The card types choosable at ETB (CR 702.16 / 205.2).</summary>
    public static readonly CardType[] ChoosableTypes =
    {
        CardType.Artifact, CardType.Creature, CardType.Enchantment,
        CardType.Instant, CardType.Land, CardType.Planeswalker, CardType.Sorcery,
    };

    /// <summary>Shape-only build (no live registries).</summary>
    public static Creature Create(Player owner) =>
        Create(owner, continuousEffects: null, replacements: null, triggers: null, eventBus: null);

    /// <summary>
    /// Fully wired build. <paramref name="continuousEffects"/> backs the
    /// creatures-you-control group grant; <paramref name="replacements"/> the
    /// player damage shield; <paramref name="triggers"/> the ETB choose-card-
    /// type trigger; <paramref name="eventBus"/> the group-grant + player-static
    /// LTB teardown. Any may be null (the clause is skipped).
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        ReplacementBus? replacements,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            CardName, PrintedManaCost, Power, Toughness,
            supertypes: null, subtypes: new[] { CardSubtype.Angel });
        card.SetOwner(owner);
        card.SetController(owner);
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // The chosen card type is captured at ETB resolution; the protection
        // wiring (player + creatures) keys off this single token.
        var token = new object();

        var etbEffect = new Effect(
            $"{CardName}: choose a card type; you and creatures you control gain protection from it",
            ctx => ChooseAndGrantAsync(card, token, ctx, continuousEffects, replacements, eventBus));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // Tear the player-level grants down when the Emissary leaves play
        // (the creature-side group grant self-revokes via its own lifecycle).
        if (eventBus != null)
        {
            void OnMoved(CardMovedEvent e)
            {
                if (!ReferenceEquals(e.Card, card)) return;
                if (card.Zone != ZoneType.Battlefield)
                {
                    PlayerStaticAbilities.RemoveProtectionFromCardType(token);
                }
            }
            eventBus.Subscribe<CardMovedEvent>(OnMoved);
        }

        return card;
    }

    private static async System.Threading.Tasks.ValueTask ChooseAndGrantAsync(
        Creature card,
        object token,
        ResolutionContext ctx,
        ContinuousEffectsService? continuousEffects,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        var controller = card.Controller;
        if (controller == null) return;

        var chosen = await ChooseTypeAsync(controller, ctx).ConfigureAwait(false);
        Grant(card, token, chosen, continuousEffects, replacements, eventBus);
    }

    /// <summary>CR 702.16 — apply both halves of Serra's Emissary's protection
    /// for the chosen <paramref name="type"/>. Exposed for tests / bots without
    /// driving the ETB choice flow.</summary>
    public static void Grant(
        Creature card,
        object token,
        CardType type,
        ContinuousEffectsService? continuousEffects,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        var controller = card.Controller;
        if (controller == null) return;

        // ── Player half ──────────────────────────────────────────────────
        PlayerStaticAbilities.AddProtectionFromCardType(token, controller, type);
        if (replacements != null)
        {
            replacements.Register<DamageIntent>(
                new PreventDamageToPlayerFromCardTypeShield(card, type));
        }

        // ── Creatures-you-control half (CR 613.1f, Layer 6) ──────────────
        if (continuousEffects != null)
        {
            var quality = PluralQuality(type);
            var lifecycle = new GrantAbilityToGroupLifecycle(
                card,
                continuousEffects,
                eventBus,
                scope: p => p is Creature && ReferenceEquals(p.Controller, card.Controller),
                abilityFactory: _ => new IAbility[] { new ProtectionAbility(quality) },
                membershipProvider: () => ControllerBattlefieldCreatures(card));
            lifecycle.Attach();
        }
    }

    private static async System.Threading.Tasks.ValueTask<CardType> ChooseTypeAsync(
        Player controller, ResolutionContext ctx)
    {
        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        if (agent == null || ctx.Game == null) return CardType.Creature;

        var req = new ChoiceRequest(
            ChoiceKind.PickOne,
            "Serra's Emissary — choose a card type",
            Min: 1, Max: 1,
            Candidates: ChoosableTypes.Cast<object>().ToList());

        var picked = await agent.ChooseAsync(ctx.Game, req, ctx.Ct).ConfigureAwait(false);
        if (picked != null && picked.Count > 0 && picked[0] is CardType t) return t;
        return CardType.Creature;
    }

    private static IEnumerable<Permanent> ControllerBattlefieldCreatures(Creature emissary)
    {
        var controller = emissary.Controller;
        if (controller == null) yield break;
        foreach (var c in controller.Zones.Battlefield.GetCards().OfType<Permanent>())
        {
            yield return c;
        }
    }

    /// <summary>The plural protection-quality string the
    /// <see cref="Majik.Core.Rules.Protection"/> helpers match for a card type
    /// (CR 702.16 — "protection from creatures" / "from instants" / …).</summary>
    public static string PluralQuality(CardType type) => type switch
    {
        CardType.Creature => "creatures",
        CardType.Artifact => "artifacts",
        CardType.Enchantment => "enchantments",
        CardType.Land => "lands",
        CardType.Planeswalker => "planeswalkers",
        CardType.Instant => "instants",
        CardType.Sorcery => "sorceries",
        _ => type.ToString().ToLowerInvariant() + "s",
    };
}
