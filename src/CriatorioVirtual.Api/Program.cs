var builder = WebApplication.CreateBuilder(args);
_ = BackendLayers.Types;
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();

public partial class Program;
