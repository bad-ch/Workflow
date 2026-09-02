using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Workflow.Application.Interfaces;
using Workflow.Application.Services;
using Workflow.Persistence.Options;
using Workflow.Persistence.Services;
var builder = WebApplication.CreateBuilder(args);

// ── MVC & OpenAPI ─────────────────────────────────────────────────────────────
builder.Services.AddControllers().AddNewtonsoftJson();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// ── Options ───────────────────────────────────────────────────────────────────
builder.Services.Configure<WorkflowStorageOptions>(
    builder.Configuration.GetSection(WorkflowStorageOptions.SectionName));

builder.Services.Configure<WorkflowSecurityOptions>(
    builder.Configuration.GetSection(WorkflowSecurityOptions.SectionName));

// ── Application services ──────────────────────────────────────────────────────
builder.Services.AddSingleton<IWorkflowRepository, JsonWorkflowRepository>();
builder.Services.AddSingleton<IRunRepository, JsonRunRepository>();
builder.Services.AddSingleton<IWorkflowQueue, WorkflowQueue>();
builder.Services.AddSingleton<IUrlPolicy, UrlPolicy>();
builder.Services.AddSingleton<ICertificateInspector, CertificateInspector>();
builder.Services.AddSingleton<IWorkflowRunner, WorkflowRunner>();

// ── Background workers ────────────────────────────────────────────────────────
builder.Services.AddHostedService<WorkflowWorker>();
builder.Services.AddHostedService<ScheduleWorker>();

// ── HTTP client ───────────────────────────────────────────────────────────────
builder.Services
    .AddHttpClient("workflow", client =>
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Doedel-Workflow/1.0");
        client.MaxResponseContentBufferSize = 10 * 1024 * 1024;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        UseCookies = false
    });

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
    options.AddPolicy("frontend", policy =>
        policy
            .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
            .AllowAnyHeader()
            .AllowAnyMethod()));

// ── Problem details ───────────────────────────────────────────────────────────
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = ctx =>
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var problemDetails = context.RequestServices.GetRequiredService<IProblemDetailsService>();

        context.Response.StatusCode = 500;

        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = 500,
                Title = "Unexpected error",
                Detail = "An unexpected error occurred.",
                Extensions = { ["code"] = "unexpected_error" }
            }
        });
    }));

app.UseHttpsRedirection();
app.UseCors("frontend");

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program;

