using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData;

/// <summary>
/// Builds the spell-definition resolver <see cref="Majik.Core.Game.TurnDriver"/>
/// consults at cast time for non-permanent spells (Lightning Bolt, Lava Spike,
/// Boltwave, …). Cards do NOT carry their spell definitions — they are resolved
/// AT CAST TIME, BY NAME, via <see cref="ScryfallCardFactory.LookupSpellDefinition"/>
/// (→ <see cref="OracleSpellBinder.Bind"/>). Without a resolver,
/// <c>TurnDriver.DispatchCast</c> hits the "no SpellDef for instant/sorcery"
/// branch and rotates the card back into hand — every instant/sorcery becomes
/// uncastable.
///
/// <para>
/// Single definition shared by <see cref="Majik.Core.Api.GameFacade"/> (live
/// games) and <see cref="Majik.Core.Simulation.SandboxGame"/> (bot-search
/// sandboxes), so in-sim casting resolves spells through EXACTLY the same
/// binder pipeline as the live engine. Extracted from GameFacade's private
/// <c>BuildSpellDefinitionResolver</c> (same discipline as
/// <c>DeckCardBuilder</c>).
/// </para>
/// </summary>
public static class SpellDefinitionResolverFactory
{
    /// <summary>
    /// Create a cast-time spell-definition resolver over <paramref name="cardRepo"/>.
    /// Returns null when <paramref name="cardRepo"/> is null — callers preserve
    /// their no-resolver behaviour (TurnDriver's skip-rotate branch).
    ///
    /// <para>
    /// The optional subsystem services are threaded into the underlying
    /// <see cref="ScryfallCardFactory"/> so bound definitions can register
    /// replacement/continuous/triggered effects against the CALLER's game
    /// (facade subsystems for live games, sandbox-local subsystems for sims).
    /// </para>
    /// </summary>
    public static Func<ICard, Player, Majik.Core.Stack.Stack?, SpellDefinition?>? Create(
        ICardRepository? cardRepo,
        ReplacementBus? replacements = null,
        ContinuousEffectsService? effects = null,
        TriggerManager? triggers = null,
        IEventBus? eventBus = null,
        ZoneService? zones = null)
    {
        if (cardRepo == null) return null;

        var spellFactory = new ScryfallCardFactory(
            cardRepo,
            replacements: replacements,
            effects: effects,
            triggers: triggers,
            eventBus: eventBus,
            zones: zones);

        return (card, caster, stk) =>
            spellFactory.LookupSpellDefinition(card.Name, caster, raw => raw, stk);
    }
}
