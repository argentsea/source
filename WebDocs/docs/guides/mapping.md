---
title: Mapping
description: How the Mapper turns attributes on model properties into parameters and results at compiled speed, and how it handles nulls, required columns, nested models, result-set lists, table-valued parameters and COPY.
---

# Mapping

The Mapper removes the ADO.NET boilerplate of creating parameters and reading columns, without taking over your SQL. You add an attribute to each property that maps to a database value, and the Mapper does the rest at the speed of hand-written code.

## What the Mapper does, and does not do

The Mapper maps model properties to and from four targets: query input parameters, output parameters, data-reader columns and, on SQL Server, the rows of a table-valued parameter. That is the whole job. It never generates SQL, never tracks changes, never manages identity and never lazy-loads relationships. Parameter creation, execution and result handling stay separate steps that work on standard ADO.NET types, so you can let the Mapper do any step, do it by hand, or mix the two.

## A mapped model

Each attribute names the parameter or column and fixes the database type. The property type must match that database type exactly (more on that below).

::: code-group
```csharp [SQL Server]
using ArgentSea.Sql;

public readonly record struct Customer
{
    [MapToSqlInt("@CustomerId", true)]
    public int CustomerId { get; init; }

    [MapToSqlNVarChar("@Name", 255)]
    public string Name { get; init; }

    [MapToSqlDecimal("@CreditLimit", 9, 2)]
    public decimal? CreditLimit { get; init; }

    [MapToSqlSmallInt("@StatusId")]
    public CustomerStatus Status { get; init; }   // enum : short

    [MapToSqlDateTime2("@Created")]
    public DateTime Created { get; init; }
}
```
```csharp [PostgreSQL]
using ArgentSea.Pg;

public readonly record struct Customer
{
    [MapToPgInteger("customer_id", true)]
    public int CustomerId { get; init; }

    [MapToPgVarchar("name", 255)]
    public string Name { get; init; }

    [MapToPgNumeric("credit_limit", 9, 2)]
    public decimal? CreditLimit { get; init; }

    [MapToPgSmallint("status_id")]
    public CustomerStatus Status { get; init; }   // enum : short

    [MapToPgTimestamp("created")]
    public DateTime Created { get; init; }
}
```
:::

The `true` on the key column is the `required` flag, explained below. Properties without an attribute are ignored. On SQL Server the `@` prefix is optional: the Mapper adds it for parameters and drops it for columns. On PostgreSQL names are used exactly as written, so match your column case. Each property has one name, so name the parameter the same as the column it carries.

## Classes or structs

A model may be a class or a struct. The Mapper needs only a parameterless constructor and properties it can set, and `init` accessors qualify, so the examples here use `readonly record struct`: immutable, compared by value, and without a heap allocation per row. Three things change when a model is a struct. There is no null, so a single-row method whose record does not exist returns `default`; test for it with `== default`, or with `Key.IsEmpty` on a keyed model. A row whose required column is null is not dropped from a list the way it is for a class; it arrives as `default` and you filter it yourself. And a property marked `[MapToModel]` must still be a class, because nested models are flattened by reference. Two places call for a class regardless: inheritance, which structs do not have, and Orleans grain state, where the provider tells a missing record from a found one by a null result.

## Performance

Attributes can only be read by reflection, which is far too slow for every row of every query. The Mapper reflects over a model type once per purpose (input parameters, output parameters, reader rows, and so on), builds an expression tree from the metadata, compiles it to a delegate and caches that delegate per process. Every later call runs compiled code with no reflection.

The first use of each model type therefore costs reflection plus compilation, typically under a second, and the cost recurs after each process restart. Because compiled delegates are opaque to the debugger, the Mapper logs a rendering of the generated code at `Debug` level when it compiles, and each property it visits at `Trace` level. Turn on `Debug` logging when a mapping does not behave as you expect.

## Strict about types, tolerant about presence

The Mapper never casts. A `long` property must map to a `bigint` attribute, a `decimal` to `decimal`/`numeric`, an enum with a `short` base type to a `smallint`. A mismatch throws immediately, which is deliberate: silent conversions hide truncation and schema drift.

Presence is the opposite. A mapped column that is missing from a result set, or a mapped output parameter that is missing from the collection, is skipped and logged at `Debug`. Column positions are resolved by name once per result set and then read by ordinal, so column order and extra columns do not matter. The practical consequence is that one `Customer` model can serve a list query that returns three columns and a detail query that returns twelve. The trade-off is that a misspelled column name leaves the property at its default, discoverable only in the `Debug` log.

## Nulls

How a database null reaches a property depends on the property type:

- **Nullable value types and nullable enums** (`int?`, `DateTime?`, `CustomerStatus?`) map null to `DBNull` and back. This is the recommended way to map nullable columns.
- **Non-nullable sentinels.** A `Guid` property treats `Guid.Empty` as null, and `double`/`float` properties treat `NaN` as null, in both directions. Wrap the type in `Nullable<>` if you need to store an empty Guid or a real NaN.
- **Strings and arrays** save null as `DBNull`; an empty string saves as a zero-length string.
- **Enums** save their *name* when mapped to a string column and their *numeric value* when mapped to a numeric column, where the enum's base type must match the column type.

## The required flag

Every attribute has an optional trailing `bool`. When it is `true`, a null in that column or parameter means "there is no record", not "this field is empty":

```csharp
[MapToSqlInt("@CustomerId", true)]
public int CustomerId { get; set; }
```

For a single-object result the Mapper returns `null` instead of a `Customer` with default properties, and logs which value caused it at `Debug`. For a list, the row is omitted. Key columns are the natural place for the flag, since they are null only when an outer join found nothing.

Inside a nested model the flag works on the parent: a required null in an `Address` property nulls the whole `Customer`, not only the address, because nested properties are mapped as part of the same column set.

## Nested models

A property whose type is itself a mapped model is marked `[MapToModel]`. Its columns and parameters are flattened into the parent's, so an `Address` model shared by customers, stores and vendors is mapped once and reused:

```csharp
public class Customer
{
    [MapToSqlInt("@CustomerId", true)]
    public int CustomerId { get; set; }

    [MapToModel]
    public Address BillingAddress { get; set; }
}

public class Address
{
    [MapToSqlNVarChar("@StreetAddress", 255)]
    public string StreetAddress { get; set; }

    [MapToSqlNVarChar("@PostalCode", 25)]
    public string PostalCode { get; set; }
}
```

Mapping a `Customer` now expects `@StreetAddress` and `@PostalCode` alongside `@CustomerId`. If the property is null, settable and its type has a parameterless constructor, the Mapper creates the instance; otherwise initialize it yourself. Nested models chain to any depth: `Address` could carry a `[MapToModel] Coordinates` property of its own.

## Result-set lists

One-to-many data comes from additional result sets, not from extra columns. Give the parent a settable `List<T>` property and name the element types, in result-set order, as extra generic arguments on the data method. No attribute is needed; the Mapper matches each result set to the property whose element type it fits.

```csharp
public readonly record struct Order
{
    [MapToSqlInt("@OrderId", true)]
    public int OrderId { get; init; }

    public List<OrderItem> Items { get; init; }
}

// Result set 1: one Order row. Result set 2: its OrderItem rows.
var order = await _db.Read.MapReaderAsync<Order, Order, OrderItem>(Queries.GetOrder, prms, cancellation);

// Order fields in output parameters, items in the only result set.
var order = await _db.Read.MapOutputAsync<Order, OrderItem>(Queries.GetOrder, prms, cancellation);
```

The first generic argument is the return type; the rest describe the result sets in order. A result set of the return type supplies the root object and must contain exactly one row; any other type fills the list property with that element type. Up to eight list properties are supported. Because matching is by type, a model can have only one list property per element type, and items in a list do not get nested lists of their own. For deeper hierarchies, write a [custom handler](/guides/queries#custom-result-handlers) and call the Mapper for the parts it handles.

## Inheritance

Attributes are inherited, which is the main reason to prefer a class over a record struct for a family of models. A `CustomerSummary` base class with the key and name, and a `Customer` subclass with the remaining columns, lets list queries map the small type and detail queries the full one, with shared attributes declared once.

## Using the Mapper directly

The `Map*` data methods call the Mapper for you. When you want ADO.NET in your hands, call the same extension methods yourself; they work on any `DbParameterCollection` (including `SqlCommand.Parameters` and `NpgsqlCommand.Parameters`) and any `DbDataReader`:

| Method | Does |
| --- | --- |
| `prms.CreateInputParameters(model, logger)` | Adds an input parameter per mapped property, set from the model |
| `prms.CreateOutputParameters<TModel>(logger)` | Adds an output parameter per mapped property |
| `prms.ToModel<TModel>(logger)` | Builds a model from output parameter values |
| `rdr.ToModel<TModel>(logger)` | Builds a model from the current result set's single row |
| `rdr.ToList<TModel>(logger)` | Builds a `List<TModel>` from the current result set |

Parameters already in the collection are not recreated, so add the ones you want to treat specially first:

```csharp
cmd.Parameters.AddSqlIntInputParameter("@CustomerId", id)
    .CreateOutputParameters<Customer>(logger);
await cmd.ExecuteNonQueryAsync(cancellation);
var customer = cmd.Parameters.ToModel<Customer>(logger);
```

You can inspect, add, remove or change any parameter the Mapper created before the command runs. Every call takes an `ILogger`; the Mapper's diagnostics are the only window into compiled code.

## Many rows at once: table-valued parameters and COPY

Saving an order with twenty items should be one round trip, not twenty. The mechanism differs by provider.

::: code-group
```csharp [SQL Server]
// Each OrderItem becomes a row of the table-valued parameter,
// using the same MapToSql* attributes. An empty list is sent as NULL.
var prms = new ParameterCollection()
    .AddSqlIntInputParameter("@OrderId", order.OrderId)
    .AddSqlTableValuedParameter("@Items", order.Items, logger);

await _db.Write.RunAsync(Queries.SaveOrderItems, prms, cancellation);
```
```csharp [PostgreSQL]
// COPY the items into a temp table inside the batch's transaction,
// then run the statement that merges them into the real table.
var batch = new DatabaseBatch<int>()
    .Add(order.Items, "temp_order_items")
    .Add(Queries.SaveOrderItems, prms);

await _db.Write.RunAsync(batch, cancellation);
```
:::

On SQL Server, `AddSqlTableValuedParameter` converts each model to a `SqlDataRecord` with a compiled delegate, like every other Mapper operation; a list property can also carry `[MapToSqlTableValuedParameter("@Items", "dbo.OrderItemType")]` so that `CreateInputParameters` on the parent sends it. A null or empty collection is sent as a null parameter value, which SQL Server reads as an empty table; the one exception is a lazily produced `IEnumerable<SqlDataRecord>` that is not a collection, which the SqlClient driver rejects when it turns out to yield no rows, so buffer it or pass null. On PostgreSQL, the bulk load is a batch step built on Npgsql's binary COPY: a table name without a schema creates a temporary table dropped on commit, while `schema.table` creates the table if it does not exist. See [Batches](/guides/batches).

**Next:** [Sharding](/guides/sharding)
