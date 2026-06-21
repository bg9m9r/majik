using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Meddling Mage (Planeshift / various reprints,
/// {W}{U}).
///
/// Creature — Human Wizard, 2/2.
/// Oracle text:
///   "As Meddling Mage enters the battlefield, choose a nonland card name.
///    Spells with the chosen name can't be cast."
///
/// ## Implemented (v1)
/// - Creature with mana cost {W}{U}, P/T 2/2, Human + Wizard subtypes
///   and correct identity / owner / controller.
/// - <b>ETB name choice</b>: accepted as an optional
///   <paramref name="chosenName"/> parameter in
///   <see cref="Create(Player,string)"/>.  Single-arg path defaults to
///   <see cref="string.Empty"/> (no restriction) for dispatcher shape
///   tests.
/// - <b>Printed static</b> (CR 601.3): name-targeted cast restriction.
///   Wired via <see cref="MeddlingMageCastRestrictionEffect"/>: while the
///   Mage is on the battlefield, the chosen name is registered into
///   <see cref="Majik.Core.Rules.CastingRestrictions"/> via
///   <c>AddNamedCardBlock</c>, and
///   <see cref="Majik.Core.Rules.ActionValidator"/> rejects any
///   <c>CastSpellAction</c> whose card name matches. The effect detaches
///   as the Mage leaves the battlefield via
///   <see cref="Majik.Core.Events.CardMovedEvent"/> on the supplied bus.
///
/// ## Agent-prompt integration (CR 614.12 / CR 201.4)
/// The deferral <c>choose-card-name-agent-surface</c> is paid down: the
/// production single-arg <see cref="Create(Player)"/> (and the
/// <see cref="Create(Player, Majik.Core.Game.GameContext?, IEventBus?)"/>
/// overload) resolve the chosen NONLAND name through
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseCardNameAsync"/> via
/// <see cref="Majik.Core.CardData.CardNameChoice"/> — the opponents' visible
/// nonland "known threats" pool, most-threatening-first. The
/// <c>string chosenName</c> overload remains for tests that supply a fixed name.
///
/// ## Deferred (v1 gaps)
/// - <b>"As ~ enters" choice timing</b>: the name is resolved when the factory
///   builds the lifecycle (matching the pre-existing chosenName overload's
///   construction-time posture) rather than strictly as part of the ETB
///   replacement (CR 614.12) — observationally equivalent in the current ETB
///   pipeline.
/// - <b>"nonland card name" validation</b>: the chosen name is accepted as a
///   raw string; the suggestion pool excludes lands, but enforcement that an
///   agent-overriding name isn't a basic land is deferred (rules-layer, not
///   mechanical).
/// </summary>
[CardName("Meddling Mage")]
public static class MeddlingMageFactory
{
    public const string CardName = "Meddling Mage";
    public const string Cost = "{W}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct a Meddling Mage whose ETB name choice is resolved through the
    /// controller's <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> (the
    /// production posture — pays down the
    /// <c>choose-card-name-agent-surface</c> deferral). Prompts
    /// <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseCardNameAsync"/>
    /// at ETB via <see cref="CardNameChoice"/> with the "nonland card name"
    /// constraint. When no agent is registered (a pure shape / dispatcher test
    /// with no game) the choice returns empty and the static stays inert — the
    /// same observable behaviour the old empty-name single-arg build had.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, game: null, eventBus: null);

    /// <summary>
    /// Production-shaped overload: resolve the chosen NONLAND name through the
    /// owner's agent at ETB. <paramref name="game"/> is threaded into
    /// <see cref="CardNameChoice"/> so the agent's suggestion pool is the
    /// opponents' visible nonland "known threats" (most-threatening first). May
    /// be null (no live game → empty suggestion pool, agent falls back).
    /// </summary>
    public static Creature Create(Player owner, GameContext? game, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var chosen = CardNameChoice.ChooseSync(
            game, owner, CardNameChoice.NonlandCardNameLabel, nonlandOnly: true);
        return Create(owner, chosenName: chosen, eventBus: eventBus);
    }

    /// <summary>
    /// Construct a Meddling Mage with <paramref name="chosenName"/> as the
    /// ETB-declared name. When <paramref name="eventBus"/> is supplied, the
    /// printed static lifecycle is fully wired (name registered into
    /// <see cref="Majik.Core.Rules.CastingRestrictions"/> while the Mage is
    /// on the battlefield; removed on LTB).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="chosenName">The nonland card name chosen as the Mage
    /// enters. An empty string means no restriction (useful for shape
    /// tests). May be null — treated as empty.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be null
    /// — the lifecycle will still sync once on Attach (no LTB
    /// unregistration).</param>
    public static Creature Create(
        Player owner,
        string? chosenName,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var mage = new Creature(
            CardName,
            Cost,
            Power,
            Toughness,
            supertypes: null,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        mage.SetOwner(owner);
        mage.SetController(owner);

        var name = chosenName ?? string.Empty;
        if (!string.IsNullOrEmpty(name))
        {
            var lifecycle = new MeddlingMageCastRestrictionEffect(
                source: mage,
                chosenName: name,
                eventBus: eventBus);
            lifecycle.Attach();
        }

        return mage;
    }
}
