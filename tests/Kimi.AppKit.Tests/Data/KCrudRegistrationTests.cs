using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kimi.AppKit.Tests.Data;

/// <summary>
/// CRUD 实体登记（<c>AddKCrud().AddEntity&lt;T&gt;()</c>）的行为契约。
/// </summary>
/// <remarks>
/// 【为什么这组测试重要】登记是**白名单**——「哪些实体可以被外部读写」这件事，
/// 前身完全没有一处代码在说明（一个端点读任意表），于是权限判断无处可挂。
/// 白名单一旦失效（没登记也能解析出数据源、或自定义实现被默认实现顶掉），
/// 症状都是**安静的**：功能照常工作，只是开放面比预期大。
/// </remarks>
public sealed class KCrudRegistrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public KCrudRegistrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<ShopContext>>(new PooledFactory(_connection));
        configure(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task 登记过的实体能解析出数据源并真的读到数据()
    {
        using var sp = BuildProvider(s => s.AddKCrud<ShopContext>().AddEntity<Product>());

        using (var db = sp.GetRequiredService<IDbContextFactory<ShopContext>>().CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Products.Add(new Product { Name = "螺栓" });
            await db.SaveChangesAsync();
        }

        using var scope = sp.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<ICrudDataSource<Product>>();
        var page = await source.LoadAsync(new KQuery());

        Assert.Equal(1, page.TotalCount);
        Assert.IsType<ReflectiveCrudDataSource<ShopContext, Product>>(source);
    }

    [Fact]
    public void 未登记的实体解析不出数据源()
    {
        using var sp = BuildProvider(s => s.AddKCrud<ShopContext>().AddEntity<Product>());
        using var scope = sp.CreateScope();

        // ⚠️ 这条是白名单的核心断言：没登记 = 拿不到数据源 = 端点也映射不出来。
        Assert.Null(scope.ServiceProvider.GetService<ICrudDataSource<Warehouse>>());
    }

    [Fact]
    public void 先注册的自定义实现胜出()
    {
        using var sp = BuildProvider(s =>
        {
            // TryAdd 语义：调用 AddEntity 之前先注册自己的实现即可覆盖。
            s.AddScoped<ICrudDataSource<Product>, CustomProductSource>();
            s.AddKCrud<ShopContext>().AddEntity<Product>();
        });

        using var scope = sp.CreateScope();
        Assert.IsType<CustomProductSource>(scope.ServiceProvider.GetRequiredService<ICrudDataSource<Product>>());
    }

    [Fact]
    public void 显式指定实现类型也生效()
    {
        using var sp = BuildProvider(s =>
        {
            s.AddScoped<CustomProductSource>();
            s.AddKCrud<ShopContext>().AddEntity<Product, CustomProductSource>();
        });

        using var scope = sp.CreateScope();
        Assert.IsType<CustomProductSource>(scope.ServiceProvider.GetRequiredService<ICrudDataSource<Product>>());
    }

    [Fact]
    public void 登记清单可被运行期枚举()
    {
        using var sp = BuildProvider(s =>
            s.AddKCrud<ShopContext>().AddEntity<Product>().AddEntity<Warehouse>());

        var registered = sp.GetServices<KCrudEntityRegistration>().Select(r => r.EntityType).ToList();

        // 通用浏览页据此列出可维护的表，不必各页面各抄一份清单。
        Assert.Contains(typeof(Product), registered);
        Assert.Contains(typeof(Warehouse), registered);
        Assert.Equal(2, registered.Count);
    }

    private sealed class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class Warehouse
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
    }

    private sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
    {
        public DbSet<Product> Products => Set<Product>();
    }

    private sealed class PooledFactory(SqliteConnection connection) : IDbContextFactory<ShopContext>
    {
        public ShopContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<ShopContext>().UseSqlite(connection).Options);
    }

    private sealed class CustomProductSource : ICrudDataSource<Product>
    {
        public Task<KPage<Product>> LoadAsync(KQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(KPage<Product>.Empty(query));

        public Task<Product?> GetAsync(object id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Product?>(null);

        public Task<KResult> UpsertAsync(Product item, CancellationToken cancellationToken = default) =>
            Task.FromResult(KResult.Ok());

        public Task<KResult> DeleteAsync(object id, CancellationToken cancellationToken = default) =>
            Task.FromResult(KResult.Ok());
    }
}
