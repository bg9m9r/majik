using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Disturbing Mirth (Duskmourn: House of Horror,
/// <c>{B}{R}</c>). Enchantment.
///
/// Oracle text (Scryfall-verified, embedded seed):
///   "When this enchantment enters, you may sacrifice another enchantment
///    or creature. If you do, draw two cards.
///    When you sacrifice this enchantment, manifest dread. (Look at the top
///    two cards of your library. Put one onto the battlefield face down as a
///    2/2 creature and the other into your graveyard. Turn it face up any
///    time for its mana cost if it's a creature card.)"
///
/// The base shape (name, Enchantment, <c>{B}{R}</c>) is materialised from the
/// embedded JSON definition (<c>disturbing-mirth.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two triggers — neither
/// expressible in the JSON <c>AbilityDefinition</c> schema (an optional
/// filtered-sacrifice-then-draw ETB, and a self-sacrifice manifest-dread
/// trigger) — are layered on here (same posture as
/// <see cref="TheGooseMotherFactory"/>'s ETB clause and
/// <see cref="AbhorrentOculusFactory"/>'s manifest-dread clause).
///
/// ## Implemented (v1)
///
/// - <b>ETB optional sacrifice → draw two (CR 603.6a / CR 117.5 / CR 120.2)</b>:
///   "When this enchantment enters, you may sacrifice another enchantment or
///   creature. If you do, draw two cards." Wired as a
///   <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnEnterBattlefieldSelf(ICard)"/>. On resolve the
///   "you may" (CR 117.5) is offered to the controller's
///   <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseYesNoAsync(string, BotIntent, System.Threading.CancellationToken)"/>;
///   with no agent registered the upside is auto-taken (sacrificing a
///   creature/enchantment to draw two is card advantage). The optional cost is
///   a <see cref="SacrificeFilteredCost"/> filtered to creatures-or-enchantments
///   with <c>excludeSelf</c> = this card (CR 109.2 — "<i>another</i>"). "If you
///   do" (CR 120.2) gates the draw on the sacrifice actually being payable +
///   paid — no other creature/enchantment (cost can't be paid) → no draw.
///
/// - <b>Self-sacrifice manifest dread (CR 603.6b / CR 701.59)</b>: "When you
///   sacrifice this enchantment, manifest dread." Wired as a
///   <see cref="TriggeredAbility"/> over a raw
///   <see cref="EventTriggerCondition{TEvent}"/> on
///   <see cref="PermanentSacrificedEvent"/> filtered to
///   <c>SacrificedCard == this card</c> (reference identity — the
///   self-sacrifice scope; Disturbing Mirth has no sacrifice ability of its
///   own, so this fires only when some <em>other</em> effect sacrifices it).
///   On resolve it runs real manifest dread via
///   <see cref="ManifestDreadEffect.Resolve(Player, ZoneService?)"/> for the
///   card's controller.
///
/// ## Deferred (v1 gaps — small, shared)
///
/// - <b>Agent pick of WHICH creature/enchantment to sacrifice:</b> the
///   <see cref="SacrificeFilteredCost"/> deterministically takes the first
///   eligible permanent when no <c>Target</c> is pre-stamped (the shared
///   sacrifice-prompt surface — same gap noted on Gilded Goose / The Goose
///   Mother). The live activation dispatch stamps the pick when more than one
///   qualifies.
/// - <b>Manifest-dread pick-one-of-two:</b> v1 deterministically manifests the
///   top-of-library card (the shared <see cref="ManifestDreadEffect"/>
///   deferral — same as <see cref="AbhorrentOculusFactory"/>).
///
/// CR rule references: 603.6a/603.6b (ETB + sacrifice triggers), 109.2
/// ("another"), 117.5 ("you may"), 120.2 ("if you do"), 701.16
/// (sacrifice / <see cref="PermanentSacrificedEvent"/>), 701.59 (manifest
/// dread).
/// </summary>
[CardName("Disturbing Mirth")]
public static class DisturbingMirthFactory
{
    public const string CardName = "Disturbing Mirth";
    public const string Slug = "disturbing-mirth";
    public const int DrawCount = 2;

    /// <summary>
    /// Construct Disturbing Mirth with no live wiring. The two triggers are
    /// attached for shape observability but not registered with any
    /// <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null, zones: null, eventBus: null);

    /// <summary>
    /// Construct Disturbing Mirth with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, both triggers register so the
    /// matching event lands the ability on the stack automatically
    /// (CR 603.3).</param>
    /// <param name="zones">When supplied, manifest dread routes its zone moves
    /// through <see cref="ZoneService"/> so ETB / LTB triggers fire; otherwise
    /// raw-zone moves are used.</param>
    /// <param name="eventBus">When supplied, the ETB sacrifice publishes a
    /// <see cref="PermanentSacrificedEvent"/> so aristocrat payoffs (and, for a
    /// self-sacrifice case, the manifest-dread trigger of OTHER copies) fire.</param>
    public static Enchantment Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zones,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Enchantment,
        // {B}{R}). The JSON carries no abilities — both triggers layer on here.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Enchantment)CardDefinitionFactory.Build(definition, owner, replacements: null);

        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a.
        //   "When this enchantment enters, you may sacrifice another
        //    enchantment or creature. If you do, draw two cards."
        // "You may" (CR 117.5) → consult the agent; auto-take the upside when
        // no agent is registered (sacrificing one permanent to draw two is
        // card advantage). "If you do" (CR 120.2) gates the draw on the
        // optional sacrifice actually being paid — no eligible
        // creature/enchantment (cost can't be paid) → no draw.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: you may sacrifice another enchantment or creature; if you do, draw two cards",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                // CR 109.2 — "another" excludes this card. Filter to
                // creatures-or-enchantments only.
                var sacCost = new SacrificeFilteredCost(
                    filter: p => p.HasType(CardType.Creature)
                              || p.HasType(CardType.Enchantment),
                    description: "sacrifice another enchantment or creature",
                    eventBus: eventBus,
                    excludeSelf: card);

                // "If you do" hinges on actually being able to sacrifice an
                // eligible permanent. None → nothing to pay → no draw
                // (CR 120.2).
                if (!sacCost.CanPay(controller)) return;

                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                bool sac = agent == null
                    || await agent.ChooseYesNoAsync(
                        "Sacrifice another enchantment or creature to draw two cards?",
                        BotIntent.Draw | BotIntent.CardAdvantage).ConfigureAwait(false);

                if (!sac) return;

                // Pay the optional cost, then draw two (CR 120.2 — "If you do").
                sacCost.Pay(controller);
                Fx.DrawCards(controller, DrawCount);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Self-sacrifice trigger — CR 603.6b / CR 701.16 / CR 701.59.
        //   "When you sacrifice this enchantment, manifest dread."
        // Fires on the dedicated PermanentSacrificedEvent whose
        // SacrificedCard IS this card (reference identity — the self scope;
        // "you sacrifice" coincides with "this is sacrificed" because the
        // sacrificing player is always this card's controller, CR 701.16a).
        // Disturbing Mirth has no sacrifice ability of its own, so this fires
        // only when some OTHER effect sacrifices it (an edict, an aristocrat
        // outlet, etc.). The trigger stays active in the Graveyard zone too:
        // by the time PermanentSacrificedEvent publishes the card is already
        // in its owner's graveyard (CR 701.16a).
        // ----------------------------------------------------------------
        var capturedZones = zones;
        var manifestDreadEffect = new Effect(
            $"{CardName}: manifest dread (CR 701.59)",
            () => ManifestDreadEffect.Resolve(
                card.Controller ?? owner,
                capturedZones));

        var selfSacCondition = new EventTriggerCondition<PermanentSacrificedEvent>(
            (e, _) => ReferenceEquals(e.SacrificedCard, card));

        var selfSacTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: selfSacCondition,
            effects: new IEffect[] { manifestDreadEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(selfSacTrigger);
        triggers?.RegisterTriggeredAbility(selfSacTrigger);

        return card;
    }
}
