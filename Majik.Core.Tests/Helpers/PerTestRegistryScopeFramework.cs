using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Majik.Core.Game;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Majik.Core.Tests.Helpers;

// ---------------------------------------------------------------------------
// Per-test ambient-registry isolation, assembly-wide, WITHOUT touching any of
// the ~1,800 test classes.
//
// Why this exists
// ---------------
// PR #1704 de-static-ed the four player-keyed registries (AgentRegistry,
// ZoneServiceRegistry, GameRandomRegistry, EventBusRegistry) plus — in this
// PR — CastingRestrictions onto a per-game AsyncLocal ambient store
// (AmbientRegistryStore<T> / GameRegistryScope). LIVE games install a scope at
// game start, so concurrent matches are already isolated.
//
// But the BULK of the unit suite never starts a game: it constructs effects /
// agents / zone services / restriction rails directly. With no scope
// installed, every one of those calls resolves the registry's PROCESS-WIDE
// FALLBACK store. Under xUnit's default parallel collections, two test
// classes running concurrently then read/write the SAME fallback — one
// class's Clear() wipes an agent another just registered, a restriction
// leaks across cases, an RNG seed is read from the wrong store. That race is
// exactly why the assembly was pinned to DisableTestParallelization.
//
// The fix: push a FRESH ambient scope (GameRegistryScope.PushForGame, which
// covers all five registries) around EVERY test case and dispose it after.
// Each test then sees its own private set of stores — direct-construction
// tests can no longer cross-contaminate, so the suite is safe to run with
// parallel collections re-enabled.
//
// How it hooks in
// ---------------
// xUnit 2.x has no assembly-level "before/after each test" attribute
// (BeforeAfterTestAttribute is class/method only) and a setup pushed in a
// class ctor / IClassFixture would require editing every class. The only
// assembly-wide, zero-per-class hook is a custom test framework: we subclass
// the standard Xunit runner chain down to the test-method runner and wrap each
// test case's invocation in `using (GameRegistryScope.PushForGame())`. The
// scope is an AsyncLocal, so it flows into the awaited test body (sync and
// async tests alike) and is torn down when the case completes — including each
// individual [Theory] data row, which xUnit treats as its own test case.
//
// Registered via [assembly: TestFramework(...)] in AssemblyAttributes.cs.
// ---------------------------------------------------------------------------

/// <summary>
/// Custom xUnit test framework that installs a fresh per-game registry scope
/// (<see cref="GameRegistryScope.PushForGame"/>) around every test case, so
/// direct-construction tests get an isolated ambient store and the suite can
/// run with parallel collections enabled. See file header for rationale.
/// </summary>
public sealed class PerTestRegistryScopeFramework : XunitTestFramework
{
    public PerTestRegistryScopeFramework(IMessageSink messageSink)
        : base(messageSink)
    {
    }

    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
        => new ScopedExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    private sealed class ScopedExecutor : XunitTestFrameworkExecutor
    {
        public ScopedExecutor(
            AssemblyName assemblyName,
            ISourceInformationProvider sourceInformationProvider,
            IMessageSink diagnosticMessageSink)
            : base(assemblyName, sourceInformationProvider, diagnosticMessageSink)
        {
        }

        protected override async void RunTestCases(
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions)
        {
            using var runner = new ScopedAssemblyRunner(
                TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions);
            await runner.RunAsync();
        }
    }

    private sealed class ScopedAssemblyRunner : XunitTestAssemblyRunner
    {
        public ScopedAssemblyRunner(
            ITestAssembly testAssembly,
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink diagnosticMessageSink,
            IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions)
            : base(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
        {
        }

        protected override Task<RunSummary> RunTestCollectionAsync(
            IMessageBus messageBus,
            ITestCollection testCollection,
            IEnumerable<IXunitTestCase> testCases,
            CancellationTokenSource cancellationTokenSource)
            => new ScopedCollectionRunner(
                    testCollection, testCases, DiagnosticMessageSink, messageBus,
                    TestCaseOrderer, new ExceptionAggregator(Aggregator), cancellationTokenSource)
                .RunAsync();
    }

    private sealed class ScopedCollectionRunner : XunitTestCollectionRunner
    {
        public ScopedCollectionRunner(
            ITestCollection testCollection,
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            ITestCaseOrderer testCaseOrderer,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
            : base(testCollection, testCases, diagnosticMessageSink, messageBus,
                   testCaseOrderer, aggregator, cancellationTokenSource)
        {
        }

        protected override Task<RunSummary> RunTestClassAsync(
            ITestClass testClass,
            IReflectionTypeInfo @class,
            IEnumerable<IXunitTestCase> testCases)
            => new ScopedClassRunner(
                    testClass, @class, testCases, DiagnosticMessageSink, MessageBus,
                    TestCaseOrderer, new ExceptionAggregator(Aggregator),
                    CancellationTokenSource, CollectionFixtureMappings)
                .RunAsync();
    }

    private sealed class ScopedClassRunner : XunitTestClassRunner
    {
        public ScopedClassRunner(
            ITestClass testClass,
            IReflectionTypeInfo @class,
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            ITestCaseOrderer testCaseOrderer,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource,
            IDictionary<System.Type, object> collectionFixtureMappings)
            : base(testClass, @class, testCases, diagnosticMessageSink, messageBus,
                   testCaseOrderer, aggregator, cancellationTokenSource, collectionFixtureMappings)
        {
        }

        protected override Task<RunSummary> RunTestMethodAsync(
            ITestMethod testMethod,
            IReflectionMethodInfo method,
            IEnumerable<IXunitTestCase> testCases,
            object[] constructorArguments)
            => new ScopedMethodRunner(
                    testMethod, Class, method, testCases, DiagnosticMessageSink, MessageBus,
                    new ExceptionAggregator(Aggregator), CancellationTokenSource, constructorArguments)
                .RunAsync();
    }

    private sealed class ScopedMethodRunner : XunitTestMethodRunner
    {
        public ScopedMethodRunner(
            ITestMethod testMethod,
            IReflectionTypeInfo @class,
            IReflectionMethodInfo method,
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource,
            object[] constructorArguments)
            : base(testMethod, @class, method, testCases, diagnosticMessageSink, messageBus,
                   aggregator, cancellationTokenSource, constructorArguments)
        {
        }

        // The single integration point: each test case (incl. every [Theory]
        // data row) runs inside a fresh ambient registry scope that flows into
        // the awaited test body and is reclaimed when the case finishes.
        protected override async Task<RunSummary> RunTestCaseAsync(IXunitTestCase testCase)
        {
            using (GameRegistryScope.PushForGame())
            {
                return await base.RunTestCaseAsync(testCase);
            }
        }
    }
}
