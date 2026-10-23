---
layout: home
title: ArgentSea · .NET data access for global scale
titleTemplate: false
description: ArgentSea is a .NET open source data-access framework built on ADO.NET for simplicity, high performance and global scale. Sharding, tenant databases, read scale-out, compiled mapping, resilience and Microsoft Orleans.
pageClass: as-home
---

<HomeHero />

<HomeFeatures />

<HomeTopology />

<HomeCodeTeaser>

::: code-group

```csharp [SQL Server]
public readonly record struct Customer : IKeyedModel<int>
{
    [MapSqlShardKey("@CustomerId")]
    [MapToSqlInt("@CustomerId")]
    public ShardKey<int> Key { get; init; }

    [MapToSqlNVarChar("@Name", 100)]
    public string Name { get; init; }

    [MapToSqlNVarChar("@Email", 255)]
    public string? Email { get; init; }
}

public static class Queries
{
    public static QueryProcedure GetCustomer
        => new("rdr.GetCustomer", ["CustomerId"]);
}

public class CustomerStore(SqlShardSets shardSets, ILogger<CustomerStore> logger)
{
    private readonly SqlShardSets.ShardSet _customers = shardSets["Customers"];

    public Task<Customer> GetAsync(ShardKey<int> key, CancellationToken cancellation)
    {
        var prms = new ParameterCollection()
            .AddSqlIntInputParameter("@CustomerId", key.RecordId);
        return _customers[key].Read
            .MapReaderAsync<Customer>(Queries.GetCustomer, prms, cancellation);
    }
}
```

```csharp [PostgreSQL]
public readonly record struct Customer : IKeyedModel<int>
{
    [MapPgShardKey("customer_id")]
    [MapToPgInteger("customer_id")]
    public ShardKey<int> Key { get; init; }

    [MapToPgVarchar("name", 100)]
    public string Name { get; init; }

    [MapToPgVarchar("email", 255)]
    public string? Email { get; init; }
}

public static class Queries
{
    // Loads SQL/GetCustomer.psql the first time it is used.
    private static readonly Lazy<QueryStatement> _getCustomer
        = QueryStatement.Create("GetCustomer", ["customer_id"]);
    public static QueryStatement GetCustomer => _getCustomer.Value;
}

public class CustomerStore(PgShardSets shardSets, ILogger<CustomerStore> logger)
{
    private readonly PgShardSets.ShardSet _customers = shardSets["Customers"];

    public Task<Customer> GetAsync(ShardKey<int> key, CancellationToken cancellation)
    {
        var prms = new ParameterCollection()
            .AddPgIntegerInputParameter("customer_id", key.RecordId);
        return _customers[key].Read
            .MapReaderAsync<Customer>(Queries.GetCustomer, prms, cancellation);
    }
}
```

:::

</HomeCodeTeaser>

<HomePlatforms />
