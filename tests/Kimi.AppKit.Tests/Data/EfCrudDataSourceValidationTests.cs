using System.ComponentModel.DataAnnotations;
using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kimi.AppKit.Tests.Data;

/// <summary>
/// <c>EfCrudDataSource.UpsertAsync</c> 的保存前校验与唯一约束冲突翻译（A7）。
/// 端点手工读 body，minimal API 的自动校验不跑，所以校验必须落在数据源这一层；
/// Excel 导入逐行调同一个 UpsertAsync，自然一并覆盖。
/// </summary>
public sealed class EfCrudDataSourceValidationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<VContext> _factory;

    public EfCrudDataSourceValidationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _factory = new Factory(_connection);
        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private EfCrudDataSource<VContext, Supplier> Suppliers() => new(_factory);

    private int Count() { using var db = _factory.CreateDbContext(); return db.Suppliers.Count(); }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task 必填名称留空时保存失败且不落库(string name)
    {
        var result = await Suppliers().UpsertAsync(new Supplier { Name = name });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("Name"));
        Assert.Equal(0, Count());
    }

    [Fact]
    public async Task 合法实体保存成功()
    {
        Assert.True((await Suppliers().UpsertAsync(new Supplier { Name = "A" })).Succeeded);
        Assert.Equal(1, Count());
    }

    [Fact]
    public async Task 更新路径同样校验且库里保留旧值()
    {
        var source = Suppliers();
        await source.UpsertAsync(new Supplier { Name = "A" });
        var stored = (await source.LoadAsync(new KQuery())).Items.Single();

        stored.Name = "";
        var result = await source.UpsertAsync(stored);

        Assert.False(result.Succeeded);
        Assert.Equal("A", (await source.LoadAsync(new KQuery())).Items.Single().Name);
    }

    [Fact]
    public async Task 超长名称被拒绝()
    {
        var result = await Suppliers().UpsertAsync(new Supplier { Name = new string('x', 201) });
        Assert.False(result.Succeeded);
        Assert.Equal(0, Count());
    }

    [Fact]
    public async Task 审计字段上的_Required_不会因客户端未提供而拒绝保存()
    {
        // 审计字段由审计上下文在保存时填，校验时必为缺省值，不能算客户端的错。
        var source = new EfCrudDataSource<VContext, Doc>(_factory);
        var result = await source.UpsertAsync(new Doc { Title = "T" });
        Assert.True(result.Succeeded, string.Join(";", result.Errors));
    }

    [Fact]
    public async Task 非审计字段的_Required_仍然生效()
    {
        var source = new EfCrudDataSource<VContext, Doc>(_factory);
        Assert.False((await source.UpsertAsync(new Doc { Title = "" })).Succeeded);
    }

    [Fact]
    public async Task 重复名称返回可读失败而不是抛异常()
    {
        var source = Suppliers();
        Assert.True((await source.UpsertAsync(new Supplier { Name = "A" })).Succeeded);

        var result = await source.UpsertAsync(new Supplier { Name = "A" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("已存在"));
        Assert.Contains(result.Errors, e => e.Contains("Name"));
        Assert.DoesNotContain(result.Errors, e => e.Contains("Suppliers", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, Count());
    }

    [Fact]
    public async Task 审计属性的_Required_失败不会让_IValidatableObject_被跳过()
    {
        // 评审探针：先整体校验再丢审计错误时，TryValidateObject 因审计属性失败提前停止，
        // IValidatableObject 不运行，保存静默通过。
        var source = new EfCrudDataSource<VContext, Probe>(_factory);
        var result = await source.UpsertAsync(new Probe { Title = "T" });
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("对象级规则"));
    }

    [Fact]
    public async Task 类级特性会运行()
    {
        var result = await new EfCrudDataSource<VContext, ClassLevel>(_factory)
            .UpsertAsync(new ClassLevel { Title = "T" });
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("类级规则"));
    }

    [Fact]
    public async Task 只指向审计属性的_IValidatableObject_错误被忽略()
    {
        var result = await new EfCrudDataSource<VContext, AuditOnly>(_factory)
            .UpsertAsync(new AuditOnly { Title = "T" });
        Assert.True(result.Succeeded, string.Join(";", result.Errors));
    }

    [Fact]
    public async Task 内置特性用中文消息且字段名取_Display()
    {
        var result = await new EfCrudDataSource<VContext, Labeled>(_factory)
            .UpsertAsync(new Labeled { Title = "", Code = new string('x', 6), Qty = 0 });
        Assert.Contains("标题不能为空。", result.Errors);
        Assert.Contains("编码长度不能超过 5。", result.Errors);
        Assert.Contains("数量必须在 1 到 9 之间。", result.Errors);
        Assert.Contains("自定义消息", result.Errors);   // 显式消息优先
    }

    [Fact]
    public async Task 唯一冲突提示使用字段显示名()
    {
        var source = new EfCrudDataSource<VContext, Labeled>(_factory);
        Assert.True((await source.UpsertAsync(new Labeled { Title = "A", Code = "c", Qty = 1, Tag = "o" })).Succeeded);
        var dup = await source.UpsertAsync(new Labeled { Title = "A", Code = "d", Qty = 1, Tag = "o" });
        Assert.Contains(dup.Errors, e => e.Contains("标题") && e.Contains("已存在"));
    }

    // ── 测试替身 ──────────────────────────────────────────────

    private sealed class Supplier : AuditableEntityWithName;

    private sealed class Probe : IAuditableEntity, IValidatableObject
    {
        public int Id { get; set; }
        [Required] public string Title { get; set; } = string.Empty;
        public DateTimeOffset Updated { get; set; }
        [Required] public string? UpdatedBy { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public string? CreatedBy { get; set; }
        public IEnumerable<ValidationResult> Validate(ValidationContext c) => [new("对象级规则")];
    }

    private sealed class AuditOnly : IAuditableEntity, IValidatableObject
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTimeOffset Updated { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public string? CreatedBy { get; set; }
        public IEnumerable<ValidationResult> Validate(ValidationContext c) =>
            [new("审计", [nameof(CreatedBy)])];
    }

    [AlwaysFail]
    private sealed class ClassLevel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    [AttributeUsage(AttributeTargets.Class)]
    private sealed class AlwaysFailAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext c) => new("类级规则");
    }

    private sealed class Labeled
    {
        public int Id { get; set; }
        [Required, Display(Name = "标题")] public string Title { get; set; } = string.Empty;
        [MaxLength(5), Display(Name = "编码")] public string Code { get; set; } = string.Empty;
        [Range(1, 9), Display(Name = "数量")] public int Qty { get; set; }
        [MaxLength(1, ErrorMessage = "自定义消息")] public string Tag { get; set; } = "xx";
    }


    private sealed class Doc : IAuditableEntity
    {
        public int Id { get; set; }
        [Required] public string Title { get; set; } = string.Empty;
        public DateTimeOffset Updated { get; set; }
        [Required] public string? UpdatedBy { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        [Required] public string? CreatedBy { get; set; }
    }

    private sealed class VContext(DbContextOptions<VContext> options) : DbContext(options)
    {
        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<Doc> Docs => Set<Doc>();
        public DbSet<Probe> Probes => Set<Probe>();
        public DbSet<ClassLevel> ClassLevels => Set<ClassLevel>();
        public DbSet<AuditOnly> AuditOnlies => Set<AuditOnly>();
        public DbSet<Labeled> Labeleds => Set<Labeled>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Supplier>().HasIndex(s => s.Name).IsUnique();
            builder.Entity<Labeled>().HasIndex(l => l.Title).IsUnique();
        }

        /// <summary>模拟审计上下文：保存时才填审计字段。</summary>
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var d in ChangeTracker.Entries<Doc>())
            {
                d.Entity.CreatedBy ??= "u";
                d.Entity.UpdatedBy ??= "u";
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class Factory(SqliteConnection connection) : IDbContextFactory<VContext>
    {
        public VContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<VContext>().UseSqlite(connection).Options);
    }
}
