using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Necropotence (Ice Age — Modern-format defining
/// black draw-engine enchantment). Oracle text:
///
///   Enchantment — {B}{B}{B}
///   "Skip your draw step.
///    Whenever you discard a card, exile that card.
///    Pay 1 life: Exile the top card of your library face down. Put that
///    card into your hand at the beginning of your next end step."
///
/// ## Implemented (v1)
/// - Card identity: Enchantment, {B}{B}{B}, owner/controller.
/// - <b>Static "skip your draw step"</b>: wired against
///   <see cref="SkipDrawRegistry"/> when the registry-aware overload is
///   used. <see cref="Game.TurnDriver"/> consults the registry before
///   drawing the active player's draw-step card and suppresses it while
///   any registered predicate matches (CR 117.5 / 614.12). The skip
///   predicate is gated on Necropotence being on the battlefield, so a
///   bounced/destroyed Necropotence mid-turn stops skipping immediately.
///   The skip is registered eagerly at card-build time and must be
///   unregistered by the caller when Necropotence leaves the battlefield
///   (the returned cleanup <see cref="IDisposable"/> handles that).
/// - <b>Replacement effect — discard → exile</b>: wired against
///   <see cref="ReplacementBus"/> when the bus-aware overload is used.
///   The replacement watches every <see cref="ZoneMoveIntent"/> whose
///   <see cref="ZoneMoveIntent.FromZone"/> is <see cref="ZoneType.Hand"/>
///   and <see cref="ZoneMoveIntent.ToZone"/> is
///   <see cref="ZoneType.Graveyard"/>, and whose card's owner is
///   Necropotence's controller; it rewrites the destination to
///   <see cref="ZoneType.Exile"/>. This is the engine's general
///   "discard" funnel — discards in Majik flow through hand→graveyard
///   zone moves (there is no dedicated DiscardEvent yet). The
///   replacement does not fire when Necropotence is not on the
///   battlefield (belt-and-braces guard on top of the bus Unregister
///   contract).
/// - <b>Activated ability — Pay 1 life: exile top of library + delayed
///   end-step draw</b>: cost is <see cref="AdditionalCost.PayLife"/>(1);
///   effect moves the top of the controller's library into exile and
///   registers a <see cref="DelayedTriggeredAbility"/> on the supplied
///   <see cref="TriggerManager"/> that fires on the next
///   <see cref="StepStartedEvent"/> of type
///   <see cref="PhaseStateType.End"/> and puts the exiled card into the
///   controller's hand (CR 603.7). Each activation registers its own
///   delayed trigger, so multiple activations stack — the
///   TriggerManager auto-unregisters each one after firing.
///
/// ## Deferred (v1 gaps)
/// - <b>Face-down exile</b>: the engine has no per-card face-down flag
///   yet. The exiled card is exile-face-up here, which is observable
///   only by other "look at exile" effects (none in v1) — game-state
///   visible behaviour is correct.
/// - <b>Strict "at the beginning of your next end step"</b>: CR 500.4 —
///   the trigger fires on the FIRST End StepStartedEvent after the
///   activation. v1 matches by timestamp (event.Timestamp &gt;
///   activatedAt) so an activation during End-step priority does not
///   self-fire. Multi-player turn-skipping semantics inherited from
///   <see cref="MishrasBaubleFactory"/> — first matching end step wins.
/// - <b>Lifecycle auto-cleanup</b>: when Necropotence leaves the
///   battlefield, callers must call the returned cleanup
///   <see cref="IDisposable"/> (registry-aware overload) to unregister
///   the skip-draw predicate and the discard replacement. The bus + the
///   registry both no-op on null tokens, so leaking is bounded but not
///   ideal. Auto-cleanup mirrors the
///   <see cref="DauthiVoidwalkerFactory"/> v1 gap.
/// </summary>
[CardName("Necropotence")]
public static class NecropotenceFactory
{
    public const string CardName = "Necropotence";

    /// <summary>
    /// Construct Necropotence with no registry/bus wiring — the card
    /// shape (Enchantment, mana cost, owner, three abilities) is fully
    /// populated but the skip-draw predicate and the discard replacement
    /// do not actually fire. Suitable for shape tests / the dispatcher
    /// path.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, replacements: null, triggerManager: null).Card;

    /// <summary>
    /// Construct Necropotence fully wired against the supplied
    /// <see cref="ReplacementBus"/> + <see cref="TriggerManager"/>. The
    /// returned <see cref="NecropotenceWiring.Cleanup"/> disposable
    /// unregisters the skip-draw predicate and the discard replacement
    /// — call it when Necropotence leaves the battlefield. The
    /// <see cref="NecropotenceWiring.Replacement"/> field exposes the
    /// underlying replacement effect for direct unregister, mirroring
    /// the Dauthi Voidwalker shape.
    /// </summary>
    public static NecropotenceWiring Create(
        Player owner,
        ReplacementBus? replacements,
        TriggerManager? triggerManager)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, "{B}{B}{B}");
        card.SetOwner(owner);
        card.SetController(owner);

        // -----------------------------------------------------------------
        // Static — "Skip your draw step."
        //
        // Registered via SkipDrawRegistry; consulted by TurnDriver before
        // performing the normal draw-step draw. The predicate gates on
        // Necropotence's zone so a bounced enchantment stops skipping
        // immediately (CR 614.12 — replacement effects function only while
        // their source is on the battlefield).
        // -----------------------------------------------------------------
        var skipToken = new object();
        SkipDrawRegistry.AddSkip(skipToken, p =>
            ReferenceEquals(p, card.Controller) && card.Zone == ZoneType.Battlefield);

        // Surface a static-ability marker on the card for shape inspection.
        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: "Skip your draw step.",
            isActiveCheck: () => card.Zone == ZoneType.Battlefield,
            applyEffect: null));

        // -----------------------------------------------------------------
        // Replacement — "Whenever you discard a card, exile that card."
        //
        // CR 614 — funnelled through ZoneMoveIntent on the ReplacementBus.
        // The engine has no DiscardEvent; discards are hand→graveyard zone
        // moves, so we intercept those for cards owned by Necropotence's
        // controller.
        // -----------------------------------------------------------------
        NecropotenceDiscardExileReplacement? replacement = null;
        if (replacements != null)
        {
            replacement = new NecropotenceDiscardExileReplacement(card);
            replacements.Register<ZoneMoveIntent>(replacement);
        }

        // Surface a static-ability marker describing the discard→exile
        // replacement, so shape-only tests can inspect it without a
        // live ReplacementBus. (Majik's <see cref="Abilities.ReplacementEffect"/>
        // class does not implement <see cref="IAbility"/>; the live
        // behaviour lives on the bus instead.)
        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: "Whenever you discard a card, exile that card.",
            isActiveCheck: () => card.Zone == ZoneType.Battlefield,
            applyEffect: null));

        // -----------------------------------------------------------------
        // Activated — "Pay 1 life: Exile the top card of your library face
        //              down. Put that card into your hand at the beginning
        //              of your next end step."
        //
        // CR 605 — not a mana ability. The effect exiles the top of the
        // controller's library and registers a one-shot
        // DelayedTriggeredAbility that fires on the next End-step
        // StepStartedEvent and returns the exiled card to hand. Each
        // activation registers an independent delayed trigger, so
        // multiple activations stack and each resolves at the same end
        // step (CR 603.7d — delayed triggers each fire once and
        // auto-unregister).
        //
        // Face-down exile is deferred — engine has no face-down flag.
        // -----------------------------------------------------------------
        var activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.PayLife(1),
            },
            effects: new IEffect[]
            {
                new Effect(
                    "Necropotence: exile top of library + queue delayed end-step return-to-hand",
                    () =>
                    {
                        // Necropotence only functions while on the battlefield
                        // (CR 113.6). Guard the effect body too — the cost
                        // payment has already happened by the time we reach
                        // here, but a destroyed Necropotence shouldn't be
                        // able to dredge more cards in response.
                        if (card.Zone != ZoneType.Battlefield) return;

                        var top = owner.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null)
                        {
                            // Empty library — SBA loss handled elsewhere (CR
                            // 704.5b). No card to exile; no delayed trigger
                            // to register.
                            return;
                        }

                        owner.Zones.Library.RemoveCard(top);
                        owner.Zones.Exile.AddCard(top);
                        top.SetZone(ZoneType.Exile);

                        if (triggerManager == null)
                        {
                            // No registry — caller opted out of the delayed
                            // return-to-hand. The card stays in exile.
                            return;
                        }

                        var activatedAt = DateTime.UtcNow;
                        var exiledCard = top;
                        var returnEffect = new Effect(
                            "Necropotence: put exiled card into hand (delayed end step)",
                            () =>
                            {
                                if (exiledCard.Zone != ZoneType.Exile) return;
                                owner.Zones.Exile.RemoveCard(exiledCard);
                                owner.Zones.Hand.AddCard(exiledCard);
                                exiledCard.SetZone(ZoneType.Hand);
                            });

                        var delayed = new DelayedTriggeredAbility(
                            source: card,
                            controller: owner,
                            condition: new EventTriggerCondition<StepStartedEvent>(
                                (e, _) => e.StepType == PhaseStateType.End
                                          && e.Timestamp > activatedAt),
                            effects: new IEffect[] { returnEffect });

                        triggerManager.RegisterDelayed(delayed);
                    }),
            });

        card.AddAbility(activated);

        var cleanup = new NecropotenceCleanup(skipToken, replacements, replacement);
        return new NecropotenceWiring(card, replacement, cleanup);
    }
}

/// <summary>
/// Bundle of the artifact handles returned by the bus-aware
/// <see cref="NecropotenceFactory.Create(Player, ReplacementBus?, TriggerManager?)"/>
/// overload. <see cref="Cleanup"/> unregisters the skip-draw predicate
/// and the discard replacement on dispose — call it when Necropotence
/// leaves the battlefield.
/// </summary>
public sealed record NecropotenceWiring(
    Enchantment Card,
    NecropotenceDiscardExileReplacement? Replacement,
    IDisposable Cleanup);

/// <summary>
/// Replacement effect: when a card owned by Necropotence's controller
/// would move from <see cref="ZoneType.Hand"/> to
/// <see cref="ZoneType.Graveyard"/>, rewrite the destination to
/// <see cref="ZoneType.Exile"/>. This implements the "Whenever you
/// discard a card, exile that card" clause via the generic
/// hand→graveyard zone-move funnel that the engine uses for discards
/// (there is no dedicated DiscardEvent in v1).
/// </summary>
public sealed class NecropotenceDiscardExileReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly Enchantment _necropotence;

    public NecropotenceDiscardExileReplacement(Enchantment necropotence)
    {
        _necropotence = necropotence ?? throw new ArgumentNullException(nameof(necropotence));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        if (intent.FromZone != ZoneType.Hand) return false;
        if (intent.ToZone != ZoneType.Graveyard) return false;
        // CR 113.6 / 614.12 — only fires while Necropotence is on the
        // battlefield. Belt-and-braces alongside the bus Unregister
        // contract.
        if (_necropotence.Zone != ZoneType.Battlefield) return false;

        var cardOwner = intent.Card.Owner;
        if (cardOwner == null) return false;
        var controller = _necropotence.Controller;
        if (controller == null) return false;

        return ReferenceEquals(cardOwner, controller);
    }

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { ToZone = ZoneType.Exile };
}

/// <summary>
/// Disposable cleanup handle: unregisters the skip-draw predicate from
/// <see cref="SkipDrawRegistry"/> and the discard replacement from the
/// supplied <see cref="ReplacementBus"/>. Idempotent — multiple
/// <see cref="Dispose"/> calls are safe.
/// </summary>
internal sealed class NecropotenceCleanup : IDisposable
{
    private readonly object _skipToken;
    private readonly ReplacementBus? _bus;
    private readonly NecropotenceDiscardExileReplacement? _replacement;
    private bool _disposed;

    public NecropotenceCleanup(
        object skipToken,
        ReplacementBus? bus,
        NecropotenceDiscardExileReplacement? replacement)
    {
        _skipToken = skipToken ?? throw new ArgumentNullException(nameof(skipToken));
        _bus = bus;
        _replacement = replacement;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SkipDrawRegistry.RemoveSkip(_skipToken);
        if (_bus != null && _replacement != null)
        {
            _bus.Unregister<ZoneMoveIntent>(_replacement);
        }
    }
}
