using System.Collections.Concurrent;
using System.Data;
using System.Text;
using ArgentSea.Orleans.Sql;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;

namespace ArgentSea.Orleans.Sql.Test;

public class ReminderTableTests
{
    private static DataTable ReminderTable()
    {
        var table = new DataTable();
        table.Columns.Add("GrainKey", typeof(byte[]));
        table.Columns.Add("GrainType", typeof(string));
        table.Columns.Add("ReminderName", typeof(string));
        table.Columns.Add("StartTime", typeof(DateTime));
        table.Columns.Add("Period", typeof(long));
        table.Columns.Add("Version", typeof(int));
        return table;
    }

    [Fact]
    [Trait("Spec", "blk-b9fca0761f3641f18aa98f1916ecef9b")]
    [Trait("Spec", "blk-7750a2740c1048cea01fdb839855bcf6")]
    public void ReadRowsHandler_ReadsEveryRowOfTheResult()
    {
        var table = ReminderTable();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 3; i++)
        {
            table.Rows.Add(Encoding.UTF8.GetBytes("key" + i), "mygrain", "reminder" + i, start.AddHours(i), TimeSpan.FromMinutes(i + 1).Ticks, i + 10);
        }
        var bag = new ConcurrentBag<ReminderEntry>();

        using var rdr = table.CreateDataReader();
        ArgentSeaOrleansReminderTable.ReadRowsHandler(null!, 0, "rdr.OrleansReminderReadRowsKeyV1", bag, rdr, null!, "test", NullLogger.Instance);

        bag.Should().HaveCount(3);
        bag.Select(e => e.ReminderName).Should().BeEquivalentTo(["reminder0", "reminder1", "reminder2"]);
        var second = bag.Single(e => e.ReminderName == "reminder1");
        second.GrainId.Type.ToString().Should().Be("mygrain");
        second.GrainId.Key.ToString().Should().Be("key1");
        second.StartAt.Should().Be(start.AddHours(1));
        second.Period.Should().Be(TimeSpan.FromMinutes(2));
        second.ETag.Should().Be("11");
    }

    [Fact]
    [Trait("Spec", "blk-7131c458eb9c4d35b5c2f516c54d01a6")]
    [Trait("Spec", "blk-b9fca0761f3641f18aa98f1916ecef9b")]
    public void ReadRowsHandler_AppendsAcrossInvocations()
    {
        // The data connection invokes the handler once per shard; each invocation appends to the shared bag.
        var bag = new ConcurrentBag<ReminderEntry>();
        foreach (var shard in new[] { "a", "b" })
        {
            var table = ReminderTable();
            table.Rows.Add(Encoding.UTF8.GetBytes(shard + "1"), "mygrain", shard + "1", DateTime.UtcNow, 1L, 1);
            table.Rows.Add(Encoding.UTF8.GetBytes(shard + "2"), "mygrain", shard + "2", DateTime.UtcNow, 1L, 1);
            using var rdr = table.CreateDataReader();
            ArgentSeaOrleansReminderTable.ReadRowsHandler(null!, 0, "rdr.OrleansReminderReadRangeRows1KeyV1", bag, rdr, null!, "test", NullLogger.Instance);
        }

        bag.Should().HaveCount(4);
    }

    [Fact]
    [Trait("Spec", "blk-b9fca0761f3641f18aa98f1916ecef9b")]
    public void ReadRowsHandler_EmptyResult_AddsNothing()
    {
        var bag = new ConcurrentBag<ReminderEntry>();
        using var rdr = ReminderTable().CreateDataReader();
        ArgentSeaOrleansReminderTable.ReadRowsHandler(null!, 0, "rdr.OrleansReminderReadRowsKeyV1", bag, rdr, null!, "test", NullLogger.Instance);
        bag.Should().BeEmpty();
    }
}
