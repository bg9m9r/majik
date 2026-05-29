namespace Majik.Core.CardData;

/// <summary>
/// Process-wide feature flag controlling whether factory-backed NON-LAND
/// cards are built through their <c>[CardName]</c> <c>*Factory</c> in the
/// production card-build paths (<c>GameFacade.Create</c> /
/// <see cref="ScryfallCardFactory.Create"/>) rather than arriving as
/// ability-less binder-chain shells.
///
/// <para>The per-card factories were historically TEST-ONLY (only
/// <c>NamedCardFactory.Create</c> dispatched to them, which prod never
/// called), so bespoke factory abilities — Agatha's Soul Cauldron's
/// <c>{T}</c>, Dredger's Insight's JSON ETB triggers, … — did nothing in a
/// real match even though their unit tests passed. Routing closes that gap.
/// Flip to <c>false</c> for a no-revert kill-switch.</para>
///
/// <para>The flag lives in Majik.Core (not Majik.Core.Api) because
/// <see cref="ScryfallCardFactory"/> — a Majik.Core type — must read it, and
/// Majik.Core cannot reference the higher Api layer. <c>GameFacade</c>
/// re-exposes it as <c>RouteThroughNamedFactories</c> for the server
/// composition root.</para>
///
/// <para>Lands are NEVER routed regardless of this flag — their factories
/// deliberately omit enters-tapped/shock and rely on the binder chain.</para>
/// </summary>
public static class FactoryRouting
{
    /// <summary>DEFAULT TRUE. See type docs.</summary>
    public static bool RouteThroughNamedFactories { get; set; } = true;
}
