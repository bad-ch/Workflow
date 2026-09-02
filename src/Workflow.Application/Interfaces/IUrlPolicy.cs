namespace Workflow.Application.Interfaces;
public interface IUrlPolicy { Task ValidateAsync(Uri uri, CancellationToken cancellationToken); }
