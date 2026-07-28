using Xunit;

// Each test class spins up its own real OPA subprocess (OpaTestServer). Running test
// classes in parallel would run several opa.exe processes concurrently, causing CPU
// contention severe enough to trip the policy client's timeout — serialize instead.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
