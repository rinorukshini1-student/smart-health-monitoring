using Simulator.App;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
