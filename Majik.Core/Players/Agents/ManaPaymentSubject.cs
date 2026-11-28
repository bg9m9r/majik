using Majik.Core.Cards;

namespace Majik.Core.Players.Agents;

/// <summary>
/// What a mana payment prompt pays for (CR 601.2g-h). Display data only.
/// </summary>
/// <param name="Source">The spell's card.</param>
/// <param name="StackEntryId">The id the spell has on the stack once cast
/// (its CR 601.2a being-cast entry and the pushed spell share it). The cast
/// dispatchers ask for mana before the card moves to the stack, so the entry
/// is not in a snapshot taken while this prompt is open.</param>
/// <param name="AutoPayAvailable">Whether answering with no sources ("auto-pay")
/// would pay the cost: the floating pool covers it, or untapped sources can be
/// auto-selected (ManaPaymentResolver.TryAutoSelectSources).</param>
public sealed record ManaPaymentSubject(ICard Source, Guid? StackEntryId, bool AutoPayAvailable);