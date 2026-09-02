using Workflow.Application.Models;
namespace Workflow.Application.Services;
public static class WorkflowValidator
{
    public static IReadOnlyDictionary<string,string[]> Validate(CreateWorkflowRequest request) => ValidateCore(request.Name, request.Trigger, request.Schedule, request.Steps);
    public static IReadOnlyDictionary<string,string[]> Validate(UpdateWorkflowRequest request) => ValidateCore(request.Name, request.Trigger, request.Schedule, request.Steps);
    private static Dictionary<string,string[]> ValidateCore(string name, TriggerDefinition trigger, ScheduleDefinition? schedule, IReadOnlyList<WorkflowStep> steps)
    {
        var errors = new Dictionary<string,List<string>>();
        void Add(string key,string value) { if(!errors.TryGetValue(key,out var list)) errors[key]=list=[]; list.Add(value); }
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) Add("name", "Name is required and must not exceed 200 characters.");
        if (steps.Count is < 1 or > 100) Add("steps", "A workflow needs 1 to 100 top-level steps.");
        if (trigger.Type == TriggerType.Schedule && schedule is null) Add("schedule", "A schedule trigger requires schedule settings.");
        if (schedule is not null && schedule.IntervalSeconds < 10) Add("schedule.intervalSeconds", "Minimum interval is 10 seconds.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(steps, "steps");
        return errors.ToDictionary(x => x.Key, x => x.Value.ToArray());
        void Walk(IEnumerable<WorkflowStep> items, string path)
        {
            foreach (var step in items)
            {
                if (string.IsNullOrWhiteSpace(step.Id) || !System.Text.RegularExpressions.Regex.IsMatch(step.Id, "^[A-Za-z][A-Za-z0-9_-]{0,63}$")) Add(path, "Each step needs a safe, unique id.");
                else if (!ids.Add(step.Id)) Add(path, $"Duplicate step id '{step.Id}'.");
                switch(step)
                {
                    case HttpRequestStep h when h.TimeoutSeconds is < 1 or > 300: Add(path, $"HTTP step '{h.Id}' timeout must be 1..300 seconds."); break;
                    case DelayStep d when d.Milliseconds is < 0 or > 86400000: Add(path, $"Delay step '{d.Id}' must be 0..86400000 ms."); break;
                    case LoopStep l:
                        if (l.MaxIterations is < 1 or > 1000) Add(path, $"Loop '{l.Id}' maxIterations must be 1..1000.");
                        Walk(l.Steps, $"{path}.{l.Id}"); break;
                    case ConditionStep c:
                        Walk(c.Then, $"{path}.{c.Id}.then"); if(c.Else is not null) Walk(c.Else, $"{path}.{c.Id}.else"); break;
                }
            }
        }
    }
}
