using Xunit;

// Majik.Server.Tests spins up full WebApplicationFactory / TestAppFactory server
// hosts. Running its test collections in PARALLEL races shared process-global
// state across concurrent hosts and CRASHES the test host under low core counts
// — reproducible on CI's 2-core runners and locally via `taskset -c 0,1`, but
// hidden on fast multicore boxes (which is why it passed locally yet hung/aborted
// CI build-test for ~10min until the job timeout). When the host crashes, every
// in-flight/queued test reports "incomplete", so the failure looked like a hang.
//
// Serialize the collections: the suite still completes in ~20s and never crashes.
// (Same class of concurrent-shared-state race the per-game registry scoping in
// #751 addressed for the engine; the server host surface needs the same care.)
[assembly: CollectionBehavior(DisableTestParallelization = true)]
