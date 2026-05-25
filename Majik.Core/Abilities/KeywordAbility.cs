using Majik.Core.Players;

namespace Majik.Core.Abilities;

/// <summary>
/// Simple value-carrier ability representing a named evergreen keyword
/// (Flying, First Strike, Vigilance, Deathtouch, Trample, Haste, Reach,
/// Double Strike, Lifelink, etc.). The keyword's combat semantics live in
/// <see cref="Majik.Core.Combat.CombatAbilities"/>; this type is just the
/// marker the runtime scans for.
///
/// Effectively a stand-in until the full Rule 613 layer system arrives.
/// </summary>
public sealed class KeywordAbility : IStaticAbility
{
    public string Keyword { get; }
    public object Source { get; }
    public Player? Controller { get; }
    public string Description => Arg is int n ? $"{Keyword} {n}" : Keyword;

    /// <summary>
    /// Optional numeric parameter for parameterised keywords (CR 702.x —
    /// e.g. Annihilator N, Ward N, Fading N, Vanishing N, Bushido N). Null
    /// for non-parameterised keywords (Flying, Trample, Haste, etc.).
    /// Read by the keyword's wiring factory (e.g. <c>AnnihilatorFactory</c>
    /// reads <c>Arg</c> off the marker to decide how many permanents the
    /// defending player must sacrifice on attack).
    /// </summary>
    public int? Arg { get; }

    public KeywordAbility(string keyword, object? source = null, Player? controller = null, int? arg = null)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            throw new ArgumentException("Keyword required", nameof(keyword));
        }
        Keyword = keyword;
        Source = source ?? new object();
        Controller = controller;
        Arg = arg;
    }

    Player IStaticAbility.Controller => Controller!;

    public bool IsActive() => true;
    public void ApplyEffect() { /* no continuous mutation — combat code reads marker directly */ }
}
