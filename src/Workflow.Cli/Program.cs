using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Workflow.Application.Interfaces;
using Workflow.Application.Models;
using Workflow.Application.Services;
using Workflow.Persistence.Options;
using Workflow.Persistence.Services;

// -- Host / DI -----------------------------------------------------------------
var host = Host.CreateApplicationBuilder(args);

host.Services.Configure<WorkflowStorageOptions>(
    host.Configuration.GetSection(WorkflowStorageOptions.SectionName));

host.Services.Configure<WorkflowSecurityOptions>(
    host.Configuration.GetSection(WorkflowSecurityOptions.SectionName));

host.Services.AddSingleton<IWorkflowRepository, JsonWorkflowRepository>();
host.Services.AddSingleton<IRunRepository, JsonRunRepository>();
host.Services.AddSingleton<IUrlPolicy, UrlPolicy>();
host.Services.AddSingleton<ICertificateInspector, CertificateInspector>();
host.Services.AddSingleton<IWorkflowRunner, WorkflowRunner>();

host.Services
    .AddHttpClient("workflow", client =>
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Workflow/1.0");
        client.MaxResponseContentBufferSize = 10 * 1024 * 1024;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        UseCookies = false
    });

var app = host.Build();

var repository = app.Services.GetRequiredService<IWorkflowRepository>();
var runner     = app.Services.GetRequiredService<IWorkflowRunner>();

Console.OutputEncoding = System.Text.Encoding.UTF8;
await Cli.RunAsync(repository, runner);

// -- CLI -----------------------------------------------------------------------
internal static class Cli
{
    public static async Task RunAsync(IWorkflowRepository repository, IWorkflowRunner runner)
    {
        PrintBanner();
        while (true)
        {
            PrintMenu();
            switch (Console.ReadLine()?.Trim().ToUpperInvariant())
            {
                case "1": await ListAsync(repository);            break;
                case "2": await RunPickAsync(repository, runner); break;
                case "3": await RunByIdAsync(repository, runner); break;
                case "Q": Bye(); return;
                default:  Warn("Enter 1, 2, 3 or Q.");            break;
            }
        }
    }

    // -- Screens ---------------------------------------------------------------

    private static async Task ListAsync(IWorkflowRepository repository)
    {
        var list = await repository.GetAllAsync(CancellationToken.None);
        Console.WriteLine();
        if (list.Count == 0) { Warn("No workflows found."); return; }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  {"#",-4}{"ID",-38}{"Trigger",-12}{"Enabled",-9}Name");
        Console.ResetColor();
        Separator(80);

        for (var i = 0; i < list.Count; i++)
        {
            var w    = list[i];
            var mark = w.Enabled ? "+" : "-";
            var col  = w.Enabled ? ConsoleColor.Green : ConsoleColor.DarkGray;
            Console.Write($"  {i + 1,-4}");
            Colored(ConsoleColor.DarkCyan, $"{w.Id,-38}");
            Colored(ConsoleColor.White,    $"{w.Trigger.Type,-12}");
            Colored(col,                   $"{mark,-9}");
            Console.WriteLine(w.Name);
        }
        Console.WriteLine();
    }

    private static async Task RunPickAsync(IWorkflowRepository repository, IWorkflowRunner runner)
    {
        var list = await repository.GetAllAsync(CancellationToken.None);
        if (list.Count == 0) { Warn("No workflows found."); return; }
        await ListAsync(repository);
        Console.Write("  Enter number: ");
        if (!int.TryParse(Console.ReadLine()?.Trim(), out var idx) || idx < 1 || idx > list.Count)
        { Warn("Invalid selection."); return; }
        await ExecuteAsync(runner, list[idx - 1]);
    }

    private static async Task RunByIdAsync(IWorkflowRepository repository, IWorkflowRunner runner)
    {
        Console.Write("  Workflow ID (GUID): ");
        if (!Guid.TryParse(Console.ReadLine()?.Trim(), out var id)) { Warn("Invalid GUID."); return; }
        var workflow = await repository.GetAsync(id, CancellationToken.None);
        if (workflow is null) { Warn($"Workflow {id} not found."); return; }
        await ExecuteAsync(runner, workflow);
    }

    private static async Task ExecuteAsync(IWorkflowRunner runner, WorkflowDefinition workflow)
    {
        Console.WriteLine();
        Console.Write("  Input JSON (Enter to skip): ");
        var raw = Console.ReadLine()?.Trim();

        JObject input;
        try   { input = string.IsNullOrWhiteSpace(raw) ? new JObject() : JObject.Parse(raw); }
        catch { Warn("Invalid JSON input."); return; }

        Console.WriteLine();
        Colored(ConsoleColor.Cyan,    "  > Running : "); Console.WriteLine(workflow.Name);
        Colored(ConsoleColor.DarkGray,"    Trigger : "); Console.WriteLine(workflow.Trigger.Type);
        Colored(ConsoleColor.DarkGray,"    Steps   : "); Console.WriteLine(workflow.Steps.Count);
        Separator(60);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            var run = await runner.RunAsync(Guid.NewGuid(), workflow, input, cts.Token);
            PrintRun(run);
        }
        catch (OperationCanceledException) { Warn("Run cancelled."); }
        catch (Exception ex)               { Error($"Run failed: {ex.Message}"); }
    }

    private static void PrintRun(WorkflowRun run)
    {
        Console.WriteLine();
        var ok    = run.Status == RunStatus.Succeeded;
        var icon  = ok ? "[OK]" : "[FAIL]";
        var color = ok ? ConsoleColor.Green : ConsoleColor.Red;
        var ms    = run.CompletedUtc.HasValue ? (run.CompletedUtc.Value - run.StartedUtc).TotalMilliseconds : 0;

        Colored(color, $"\n  {icon} {run.Status}");
        Colored(ConsoleColor.DarkGray, $"   run {run.Id}   {ms:F0} ms total\n");

        if (run.Error is not null) Error($"     {run.Error}");
        if (run.Steps.Count == 0) { Console.WriteLine(); return; }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  {"Step",-32}{"Status",-13}{"ms",6}");
        Console.ResetColor();
        Separator(55);

        foreach (var step in run.Steps)
        {
            var stepOk    = step.Status == RunStatus.Succeeded;
            var stepIcon  = stepOk ? "+" : "x";
            var stepColor = stepOk ? ConsoleColor.Green : ConsoleColor.Red;
            var stepMs    = (step.CompletedUtc - step.StartedUtc).TotalMilliseconds;

            Console.Write("  ");
            Colored(stepColor,           $"{stepIcon} ");
            Console.Write(               $"{step.StepId,-32}");
            Colored(stepColor,           $"{step.Status,-13}");
            Colored(ConsoleColor.DarkGray,$"{stepMs,6:F0}\n");

            if (step.Output is not null && step.Output.Type != JTokenType.Null)
                Colored(ConsoleColor.DarkGray, $"     -> {step.Output.ToString(Formatting.None)}\n");

            if (step.Error is not null)
                Error($"     -> {step.Error}");
        }
        Console.WriteLine();
    }

    // -- Helpers ---------------------------------------------------------------

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("  +=================================+");
        Console.WriteLine("  |   Octave Workflow  CLI          |");
        Console.WriteLine("  +=================================+");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintMenu()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n  +-------------------------------------+");
        Console.WriteLine("  |  1  List all workflows              |");
        Console.WriteLine("  |  2  Run workflow  (pick from list)  |");
        Console.WriteLine("  |  3  Run workflow  by ID             |");
        Console.WriteLine("  |  Q  Quit                            |");
        Console.WriteLine("  +-------------------------------------+");
        Console.ResetColor();
        Console.Write("  > ");
    }

    private static void Bye()      => Colored(ConsoleColor.DarkGray, "\n  Goodbye.\n\n");
    private static void Separator(int w) => Colored(ConsoleColor.DarkGray, "  " + new string('-', w) + "\n");

    private static void Colored(ConsoleColor c, string text)
    {
        Console.ForegroundColor = c;
        Console.Write(text);
        Console.ResetColor();
    }

    private static void Warn(string msg)  => Colored(ConsoleColor.Yellow, $"\n  [!] {msg}\n");
    private static void Error(string msg) => Colored(ConsoleColor.Red,    $"\n  [x] {msg}\n");
}
