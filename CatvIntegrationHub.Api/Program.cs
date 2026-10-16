using CatvIntegrationHub.Api;
using CatvIntegrationHub.Api.Middleware;
using CatvIntegrationHub.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<HscasOptions>(builder.Configuration.GetSection("Hscas"));
builder.Services.AddSingleton<HscasClient>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseRouting();
app.UseCors("AllowAngular");
// Handle failures inside CORS so browser clients can read error responses too.
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();