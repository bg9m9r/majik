namespace Majik.Core.Rules.Sba;

/// <summary>
/// A single state-based action check (CR 704). Each check runs against
/// the shared <see cref="SbaContext"/> and reports whether any action
/// executed; the coordinator loops the full check set until a quiescent
/// pass is reached (CR 704.4).
///
/// Implementations are stateless — the context carries everything they
/// need. Order in the registered list determines execution order; the
/// engine's default registration follows the rule numbering in CR
/// 704.5.
/// </summary>
public interface IStateBasedActionCheck
{
    /// <summary>Short human-readable identifier (used in events / logs).</summary>
    string Name { get; }

    /// <summary>Execute the check. Returns true if any change was made
    /// (so the coordinator knows to loop).</summary>
    bool Execute(SbaContext context);
}
