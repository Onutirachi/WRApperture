// Each WebApplicationFactory runs Program.cs, which sets Serilog's global (static) bootstrap logger;
// parallel test classes would race on freezing it ("entry point exited without building an IHost").
[assembly: CollectionBehavior(DisableTestParallelization = true)]
