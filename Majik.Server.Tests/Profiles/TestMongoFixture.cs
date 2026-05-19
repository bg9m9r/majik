using Mongo2Go;
using MongoDB.Driver;

namespace Majik.Server.Tests.Profiles;

/// <summary>xUnit class fixture spinning an in-process embedded Mongo
/// for the duration of one test class. Each test gets a fresh database
/// name to avoid cross-test bleed; the runner cleans up at fixture
/// dispose time.</summary>
public sealed class TestMongoFixture : IDisposable
{
    private readonly MongoDbRunner _runner;
    public string ConnectionString { get; }

    public TestMongoFixture()
    {
        _runner = MongoDbRunner.Start(singleNodeReplSet: false);
        ConnectionString = _runner.ConnectionString;
    }

    public IMongoDatabase NewDatabase()
    {
        var name = "test-" + Guid.NewGuid().ToString("N");
        return new MongoClient(ConnectionString).GetDatabase(name);
    }

    public void Dispose() => _runner.Dispose();
}
