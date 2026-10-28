using Sample.Api;
using Sample.Api.Configuration;
using Sample.Api.Observability;
using Sample.Application;
using Sample.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddLayeredConfiguration();
builder.AddObservability();
builder.Services
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();
app.UseApi();
await app.RunAsync();
