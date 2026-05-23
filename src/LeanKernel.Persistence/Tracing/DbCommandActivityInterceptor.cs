using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LeanKernel.Persistence.Tracing;

/// <summary>
/// Emits tracing activities for EF Core database commands.
/// </summary>
public sealed class DbCommandActivityInterceptor : DbCommandInterceptor
{
    private static readonly ActivitySource ActivitySource = new("LeanKernel.Persistence");
    private readonly ConcurrentDictionary<Guid, Activity> _activities = new();

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        StartActivity(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        StartActivity(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        StopActivity(eventData.CommandId, null);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        StopActivity(eventData.CommandId, null);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        StartActivity(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StartActivity(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        StopActivity(eventData.CommandId, null);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        StopActivity(eventData.CommandId, null);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        StartActivity(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        StartActivity(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override object ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object result)
    {
        StopActivity(eventData.CommandId, null);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<object> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object result,
        CancellationToken cancellationToken = default)
    {
        StopActivity(eventData.CommandId, null);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        StopActivity(eventData.CommandId, eventData.Exception);
    }

    /// <inheritdoc />
    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        StopActivity(eventData.CommandId, eventData.Exception);
        return Task.CompletedTask;
    }

    private void StartActivity(DbCommand command, CommandEventData eventData)
    {
        var activity = ActivitySource.StartActivity("DbCommand", ActivityKind.Client);
        if (activity is null)
        {
            return;
        }

        activity.SetTag("db.system", "postgresql");
        activity.SetTag("db.name", command.Connection?.Database);
        activity.SetTag("db.statement", command.CommandText);
        activity.SetTag("db.operation", command.CommandText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault());
        activity.SetTag("db.ef.command_id", eventData.CommandId.ToString());
        _activities[eventData.CommandId] = activity;
    }

    private void StopActivity(Guid commandId, Exception? exception)
    {
        if (!_activities.TryRemove(commandId, out var activity))
        {
            return;
        }

        if (exception is not null)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.RecordException(exception);
        }

        activity.Dispose();
    }
}
