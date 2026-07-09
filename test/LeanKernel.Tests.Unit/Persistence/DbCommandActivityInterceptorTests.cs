using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using FluentAssertions;
using LeanKernel.Persistence.Tracing;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LeanKernel.Tests.Unit.Persistence;

public sealed class DbCommandActivityInterceptorTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _startedActivities = [];
    private readonly List<Activity> _stoppedActivities = [];

    public DbCommandActivityInterceptorTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "LeanKernel.Persistence",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => _startedActivities.Add(a),
            ActivityStopped = a => _stoppedActivities.Add(a),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    [Fact]
    public void ReaderExecuting_starts_activity()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var command = new TestDbCommand();
        var eventData = new TestCommandEventData(Guid.NewGuid());

        interceptor.ReaderExecuting(command, eventData, default);

        _startedActivities.Should().ContainSingle(a =>
            a.OperationName == "DbCommand" &&
            a.Kind == ActivityKind.Client);
    }

    [Fact]
    public void ReaderExecutingAsync_starts_activity()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var command = new TestDbCommand();
        var eventData = new TestCommandEventData(Guid.NewGuid());

        interceptor.ReaderExecutingAsync(command, eventData, default);

        _startedActivities.Should().ContainSingle(a =>
            a.OperationName == "DbCommand" &&
            a.Kind == ActivityKind.Client);
    }

    [Fact]
    public void ReaderExecuted_stops_activity()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var command = new TestDbCommand();
        var commandId = Guid.NewGuid();

        interceptor.ReaderExecuting(command, new TestCommandEventData(commandId), default);
        interceptor.ReaderExecuted(command, new TestCommandExecutedEventData(commandId), TestDbDataReader.Instance);

        _stoppedActivities.Should().ContainSingle();
    }

    [Fact]
    public void ReaderExecutedAsync_stops_activity()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var command = new TestDbCommand();
        var commandId = Guid.NewGuid();

        interceptor.ReaderExecutingAsync(command, new TestCommandEventData(commandId), default);
        interceptor.ReaderExecutedAsync(command, new TestCommandExecutedEventData(commandId), TestDbDataReader.Instance);

        _stoppedActivities.Should().ContainSingle();
    }

    [Fact]
    public void NonQuery_Executing_and_Executed_create_and_stop_activity()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var command = new TestDbCommand();
        var commandId = Guid.NewGuid();

        interceptor.NonQueryExecuting(command, new TestCommandEventData(commandId), default);
        interceptor.NonQueryExecuted(command, new TestCommandExecutedEventData(commandId), 0);

        _startedActivities.Should().ContainSingle(a => a.OperationName == "DbCommand");
        _stoppedActivities.Should().ContainSingle();
    }

    [Fact]
    public void NonQueryExecutingAsync_and_ExecutedAsync_create_and_stop_activity()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var command = new TestDbCommand();
        var commandId = Guid.NewGuid();

        interceptor.NonQueryExecutingAsync(command, new TestCommandEventData(commandId), default);
        interceptor.NonQueryExecutedAsync(command, new TestCommandExecutedEventData(commandId), 0);

        _startedActivities.Should().ContainSingle(a => a.OperationName == "DbCommand");
        _stoppedActivities.Should().ContainSingle();
    }

    [Fact]
    public void Scalar_Executing_and_Executed_create_and_stop_activity()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var command = new TestDbCommand();
        var commandId = Guid.NewGuid();

        interceptor.ScalarExecuting(command, new TestCommandEventData(commandId), default);
        interceptor.ScalarExecuted(command, new TestCommandExecutedEventData(commandId), 42);

        _startedActivities.Should().ContainSingle(a => a.OperationName == "DbCommand");
        _stoppedActivities.Should().ContainSingle();
    }

    [Fact]
    public void CommandFailed_sets_error_status_and_tags()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var command = new TestDbCommand();
        var commandId = Guid.NewGuid();
        var exception = new InvalidOperationException("Something went wrong");

        interceptor.ReaderExecuting(command, new TestCommandEventData(commandId), default);
        interceptor.CommandFailed(command, new TestCommandErrorEventData(commandId, exception));

        var activity = _stoppedActivities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem("exception.type").Should().Be("System.InvalidOperationException");
        activity.GetTagItem("exception.message").Should().Be("Something went wrong");
    }

    [Fact]
    public void CommandFailedAsync_sets_error_status_and_tags()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var command = new TestDbCommand();
        var commandId = Guid.NewGuid();
        var exception = new InvalidOperationException("Async error");

        interceptor.ReaderExecuting(command, new TestCommandEventData(commandId), default);
        interceptor.CommandFailedAsync(command, new TestCommandErrorEventData(commandId, exception));

        var activity = _stoppedActivities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem("exception.type").Should().Be("System.InvalidOperationException");
        activity.GetTagItem("exception.message").Should().Be("Async error");
    }

    [Fact]
    public void StopActivity_with_unknown_commandId_does_not_throw()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var command = new TestDbCommand();
        var commandId = Guid.NewGuid();

        interceptor.ReaderExecuting(command, new TestCommandEventData(commandId), default);
        var unknownId = Guid.NewGuid();
        var act = () => interceptor.ReaderExecuted(command, new TestCommandExecutedEventData(unknownId), TestDbDataReader.Instance);

        act.Should().NotThrow();
        _stoppedActivities.Should().BeEmpty();
    }

    [Fact]
    public void Interceptor_does_not_modify_passed_interception_result()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var command = new TestDbCommand();
        var eventData = new TestCommandEventData(Guid.NewGuid());
        var originalResult = new InterceptionResult<DbDataReader>();

        var result = interceptor.ReaderExecuting(command, eventData, originalResult);

        result.Should().Be(originalResult);
    }

    [Fact]
    public void Activity_tags_are_set_correctly()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var connection = new TestDbConnection { DatabaseName = "mydb" };
        using var command = new TestDbCommand
        {
            CommandText = "SELECT * FROM users WHERE id = @p1",
            Connection = connection,
        };
        var commandId = Guid.NewGuid();

        interceptor.ReaderExecuting(command, new TestCommandEventData(commandId), default);

        var activity = _startedActivities.Should().ContainSingle().Subject;
        activity.GetTagItem("db.system").Should().Be("postgresql");
        activity.GetTagItem("db.name").Should().Be("mydb");
        activity.GetTagItem("db.statement").Should().Be("SELECT * FROM users WHERE id = @p1");
        activity.GetTagItem("db.operation").Should().Be("SELECT");
        activity.GetTagItem("db.ef.command_id").Should().Be(commandId.ToString());
    }

    [Fact]
    public void db_operation_uses_first_word_of_command_text()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var command = new TestDbCommand { CommandText = "WITH cte AS (...) SELECT * FROM t" };
        var commandId = Guid.NewGuid();

        interceptor.ReaderExecuting(command, new TestCommandEventData(commandId), default);

        var activity = _startedActivities.Should().ContainSingle().Subject;
        activity.GetTagItem("db.operation").Should().Be("WITH");
    }

    [Fact]
    public void Multiple_concurrent_commands_create_separate_activities()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var command1 = new TestDbCommand { CommandText = "SELECT 1" };
        using var command2 = new TestDbCommand { CommandText = "SELECT 2" };
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        interceptor.ReaderExecuting(command1, new TestCommandEventData(id1), default);
        interceptor.ReaderExecuting(command2, new TestCommandEventData(id2), default);

        _startedActivities.Should().HaveCount(2);

        interceptor.ReaderExecuted(command1, new TestCommandExecutedEventData(id1), TestDbDataReader.Instance);
        interceptor.ReaderExecuted(command2, new TestCommandExecutedEventData(id2), TestDbDataReader.Instance);

        _stoppedActivities.Should().HaveCount(2);
    }

    [Fact]
    public void db_name_tag_is_null_when_connection_is_null()
    {
        var interceptor = new DbCommandActivityInterceptor();
        using var command = new TestDbCommand { CommandText = "SELECT 1" };
        var commandId = Guid.NewGuid();

        interceptor.ReaderExecuting(command, new TestCommandEventData(commandId), default);

        var activity = _startedActivities.Should().ContainSingle().Subject;
        activity.GetTagItem("db.name").Should().BeNull();
    }

#pragma warning disable CS8765
    private sealed class TestCommandEventData : CommandEventData
    {
        public override Guid CommandId { get; }

        public TestCommandEventData(Guid commandId)
            : base(null!, (_, _) => "", null!, null!, null!, null!, default, commandId, default, default, default, default, default)
        {
            CommandId = commandId;
        }
    }

    private sealed class TestCommandExecutedEventData : CommandExecutedEventData
    {
        public override Guid CommandId { get; }

        public TestCommandExecutedEventData(Guid commandId)
            : base(null!, (_, _) => "", null!, null!, null!, null!, default, commandId, default, null!, default, default, default, default, default)
        {
            CommandId = commandId;
        }
    }

    private sealed class TestCommandErrorEventData : CommandErrorEventData
    {
        public override Guid CommandId { get; }
        public override Exception Exception { get; }

        public TestCommandErrorEventData(Guid commandId, Exception exception)
            : base(null!, (_, _) => "", null!, null!, null!, null!, default, commandId, default, exception, default, default, default, default, default)
        {
            CommandId = commandId;
            Exception = exception;
        }
    }

    private sealed class TestDbCommand : DbCommand
    {
        public override string CommandText { get; set; } = "SELECT 1";
        public new DbConnection? Connection { get; set; }
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection
        {
            get => Connection;
            set => Connection = value;
        }

        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => 1;
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => TestDbDataReader.Instance;
        public override void Prepare() { }
        public override void Cancel() { }

        protected override DbParameter CreateDbParameter() =>
            throw new NotSupportedException();

        protected override DbParameterCollection DbParameterCollection =>
            throw new NotSupportedException();

        protected override DbTransaction? DbTransaction
        {
            get => null;
            set { }
        }
    }

    private sealed class TestDbConnection : DbConnection
    {
        public string DatabaseName { get; set; } = "";
        public override string Database => DatabaseName;
        public override string ConnectionString { get; set; } = "";
        public override string DataSource => ":memory:";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() =>
            throw new NotSupportedException();
    }

    private sealed class TestDbDataReader : DbDataReader
    {
        public static readonly TestDbDataReader Instance = new();

        public override bool HasRows => false;
        public override bool IsClosed => false;
        public override int RecordsAffected => 0;
        public override int FieldCount => 0;
        public override object this[int ordinal] => throw new NotSupportedException();
        public override object this[string name] => throw new NotSupportedException();
        public override int Depth => 0;

        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
            throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
            throw new NotSupportedException();
        public override string GetDataTypeName(int ordinal) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override Type GetFieldType(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override int GetInt32(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override string GetName(int ordinal) => throw new NotSupportedException();
        public override int GetOrdinal(string name) => throw new NotSupportedException();
        public override string GetString(int ordinal) => throw new NotSupportedException();
        public override object GetValue(int ordinal) => throw new NotSupportedException();
        public override int GetValues(object[] values) => throw new NotSupportedException();
        public override bool IsDBNull(int ordinal) => throw new NotSupportedException();
        public override bool NextResult() => false;
        public override bool Read() => false;
        public override IEnumerator GetEnumerator() => new List<object>().GetEnumerator();
    }
#pragma warning restore CS8765
}
