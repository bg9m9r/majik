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
    public string Description => Keyword;

    public KeywordAbility(string keyword, object? source = null, Player? controller = null)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            throw new ArgumentException("Keyword required", nameof(keyword));
        }
        Keyword = keyword;
        Source = source ?? new object();
        Controller = controller;
    }

    Player IStaticAbility.Controller => Controller!;

    public bool IsActive() => true;
    public void ApplyEffect() { /* no continuous mutation — combat code reads marker directly */ }
}
