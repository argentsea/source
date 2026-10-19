---
title: Batches and transactions
description: Where ArgentSea puts transactions, and how a batch runs several steps, including bulk loads, in one client transaction on one database or shard.
---

# Batches and transactions

ArgentSea runs almost everything as one command with no client transaction. A **batch** is the exception: an ordered set of steps that share one connection and one transaction, so that a bulk load and the SQL that consumes it commit or roll back together.

## Where transactions belong

A transaction inside a stored procedure or statement file begins and ends on the server in a single round trip. A client transaction opened from .NET holds its locks across every network hop until the client commits, which is slower and blocks other sessions for longer. ArgentSea therefore takes a clear stance: put the transaction in the SQL. Every ordinary data method (`RunAsync`, `MapReaderAsync` and the rest) sends one command and never calls `BeginTransaction`.

The batch is the **only place ArgentSea opens a client transaction**, and it exists for work that cannot be one command: combining a non-query operation such as SQL Server bulk copy or PostgreSQL `COPY` with the queries that use the loaded rows, or running a sequence assembled at run time. Each step is a round trip, so a batch is not a substitute for a multi-statement procedure.

## Three kinds of batch

| Type | Runs on | Returns |
| --- | --- | --- |
| `DatabaseBatch<TResult>` | one non-sharded database | a `TResult` |
| `ShardBatch<TResult>` | one shard | a `TResult`: a value, a list, a model, or one or more `ShardKey`s |
| `ShardSetBatch` | every targeted shard of a shard set | nothing |

All three run their steps **in order on one connection**: the transaction is committed when the last step succeeds and rolled back when any step throws. For a database or shard batch, the result is the **last step that produced a value**; steps that return nothing (or `null`/default) are skipped, so put the step whose result you want last, or declare `TResult` as `object` when you need no result.

A `ShardSetBatch` runs an **independent transaction on each shard**, concurrently. There is **no distributed transaction**: one shard can commit while another fails, and ArgentSea surfaces every shard's failure once all have settled, several as one `AggregateException`. Design shard-set batches to be idempotent, or be ready to compensate.

## Building a batch

Batches have a fluent `Add` API. The two basic steps execute a Query without reading a result:

```csharp
var batch = new ShardSetBatch()
    .Add(Queries.ArchiveClosedSessions, prms)   // query + parameters
    .Add(Queries.RebuildSessionStats);          // query, no parameters
await _shardSet.Write.RunAsync(batch, cancellation);
```

Extension methods in `ArgentSea` add steps that *do* read a result, chosen by the batch's `TResult`:

- **A column value**: `new DatabaseBatch<long>().Add(Queries.CreateInvoice, prms, "InvoiceId")` returns the named column from the first row; `DatabaseBatch<List<long>>` collects it from every row.
- **A new shard key**: `new ShardBatch<ShardKey<long>>().Add(Queries.CreateOrder, prms, "OrderId")` builds a key whose shard id is the shard the batch runs on; `.Add(query, prms, "ShardId", "OrderId")` reads the shard id from a column instead, and `ShardKey<long, short>` results take a child column too. `ShardBatch<List<ShardKey<long>>>` returns one key per row.
- **A custom handler**: `.Add(query, prms, handler)` runs a `QueryResultModelHandler<TArg, TModel>` delegate against the reader, for any result shape.

Pass `ShardsValues` to `shardSet.Write.RunAsync(batch, shards, cancellation)` to limit a shard-set batch to some shards.

## Custom steps: bulk copy and COPY

A step is any subclass of `BatchStep<TResult>`. Its `ExecuteAsync` receives the open connection and the live transaction, which is how non-query work joins the transaction. ArgentSea.Pg ships this for PostgreSQL in `CopyMapper`: `batch.Add(models, "table_name")` creates a temporary table from the model's mapping attributes (dropped on commit; use `schema.table` to target a real table), then streams the list with binary `COPY`. On SQL Server, write a small step around `SqlBulkCopy`:

```csharp
public class OrderItemsBulkCopyStep<TResult> : BatchStep<TResult>
{
    private readonly DataTable _items;   // columns match #OrderItems
    public OrderItemsBulkCopyStep(DataTable items) => _items = items;

    protected override async Task<TResult> ExecuteAsync(short shardId, DbConnection connection,
        DbTransaction transaction, string connectionName, IDataProviderServiceFactory services,
        ILogger logger, CancellationToken cancellation)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "CREATE TABLE #OrderItems (ProductId int, Quantity int, UnitPrice money);";
            await cmd.ExecuteNonQueryAsync(cancellation);
        }
        using var copy = new SqlBulkCopy((SqlConnection)connection, SqlBulkCopyOptions.Default, (SqlTransaction)transaction)
        {
            DestinationTableName = "#OrderItems"
        };
        await copy.WriteToServerAsync(_items, cancellation);
        return default;
    }
}
```

A local temporary table created on the batch's connection stays visible to the later steps, so the next step's procedure can read it.

## Example: an order and its items in one transaction

Stage the line items with a bulk load, then run one query that inserts the order header from parameters, inserts the items from the staging table, and returns the new order id. The batch returns that id as a `ShardKey<long>` stamped with the shard it ran on.

::: code-group
```csharp [SQL Server]
public async Task<ShardKey<long>> CreateOrder(short tenantId, Order order, DataTable items, CancellationToken cancellation)
{
    var prms = new ParameterCollection()
        .CreateInputParameters(order, _logger);          // @CustomerId, @OrderDate, ...
    var batch = new ShardBatch<ShardKey<long>>()
        .Add(new OrderItemsBulkCopyStep<ShardKey<long>>(items))
        .Add(Queries.CreateOrderFromStaging, prms, "OrderId");   // INSERT ... FROM #OrderItems; SELECT OrderId
    return await _shardSet[tenantId].Write.RunAsync(batch, cancellation);
}
```
```csharp [PostgreSQL]
public async Task<ShardKey<long>> CreateOrder(short tenantId, Order order, List<OrderItem> items, CancellationToken cancellation)
{
    var prms = new ParameterCollection()
        .CreateInputParameters(order, _logger);          // customer_id, order_date, ...
    var batch = new ShardBatch<ShardKey<long>>()
        .Add(items, "order_items_staging")               // COPY into a temp table (CopyMapper)
        .Add(Queries.CreateOrderFromStaging, prms, "order_id");   // INSERT ... FROM order_items_staging RETURNING order_id
    return await _shardSet[tenantId].Write.RunAsync(batch, cancellation);
}
```
:::

If the second step fails, the staged rows and the header are rolled back together. On a non-sharded database the shape is the same with `DatabaseBatch<long>` and `_database.Write.RunAsync(batch, cancellation)`, returning the plain `long`.

::: tip One procedure is usually better
If the items fit a table-valued parameter (SQL Server) or an array parameter (PostgreSQL), a single procedure that takes both the header and the items needs no batch at all, and runs in one round trip with a server-side transaction. Reach for the batch when the data is too large for a parameter, or the sequence is only known at run time.
:::

## Running and cancelling

Execute with the write connection of the target: `database.Write.RunAsync(batch, cancellation)`, `shardSet[shardId].Write.RunAsync(batch, cancellation)`, or `shardSet.Write.RunAsync(shardSetBatch, cancellation)`. The whole batch runs under the connection's [resilience policy](/guides/resilience), so a transient failure retries the batch from the beginning on a fresh transaction; steps should tolerate being re-run after a rollback.

The `CancellationToken` is checked before each step and passed to every command (including a `COPY` or bulk copy), so cancelling mid-batch stops the current command, throws `OperationCanceledException`, and rolls the transaction back. Each step's start and end is logged, which makes a slow step easy to find.

**Next:** [Orleans](/guides/orleans)
