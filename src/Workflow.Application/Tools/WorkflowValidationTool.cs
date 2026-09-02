using Workflow.Application.Diagnostics;

namespace Workflow.Application.Tools;

/// <summary>
/// Command-line tool for validating workflow JSON files.
/// Usage: dotnet run --project Workflow.Cli -- validate [path-to-workflows-directory]
/// </summary>
public static class WorkflowValidationTool
{
    public static int ValidateWorkflows(string? workflowsPath = null)
    {
        // Determine the path to validate
        workflowsPath ??= Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Workflow.Api", "data", "workflows");
        workflowsPath = Path.GetFullPath(workflowsPath);

        Console.WriteLine($"Validating workflow JSON files in: {workflowsPath}");
        Console.WriteLine(new string('-', 80));

        var reports = WorkflowJsonDiagnostics.AnalyzeWorkflowDirectory(workflowsPath);

        if (reports.Count == 0)
        {
            Console.WriteLine("No workflow files found.");
            return 1;
        }

        var validFiles = 0;
        var filesByStatus = new Dictionary<bool, List<DiagnosticReport>>();

        foreach (var report in reports)
        {
            var isValid = report.IsValid;
            if (!filesByStatus.ContainsKey(isValid))
                filesByStatus[isValid] = new();
            filesByStatus[isValid].Add(report);

            if (isValid) validFiles++;
        }

        // Display summary
        Console.WriteLine($"\nSummary:");
        Console.WriteLine($"  Total files: {reports.Count}");
        Console.WriteLine($"  ✓ Valid: {validFiles}");
        Console.WriteLine($"  ✗ Invalid: {reports.Count - validFiles}");

        // Display details for invalid files
        if (filesByStatus.TryGetValue(false, out var invalidReports) && invalidReports.Count > 0)
        {
            Console.WriteLine($"\nInvalid files:");
            foreach (var report in invalidReports)
            {
                Console.WriteLine(new string('-', 80));
                Console.WriteLine(report.ToString());
            }
        }

        // Display details for valid files (optional, can be verbose)
        if (filesByStatus.TryGetValue(true, out var validReports) && validReports.Count > 0)
        {
            Console.WriteLine($"\nValid files:");
            foreach (var report in validReports)
            {
                Console.WriteLine($"  ✓ {Path.GetFileName(report.FilePath)}");
            }
        }

        Console.WriteLine(new string('-', 80));
        return reports.All(r => r.IsValid) ? 0 : 1;
    }
}
