using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Anointed Procession (Amonkhet, {3}{W}).
///
/// Enchantment. Oracle text:
///   "If an effect would create one or more tokens under your control,
///    it creates twice that many of those tokens instead."
///
/// ## Implemented (v1)
/// - Card identity: Enchantment, mana cost {3}{W}, owner / controller
///   wiring. Dispatchable via <see cref="NamedCardFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Token-creation doubling (CR 614 replacement)</b>: there is no
///   <c>TokenCreationIntent</c> primitive in the engine yet — token spawns
///   go straight through <see cref="Majik.Core.Tokens.TokenFactory"/> /
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Tokens.TokensSpellFactory"/>
///   without flowing through a replaceable intent. Doubling Season /
///   Parallel Lives / Anointed Procession all need the same primitive; once
///   it lands, this factory can register a
///   <c>LambdaReplacement&lt;TokenCreationIntent&gt;</c> that multiplies
///   <c>Amount</c> by 2 when the controller matches and Anointed Procession
///   is on the battlefield (same shape as
///   <see cref="BoonReflectionFactory"/>'s life-gain doubling).
/// - Multiple copies should stack multiplicatively (two Processions ->
///   4× tokens). Same posture as Boon Reflection's per-effect dedup in
///   <see cref="Majik.Core.Effects.ReplacementBus.Apply{TIntent}"/>.
/// </summary>
[CardName("Anointed Procession")]
public static class AnointedProcessionFactory
{
    public const string CardName = "Anointed Procession";
    public const string Cost = "{3}{W}";

    /// <summary>
    /// Construct Anointed Procession. Card identity only — the token-
    /// doubling replacement is deferred (no <c>TokenCreationIntent</c>
    /// primitive yet; see class xmldoc).
    /// </summary>
    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
