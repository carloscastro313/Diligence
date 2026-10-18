using System.Data;
using System.Diagnostics;
using Diligencia.IntegrationTests.Support;
using Diligencia.Modules.Auditoria.Features.QueryEvents;
using Diligencia.Modules.Auditoria.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.IntegrationTests.Auditoria;

// SC-007: consulta por entidad < 2 s con 1.000.000 de eventos. Base propia, no la de QueryEventsTests.
[Trait("Category", "Performance")]
public class QueryPerformanceTests(SqlServerFixture sql)
{
    private const string Db = "diligencia_perf";
    private const int Total = 1_000_000;

    [Fact]
    public async Task QueryByEntity_With1MillionEvents_RespondsUnderTwoSeconds()
    {
        var ct = TestContext.Current.CancellationToken;
        var migrator = sql.For("diligencia_migrator", SqlServerFixture.MigratorPassword, Db);
        var runtime = sql.For("diligencia_app", SqlServerFixture.RuntimePassword, Db);
        await SqlServerFixture.MigrateAsync(new Dictionary<string, string?>
        {
            ["ConnectionStrings:diligencia"] = migrator,
            ["ConnectionStrings:diligencia-runtime"] = runtime,
            ["ConnectionStrings:diligencia-admin"] = sql.AdminConnectionString,
        });

        await using (var c = new SqlConnection(migrator))
        {
            await c.OpenAsync(ct);
            using var bulk = new SqlBulkCopy(c) { DestinationTableName = "auditoria.AuditEvents", BulkCopyTimeout = 600, BatchSize = 50_000 };
            foreach (var col in new[] { "OccurredAtUtc", "ActorId", "Surface", "Action", "EntityType", "EntityId", "CorrelationId" })
                bulk.ColumnMappings.Add(col, col);
            await bulk.WriteToServerAsync(new EventReader(Total), ct);
        }

        var options = new DbContextOptionsBuilder<AuditDbContext>().UseSqlServer(runtime).Options;
        await using var db = new AuditDbContext(options);
        var handler = new QueryEventsHandler(db);
        var request = new QueryEventsRequest("Tercero", "12345", null, null, null, 1, 50);

        await handler.HandleAsync(request, ct); // calienta el plan
        var sw = Stopwatch.StartNew();
        var page = await handler.HandleAsync(request, ct);
        sw.Stop();

        Assert.NotEmpty(page.Items);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"La consulta tardó {sw.Elapsed.TotalMilliseconds:F0} ms.");
    }

    // 1.000.000 de filas repartidas en 100.000 entidades; sin cargarlas todas en memoria.
    private sealed class EventReader(int total) : IDataReader
    {
        private static readonly string[] Names = ["OccurredAtUtc", "ActorId", "Surface", "Action", "EntityType", "EntityId", "CorrelationId"];
        private int _i = -1;
        private static readonly DateTime Start = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public bool Read() => ++_i < total;
        public int FieldCount => Names.Length;
        public string GetName(int i) => Names[i];
        public int GetOrdinal(string name) => Array.IndexOf(Names, name);
        public object GetValue(int i) => i switch
        {
            0 => Start.AddSeconds(_i),
            1 => "user-" + (_i % 500),
            2 => "Backoffice",
            3 => "Updated",
            4 => "Tercero",
            5 => (_i % 100_000).ToString(),
            _ => "perf",
        };
        public bool IsDBNull(int i) => false;
        public int Depth => 0;
        public bool IsClosed => false;
        public int RecordsAffected => -1;
        public void Close() { }
        public void Dispose() { }
        public bool NextResult() => false;
        public DataTable GetSchemaTable() => throw new NotSupportedException();
        public object this[int i] => GetValue(i);
        public object this[string name] => GetValue(GetOrdinal(name));
        public int GetValues(object[] values) { for (var i = 0; i < FieldCount; i++) values[i] = GetValue(i); return FieldCount; }
        public bool GetBoolean(int i) => throw new NotSupportedException();
        public byte GetByte(int i) => throw new NotSupportedException();
        public long GetBytes(int i, long o, byte[]? b, int bo, int l) => throw new NotSupportedException();
        public char GetChar(int i) => throw new NotSupportedException();
        public long GetChars(int i, long o, char[]? b, int bo, int l) => throw new NotSupportedException();
        public IDataReader GetData(int i) => throw new NotSupportedException();
        public string GetDataTypeName(int i) => throw new NotSupportedException();
        public DateTime GetDateTime(int i) => (DateTime)GetValue(i);
        public decimal GetDecimal(int i) => throw new NotSupportedException();
        public double GetDouble(int i) => throw new NotSupportedException();
        public Type GetFieldType(int i) => i == 0 ? typeof(DateTime) : typeof(string);
        public float GetFloat(int i) => throw new NotSupportedException();
        public Guid GetGuid(int i) => throw new NotSupportedException();
        public short GetInt16(int i) => throw new NotSupportedException();
        public int GetInt32(int i) => throw new NotSupportedException();
        public long GetInt64(int i) => throw new NotSupportedException();
        public string GetString(int i) => (string)GetValue(i);
    }
}
