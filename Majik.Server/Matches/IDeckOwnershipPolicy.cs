namespace Majik.Server.Matches;

/// <summary>
/// Decides whether <see cref="MatchService"/> may proceed without a
/// real <see cref="Decks.DeckRepository"/>/<see cref="Decks.DeckValidationService"/>
/// pair wired in.
///
/// Default impl: <see cref="StrictDeckOwnershipPolicy"/> — refuses
/// construction when the deck plumbing is missing. The strict policy
/// is what production runs; bypassing it would mean any caller could
/// quote any deck id and have the match service treat it as theirs.
///
/// Tests that genuinely want the legacy <see cref="StubDeckLoader"/>
/// path (where decks are toy strings like <c>"burn"</c> and ownership
/// has no meaning) inject <see cref="AllowStubDeckOwnershipPolicy"/>.
/// </summary>
public interface IDeckOwnershipPolicy
{
    /// <summary>True if the service may run without DeckRepository/
    /// DeckValidationService. False (the default) means the service
    /// constructor must reject the missing-plumbing config.</summary>
    bool AllowMissingDeckPlumbing { get; }
}

/// <summary>Strict policy — production default. Refuses construction
/// when DeckRepository or DeckValidationService is null so the
/// ownership check in <c>ResolveDeckSnapshotAsync</c> cannot be
/// skipped.</summary>
public sealed class StrictDeckOwnershipPolicy : IDeckOwnershipPolicy
{
    public bool AllowMissingDeckPlumbing => false;
}

/// <summary>Test-only policy that lets <see cref="MatchService"/> run
/// in stub mode (no real deck repository). Wired by integration tests
/// that use <see cref="StubDeckLoader"/>; never registered by the
/// production composition root.</summary>
public sealed class AllowStubDeckOwnershipPolicy : IDeckOwnershipPolicy
{
    public bool AllowMissingDeckPlumbing => true;
}
