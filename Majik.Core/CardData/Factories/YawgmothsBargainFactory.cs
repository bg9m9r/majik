using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Yawgmoth's Bargain (Urza's Destiny, {4}{B}{B}).
///
/// Enchantment. Oracle text:
///   "Skip your draw step.
///    Pay 1 life: Draw a card."
///
/// ## Why it gets its own factory
/// Yawgmoth's Bargain is the original black "skip your draw step, trade
/// life for cards" engine — the spiritual ancestor to Necropotence
/// (and current Vintage-restricted poster child). The shape is a
/// strict subset of <see cref="NecropotenceFactory"/>: static
/// skip-draw + activated "pay 1 life → card" — but the card draws
/// immediately (no exile/end-step delay) and there's no discard→exile
/// replacement. That lets us reuse all the existing primitives —
/// <see cref="SkipDrawRegistry"/> for the static, <see cref="ICost"/>
/// + <see cref="PayLifeCost"/> for the cost, a direct top-of-library
/// draw for the effect — without building anything new.
///
/// ## Implemented (v1)
/// - Card identity: Enchantment, {4}{B}{B}, owner/controller.
/// - <b>Static "Skip your draw step"</b>: wired against
///   <see cref="SkipDrawRegistry"/> using the same posture as Necropotence
///   — predicate gates on Yawgmoth's Bargain being on the battlefield, so
///   a bounced/destroyed Bargain mid-turn stops skipping immediately
///   (CR 117.5 / 614.12 — replacements function only from the
///   battlefield). The skip is registered eagerly at card-build time
///   when the registry-aware overload is used; callers must dispose the
///   returned <see cref="YawgmothsBargainWiring.Cleanup"/> handle when
///   the card leaves the battlefield.
/// - <b>Activated ability — "Pay 1 life: Draw a card"</b>: cost is
///   <see cref="PayLifeCost"/>(1); effect draws the top card of the
///   controller's library directly to hand (no exile staging, no
///   delayed trigger). Empty library = no-op (SBA loss handled
///   elsewhere via CR 704.5b). The activation has no implicit upper
///   bound — the controller can activate repeatedly while they have
///   life and library cards. CR 605 says this is NOT a mana ability
///   (it draws a card, not produces mana).
///
/// ## Deferred (v1 gaps)
/// - <b>Lifecycle auto-cleanup</b>: when Yawgmoth's Bargain leaves the
///   battlefield, callers must call the returned cleanup
///   <see cref="IDisposable"/> (registry-aware overload) to unregister
///   the skip-draw predicate. Mirrors the Necropotence v1 gap — the
///   registry no-ops on stale tokens, so leaking is bounded but not
///   ideal.
/// - <b>Hand-size SBA / no in-trigger "would lose the game" guard</b>:
///   the cost is gated by <see cref="PayLifeCost.CanPay"/>, which
///   requires <c>LifeTotal &gt;= 1</c> (CR 119.4 — you can't pay life
///   you don't have). Players cannot suicide themselves at 1 life
///   activating Bargain — the cost fails before the ability hits the
///   stack.
/// </summary>
[CardName("Yawgmoth's Bargain")]
public static class YawgmothsBargainFactory
{
    public const string CardName = "Yawgmoth's Bargain";
    public const string PrintedManaCost = "{4}{B}{B}";

    /// <summary>Printed oracle text — informational.</summary>
    public const string OracleText =
        "Skip your draw step.\n" +
        "Pay 1 life: Draw a card.";

    /// <summary>
    /// Construct Yawgmoth's Bargain with no registry wiring — the card
    /// shape (Enchantment, mana cost, owner, two abilities) is fully
    /// populated but the skip-draw predicate is not registered. Suitable
    /// for shape tests / the dispatcher path.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, registerSkipDraw: false).Card;

    /// <summary>
    /// Construct Yawgmoth's Bargain, optionally registering the static
    /// "Skip your draw step" predicate against
    /// <see cref="SkipDrawRegistry"/>. When
    /// <paramref name="registerSkipDraw"/> is true the returned
    /// <see cref="YawgmothsBargainWiring.Cleanup"/> disposable
    /// unregisters the predicate — call it when Yawgmoth's Bargain
    /// leaves the battlefield. When false, the returned cleanup is a
    /// no-op.
    /// </summary>
    public static YawgmothsBargainWiring Create(Player owner, bool registerSkipDraw)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // -----------------------------------------------------------------
        // Static — "Skip your draw step."
        //
        // Registered via SkipDrawRegistry; consulted by TurnDriver before
        // performing the active player's draw-step draw. Predicate gates
        // on Yawgmoth's Bargain's zone so a bounced enchantment stops
        // skipping immediately (CR 614.12).
        // -----------------------------------------------------------------
        object? skipToken = null;
        if (registerSkipDraw)
        {
            skipToken = new object();
            SkipDrawRegistry.AddSkip(skipToken, p =>
                ReferenceEquals(p, card.Controller) && card.Zone == ZoneType.Battlefield);
        }

        // Surface a static-ability marker on the card for shape inspection.
        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: "Skip your draw step.",
            isActiveCheck: () => card.Zone == ZoneType.Battlefield,
            applyEffect: null));

        // -----------------------------------------------------------------
        // Activated — "Pay 1 life: Draw a card."
        //
        // CR 605 — NOT a mana ability (draws a card). Cost is PayLifeCost(1)
        // which gates on LifeTotal >= 1 (CR 119.4). On execution we draw
        // the top of the controller's library into hand directly. Empty
        // library = no-op; SBA loss is handled elsewhere by 704.5b on the
        // next SBA check.
        // -----------------------------------------------------------------
        var activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new PayLifeCost(1),
            },
            effects: new IEffect[]
            {
                new Effect(
                    "Yawgmoth's Bargain: draw a card",
                    () =>
                    {
                        // Functions only from the battlefield (CR 113.6).
                        if (card.Zone != ZoneType.Battlefield) return;

                        var top = owner.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null)
                        {
                            // Empty library — SBA loss (CR 704.5b) fires
                            // on the next SBA check. The activation has
                            // no draw to perform; the life cost still
                            // happened in the cost phase.
                            return;
                        }

                        owner.Zones.Library.RemoveCard(top);
                        owner.Zones.Hand.AddCard(top);
                        top.SetZone(ZoneType.Hand);
                    }),
            });

        card.AddAbility(activated);

        IDisposable cleanup = skipToken != null
            ? new YawgmothsBargainCleanup(skipToken)
            : NoOpCleanup.Instance;

        return new YawgmothsBargainWiring(card, cleanup);
    }
}

/// <summary>
/// Bundle of artifacts returned by the registry-aware
/// <see cref="YawgmothsBargainFactory.Create(Player, bool)"/> overload.
/// <see cref="Cleanup"/> unregisters the skip-draw predicate on dispose
/// — call it when Yawgmoth's Bargain leaves the battlefield.
/// </summary>
public sealed record YawgmothsBargainWiring(
    Enchantment Card,
    IDisposable Cleanup);

/// <summary>
/// Disposable cleanup handle: unregisters the skip-draw predicate from
/// <see cref="SkipDrawRegistry"/>. Idempotent — multiple
/// <see cref="Dispose"/> calls are safe.
/// </summary>
internal sealed class YawgmothsBargainCleanup : IDisposable
{
    private readonly object _skipToken;
    private bool _disposed;

    public YawgmothsBargainCleanup(object skipToken)
    {
        _skipToken = skipToken ?? throw new ArgumentNullException(nameof(skipToken));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SkipDrawRegistry.RemoveSkip(_skipToken);
    }
}

/// <summary>
/// No-op cleanup placeholder for callers that constructed Yawgmoth's
/// Bargain without registering the skip-draw predicate. Returned by
/// the shape-only overload so callers can always call
/// <c>wiring.Cleanup.Dispose()</c> uniformly.
/// </summary>
internal sealed class NoOpCleanup : IDisposable
{
    public static readonly NoOpCleanup Instance = new();
    private NoOpCleanup() { }
    public void Dispose() { }
}
