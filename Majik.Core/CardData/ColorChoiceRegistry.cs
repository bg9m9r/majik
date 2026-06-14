using Majik.Core.Cards;

namespace Majik.Core.CardData;

/// <summary>
/// CR 614.12 — process-wide, per-card holder table for an "as this enters,
/// choose a color" decision on NON-LAND permanents (Coldsteel Heart's artifact,
/// Utopia Sprawl's Aura). The land members of the family
/// (Sunken Citadel, Temple of the Dragon Queen) keep their own
/// <see cref="OracleManaBinder.GetColorChoice"/> table because lands are built
/// purely through the binder chain; the non-land members are built through their
/// <c>[CardName]</c> factory (<see cref="FactoryRouting"/>), so their factory
/// stashes the <see cref="ColorChoice"/> here for the
/// <see cref="ChooseColorPermanentBinder"/> overlay to find and register an
/// agent-prompting <see cref="Majik.Core.Effects.ChooseColorReplacement"/>.
///
/// <para>
/// Keyed off the built card via a <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey,TValue}"/>
/// so the synthesized chosen-colour mana ability / triggered mana ability (which
/// read the holder at activation / trigger time) and the ETB choose-color
/// replacement (which stamps the agent's pick as the permanent enters) share one
/// instance without a public mutable property on the card. Entries are GC'd with
/// the card — mirrors <see cref="OracleManaBinder"/>'s per-land idiom.
/// </para>
/// </summary>
public static class ColorChoiceRegistry
{
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<ICard, ColorChoice>
        _choices = new();

    /// <summary>Stash the <see cref="ColorChoice"/> a factory created for an
    /// "as this enters, choose a color" non-land permanent. Idempotent — a
    /// re-stash overwrites the entry.</summary>
    public static void Set(ICard card, ColorChoice choice)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(choice);
        _choices.AddOrUpdate(card, choice);
    }

    /// <summary>The <see cref="ColorChoice"/> a "choose a color" non-land
    /// permanent created when its mana ability / trigger was wired, or
    /// <c>null</c> when the card has no such choice. The
    /// <see cref="ChooseColorPermanentBinder"/> looks this up to register the
    /// agent prompt that stamps the chosen colour.</summary>
    public static ColorChoice? Get(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return _choices.TryGetValue(card, out var choice) ? choice : null;
    }
}
