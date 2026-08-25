var builder = DistributedApplication.CreateBuilder(args);

// Add postgresql database support
var postgres = builder
    .AddPostgres("postgres")
    .WithDataVolume("digitalbanking-postgres-data")
    .WithHostPort(5432);

var nx = builder
    .AddNxApp("web", "../Web")
    .WithNpm(install: true)
    .WithPackageManagerLaunch();

nx.AddApp("portal")
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WithMappedEndpointPort();


await builder.Build().RunAsync();
