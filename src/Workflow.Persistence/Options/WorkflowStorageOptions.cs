namespace Workflow.Persistence.Options;
public sealed class WorkflowStorageOptions { public const string SectionName = "WorkflowStorage"; public string RootPath { get; init; } = "data"; }
public sealed class WorkflowSecurityOptions { public const string SectionName = "WorkflowSecurity"; public string[] AllowedHosts { get; init; } = []; public bool AllowHttp { get; init; } }
