using System.Reflection;
using System.Runtime.CompilerServices;
using Majik.Core.Api;
using Xunit.Sdk;

[assembly: Majik.Core.Api.Tests.FailOnUnexpectedContainedFault]

namespace Majik.Core.Api.Tests;

/// <summary>
/// Opt-in for a test (or test class) that deliberately provokes an engine
/// fault the facade contains (see <see cref="GameFacade.OnEngineDiagnostic"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class ExpectsContainedFaultAttribute : Attribute
{
}

/// <summary>
/// Production contains some engine faults (a card's gate throwing while a
/// prompt's legal-action hint is built, a fault while a concession unwinds)
/// and only reports them. In tests that would hide real defects, so every test
/// in this assembly fails if a contained fault was reported on its async flow,
/// unless it is marked <see cref="ExpectsContainedFaultAttribute"/>.
/// Faults raised on flows the test did not start (e.g. inside a test host's
/// request pipeline) can't be attributed and are written to stderr.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public sealed class FailOnUnexpectedContainedFaultAttribute : BeforeAfterTestAttribute
{
    private static readonly AsyncLocal<List<string>?> Current = new();

    internal static void Record(Exception ex, string context)
    {
        var line = $"{context}: {ex.GetType().Name}: {ex.Message}";
        var list = Current.Value;
        if (list is null)
        {
            System.Console.Error.WriteLine("Contained engine fault outside any attributable test: " + line);
            return;
        }
        lock (list) list.Add(line);
    }

    public override void Before(MethodInfo methodUnderTest) => Current.Value = new List<string>();

    public override void After(MethodInfo methodUnderTest)
    {
        var list = Current.Value;
        Current.Value = null;
        if (list is null) return;
        string[] faults;
        lock (list) faults = list.ToArray();
        if (faults.Length == 0) return;
        if (methodUnderTest.GetCustomAttribute<ExpectsContainedFaultAttribute>() != null
            || methodUnderTest.DeclaringType?.GetCustomAttribute<ExpectsContainedFaultAttribute>() != null)
        {
            return;
        }
        throw new XunitException(
            "The engine reported contained fault(s) this test did not expect (mark it [ExpectsContainedFault] if intended):"
            + Environment.NewLine + string.Join(Environment.NewLine, faults));
    }
}

internal static class ContainedFaultHarness
{
    [ModuleInitializer]
    internal static void Install() => GameFacade.ContainedFaultReported += FailOnUnexpectedContainedFaultAttribute.Record;
}