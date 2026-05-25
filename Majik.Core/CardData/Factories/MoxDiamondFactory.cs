using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mox Diamond (Stronghold, {0}).
///
/// Artifact. Oracle text:
///   "If Mox Diamond would enter, you may discard a land card instead.
///    If you don't, sacrifice Mox Diamond.
///    {T}: Add one mana of any color."
///
/// ## Implemented (v1)
/// - <b>Artifact</b> shape, mana cost {0} (owner / controller wiring).
/// - <b>{T}: Add one mana of any color</b> — modelled as five
///   <see cref="ManaAbility"/> instances (one per WUBRG), each gated on
///   <c>!IsTapped</c>. Same shape as <see cref="MoxAmberFactory"/> /
///   <see cref="MoxOpalFactory"/> / <see cref="DelightedHalflingFactory"/>
///   / City of Brass — the engine has no "pick a colour at activation"
///   modal mana-ability primitive yet; the bot's source-picker selects
///   the right colour at payment time.
/// - <b>ETB printed replacement</b> (CR 614) — "If Mox Diamond would
///   enter, you may discard a land card instead. If you don't,
///   sacrifice Mox Diamond." Wired via
///   <see cref="MoxDiamondEntersReplacementEffect"/>: when a
///   <see cref="ReplacementBus"/> is supplied, registered at
///   construction time so the replacement is live before the card is
///   cast. On each ETB attempt:
///   <list type="bullet">
///     <item>Prompts the controller (yes/no — discard a land?).</item>
///     <item>Yes path: picks a land in hand via
///           <see cref="IPlayerAgent.ChooseFromHandAsync"/>, moves it
///           Hand → Graveyard, then lets Mox Diamond enter normally.</item>
///     <item>No path (or no land in hand): rewrites the would-enter
///           intent's destination to <see cref="ZoneType.Graveyard"/>
///           so Mox Diamond never actually enters — it's redirected
///           to the graveyard as the "sacrifice" tail (CR 614 — the
///           replacement fires before the move resolves).</item>
///   </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Modal-colour mana ability</b>: same gap as every other "add one
///   mana of any color" card — see <see cref="MoxOpalFactory"/>.
/// - <b>Shape-only path</b>: <see cref="Create(Player)"/> omits the
///   <see cref="MoxDiamondEntersReplacementEffect"/> registration (no
///   <see cref="ReplacementBus"/>), so dispatcher tests can construct
///   the card without wiring a full engine. Production wiring path uses
///   <see cref="Create(Player, ReplacementBus?, Func{Player, IPlayerAgent?}?)"/>.
/// </summary>
[CardName("Mox Diamond")]
public static class MoxDiamondFactory
{
    public const string CardName = "Mox Diamond";
    public const string PrintedManaCost = "{0}";

    private static readonly string[] Colors = { "W", "U", "B", "R", "G" };

    /// <summary>
    /// Construct Mox Diamond with no live ETB-replacement wiring. Suitable
    /// for factory-shape / dispatcher tests — the mana abilities are
    /// attached but the printed ETB replacement will not fire (Mox
    /// Diamond will just enter the battlefield normally).
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, replacementBus: null, agentSelector: null);

    /// <summary>
    /// Construct a fully-wired Mox Diamond. When
    /// <paramref name="replacementBus"/> is supplied, the printed ETB
    /// replacement (CR 614) is registered against the bus immediately so
    /// the would-enter prompt fires the first time Mox Diamond would
    /// enter the battlefield.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacementBus">Bus the ETB replacement registers on.
    /// Null → no replacement is wired (shape-only).</param>
    /// <param name="agentSelector">Optional override for the agent lookup
    /// used by the ETB prompt. Null → falls back to
    /// <see cref="AgentRegistry.Get(Player)"/>.</param>
    public static Artifact Create(
        Player owner,
        ReplacementBus? replacementBus,
        Func<Player, IPlayerAgent?>? agentSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var mox = new Artifact(CardName, PrintedManaCost);
        mox.SetOwner(owner);
        mox.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add one mana of any color.
        // Five ManaAbility instances, each gated on !IsTapped only —
        // Mox Diamond has no metalcraft / legendaries-on-board gate
        // (compare Mox Opal / Mox Amber). Live controller is read off
        // mox.Controller so control-change effects shift production
        // ownership correctly.
        // ----------------------------------------------------------------
        foreach (var code in Colors)
        {
            mox.AddAbility(new ManaAbility(
                source: mox,
                controller: owner,
                manaGenerated: ManaCost.Parse(code),
                canActivateCheck: () => !mox.IsTapped));
        }

        // ----------------------------------------------------------------
        // ETB printed replacement — CR 614.
        //   "If Mox Diamond would enter, you may discard a land card
        //    instead. If you don't, sacrifice Mox Diamond."
        // Wired through MoxDiamondEntersReplacementEffect when a
        // ReplacementBus is supplied; shape-only path skips registration.
        // ----------------------------------------------------------------
        if (replacementBus != null)
        {
            var lifecycle = new MoxDiamondEntersReplacementEffect(
                source: mox,
                replacementBus: replacementBus,
                agentSelector: agentSelector);
            lifecycle.Attach();
        }

        return mox;
    }
}
