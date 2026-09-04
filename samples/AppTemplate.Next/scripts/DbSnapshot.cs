// 运行方式: dotnet run scripts/DbSnapshot.cs
// 前置条件: 设置环境变量 NACOS_USERNAME / NACOS_PASSWORD
// 可选: ASPNETCORE_ENVIRONMENT（默认 Development）

#:package Microsoft.Data.SqlClient@5.2.2
#:property TargetFramework=net8.0

using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.SqlClient;

var connStr = await GetConnectionStringFromNacos();
using var conn = new SqlConnection(connStr);
try
{
    conn.Open();
}
catch (SqlException ex) when (ex.Message.Contains("SSPI") || ex.Message.Contains("Kerberos"))
{
    Console.WriteLine($"数据库连接失败（SSPI/Kerberos 认证错误）");
    Console.WriteLine($"请检查 Kerberos ticket: klist");
    Console.WriteLine($"重新获取 ticket: kinit your_username@YOUR.DOMAIN");
    Console.WriteLine($"\n原始错误: {ex.Message}");
    return;
}
catch (SqlException ex)
{
    Console.WriteLine($"数据库连接失败: {ex.Message}");
    return;
}

Console.WriteLine($"Connected as: {Scalar("SELECT SYSTEM_USER")} | Database: {Scalar("SELECT DB_NAME()")}");
EnsureMetadataTable();
InteractiveMenu();
conn.Close();

// ──────────────────────────────── 交互菜单 ────────────────────────────────

void InteractiveMenu()
{
    while (true)
    {
        Console.WriteLine("\n╔══════════════════════════════════╗");
        Console.WriteLine("║  KMoldApp 数据库快照管理工具     ║");
        Console.WriteLine("╠══════════════════════════════════╣");
        Console.WriteLine("║  [1] 查看快照列表                ║");
        Console.WriteLine("║  [2] 创建快照                    ║");
        Console.WriteLine("║  [3] 恢复快照                    ║");
        Console.WriteLine("║  [4] 删除快照                    ║");
        Console.WriteLine("║  [5] 清空 Data Schema 数据       ║");
        Console.WriteLine("║  [0] 退出                        ║");
        Console.WriteLine("╚══════════════════════════════════╝");
        Console.Write("\n请选择> ");

        var input = Console.ReadLine()?.Trim();
        switch (input)
        {
            case "1": ListSnapshots(); break;
            case "2": CreateSnapshot(); break;
            case "3": RestoreSnapshot(); break;
            case "4": DeleteSnapshot(); break;
            case "5": ClearDataSchema(); break;
            case "0": return;
            default: Console.WriteLine("无效选项"); break;
        }
    }
}

// ──────────────────────────────── 元数据表 ────────────────────────────────

void EnsureMetadataTable()
{
    Exec(@"
        IF OBJECT_ID('dbo.__snapshots','U') IS NULL
        CREATE TABLE dbo.__snapshots (
            Id         INT IDENTITY(1,1) PRIMARY KEY,
            Name       NVARCHAR(100) NOT NULL,
            CreatedAt  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
            TableCount INT NOT NULL DEFAULT 0,
            CreatedBy  NVARCHAR(100) NOT NULL
        )");
}

// ──────────────────────────────── 查看 ────────────────────────────────

void ListSnapshots()
{
    Console.WriteLine("\n=== 快照列表 ===");

    var snapshots = GetSnapshotList();
    if (snapshots.Count == 0)
        Console.WriteLine("  （无快照）");
    else
    {
        foreach (var s in snapshots)
        {
            Console.WriteLine($"  #{s.id}  {s.name,-20}  {s.createdAt:yyyy-MM-dd HH:mm}  {s.tableCount} 张表  by {s.createdBy}");
            var totalRows = Scalar($@"
                SELECT ISNULL(SUM(p.rows),0) FROM sys.tables t
                JOIN sys.schemas sc ON t.schema_id=sc.schema_id
                JOIN sys.partitions p ON t.object_id=p.object_id AND p.index_id IN (0,1)
                WHERE t.name LIKE '_s{s.id}[_]%'");
            Console.WriteLine($"         总行数: {totalRows}");
        }
    }

    var legacyCount = Convert.ToInt32(Scalar(
        "SELECT COUNT(*) FROM sys.tables WHERE type='U' AND name LIKE '_bak_%'"));
    if (legacyCount > 0)
        Console.WriteLine($"\n  ⚠ 检测到 {legacyCount} 张旧版 _bak_* 备份表，可通过 [4] 删除 → 选择「清理旧备份」清除");
}

// ──────────────────────────────── 创建 ────────────────────────────────

void CreateSnapshot()
{
    Console.Write("\n快照名称（如 before-migration）: ");
    var name = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(name)) { Console.WriteLine("已取消"); return; }
    if (name.Length > 50) { Console.WriteLine("名称过长（最多50字符）"); return; }
    if (name.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
    {
        Console.WriteLine("名称只能包含字母、数字、-、_");
        return;
    }

    var creator = Scalar("SELECT SYSTEM_USER")?.ToString() ?? "unknown";

    // 插入元数据获取 Id
    Exec($"INSERT INTO dbo.__snapshots (Name, CreatedBy) VALUES (N'{Escape(name)}', N'{Escape(creator)}')");
    var id = Convert.ToInt32(Scalar("SELECT SCOPE_IDENTITY()"));

    var tables = GetUserTables();
    Console.WriteLine($"\n将备份 {tables.Count} 张表到快照 #{id} [{name}]...\n");

    int ok = 0, fail = 0;
    foreach (var (schema, tbl) in tables)
    {
        try
        {
            var snap = $"_s{id}_{tbl}";
            Exec($"SELECT * INTO [{schema}].[{snap}] FROM [{schema}].[{tbl}]");
            var rows = Scalar($"SELECT COUNT(*) FROM [{schema}].[{snap}]");
            Console.WriteLine($"  OK [{schema}].{tbl} -> {snap} ({rows} rows)");
            ok++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL [{schema}].{tbl}: {ex.Message}");
            fail++;
        }
    }

    Exec($"UPDATE dbo.__snapshots SET TableCount={ok} WHERE Id={id}");
    Console.WriteLine($"\n快照创建完成: {ok} 成功, {fail} 失败");
}

// ──────────────────────────────── 恢复 ────────────────────────────────

void RestoreSnapshot()
{
    var snapshots = GetSnapshotList();
    if (snapshots.Count == 0) { Console.WriteLine("\n无可恢复的快照"); return; }

    Console.WriteLine("\n=== 选择要恢复的快照 ===");
    for (int i = 0; i < snapshots.Count; i++)
        Console.WriteLine($"  [{i + 1}] #{snapshots[i].id}  {snapshots[i].name}  ({snapshots[i].createdAt:yyyy-MM-dd HH:mm})");
    Console.Write("\n请选择> ");

    if (!int.TryParse(Console.ReadLine()?.Trim(), out var idx) || idx < 1 || idx > snapshots.Count)
    { Console.WriteLine("已取消"); return; }

    var snap = snapshots[idx - 1];
    Console.Write($"\n确认从快照 #{snap.id} [{snap.name}] 恢复？这将覆盖当前数据 (y/N) ");
    if (Console.ReadLine()?.Trim().ToLower() != "y") { Console.WriteLine("已取消"); return; }

    Console.WriteLine("\nStep 1: 禁用外键...");
    var fks = GetForeignKeys();
    foreach (var (s, t, f) in fks) Exec($"ALTER TABLE [{s}].[{t}] NOCHECK CONSTRAINT [{f}]");
    Console.WriteLine($"  已禁用 {fks.Count} 个外键");

    Console.WriteLine("Step 2: Schema 漂移检测...");
    var tables = GetUserTables();
    var driftTables = new List<string>();
    foreach (var (schema, name) in tables)
    {
        var snapTbl = $"_s{snap.id}_{name}";
        var bakExists = Scalar($"SELECT OBJECT_ID('[{schema}].[{snapTbl}]','U')");
        if (bakExists == DBNull.Value || bakExists == null) continue;
        var curCols = GetColumnList(schema, name);
        var bakCols = GetColumnList(schema, snapTbl);
        if (curCols != bakCols)
            driftTables.Add($"[{schema}].[{name}]");
    }
    if (driftTables.Count > 0)
    {
        Console.WriteLine($"  ⚠ {driftTables.Count} 张表结构与快照不一致（可能跑过 migration）：");
        foreach (var t in driftTables) Console.WriteLine($"    - {t}");
        Console.Write("  继续恢复？(y/N) ");
        if (Console.ReadLine()?.Trim().ToLower() != "y") { ReenableForeignKeys(fks); Console.WriteLine("已取消"); return; }
    }
    else
    {
        Console.WriteLine("  OK，所有快照表与当前表结构一致");
    }

    Console.WriteLine("Step 3: 恢复数据...");
    int ok = 0, skip = 0, fail = 0;
    foreach (var (schema, name) in tables)
    {
        var snapTbl = $"_s{snap.id}_{name}";
        var exists = Scalar($"SELECT OBJECT_ID('[{schema}].[{snapTbl}]','U')");
        if (exists == DBNull.Value || exists == null) { skip++; continue; }
        try
        {
            var cols = GetColumnList(schema, name);
            var hasId = Convert.ToInt32(Scalar(
                $"SELECT COUNT(*) FROM sys.identity_columns WHERE object_id=OBJECT_ID('[{schema}].[{name}]')"));
            if (hasId > 0)
            {
                Exec($@"
                    BEGIN TRAN;
                    BEGIN TRY
                        DELETE FROM [{schema}].[{name}];
                        SET IDENTITY_INSERT [{schema}].[{name}] ON;
                        INSERT INTO [{schema}].[{name}] ({cols}) SELECT {cols} FROM [{schema}].[{snapTbl}];
                        SET IDENTITY_INSERT [{schema}].[{name}] OFF;
                        DBCC CHECKIDENT ('[{schema}].[{name}]', RESEED);
                        COMMIT TRAN;
                    END TRY
                    BEGIN CATCH
                        SET IDENTITY_INSERT [{schema}].[{name}] OFF;
                        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
                        THROW;
                    END CATCH");
            }
            else
            {
                Exec($@"
                    BEGIN TRAN;
                    BEGIN TRY
                        DELETE FROM [{schema}].[{name}];
                        INSERT INTO [{schema}].[{name}] ({cols}) SELECT {cols} FROM [{schema}].[{snapTbl}];
                        COMMIT TRAN;
                    END TRY
                    BEGIN CATCH
                        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
                        THROW;
                    END CATCH");
            }
            var rows = Scalar($"SELECT COUNT(*) FROM [{schema}].[{name}]");
            Console.WriteLine($"  OK [{schema}].{name} ({rows} rows)");
            ok++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL [{schema}].{name}: {ex.Message}");
            fail++;
        }
    }

    Console.WriteLine("Step 4: 启用外键...");
    var (fkOk, fkFail) = ReenableForeignKeys(fks);
    Console.WriteLine($"\n恢复完成: {ok} 恢复, {skip} 跳过, {fail} 失败, FK {fkOk}/{fks.Count}");
}

// ──────────────────────────────── 删除 ────────────────────────────────

void DeleteSnapshot()
{
    var snapshots = GetSnapshotList();
    var hasLegacy = Convert.ToInt32(Scalar(
        "SELECT COUNT(*) FROM sys.tables WHERE type='U' AND name LIKE '_bak_%'")) > 0;

    if (snapshots.Count == 0 && !hasLegacy) { Console.WriteLine("\n无可删除的快照"); return; }

    Console.WriteLine("\n=== 选择要删除的快照 ===");
    for (int i = 0; i < snapshots.Count; i++)
        Console.WriteLine($"  [{i + 1}] #{snapshots[i].id}  {snapshots[i].name}  ({snapshots[i].createdAt:yyyy-MM-dd HH:mm})");
    if (hasLegacy)
        Console.WriteLine($"  [L] 清理旧版 _bak_* 备份表");
    if (snapshots.Count > 0)
        Console.WriteLine($"  [A] 删除全部快照");
    Console.Write("\n请选择> ");

    var input = Console.ReadLine()?.Trim().ToUpper();
    if (input == "L" && hasLegacy)
    {
        CleanupLegacyBackups();
        return;
    }
    if (input == "A" && snapshots.Count > 0)
    {
        Console.Write($"确认删除全部 {snapshots.Count} 个快照？(y/N) ");
        if (Console.ReadLine()?.Trim().ToLower() != "y") { Console.WriteLine("已取消"); return; }
        foreach (var s in snapshots) DropSnapshotTables(s.id);
        Console.WriteLine($"\n已删除全部 {snapshots.Count} 个快照");
        return;
    }

    if (!int.TryParse(input, out var idx) || idx < 1 || idx > snapshots.Count)
    { Console.WriteLine("已取消"); return; }

    var snap = snapshots[idx - 1];
    Console.Write($"确认删除快照 #{snap.id} [{snap.name}]？(y/N) ");
    if (Console.ReadLine()?.Trim().ToLower() != "y") { Console.WriteLine("已取消"); return; }

    DropSnapshotTables(snap.id);
    Console.WriteLine($"\n已删除快照 #{snap.id} [{snap.name}]");
}

void DropSnapshotTables(int snapshotId)
{
    var tables = new List<(string s, string t)>();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = $@"SELECT s.name, t.name FROM sys.tables t
        JOIN sys.schemas s ON t.schema_id=s.schema_id
        WHERE t.name LIKE '_s{snapshotId}[_]%' AND t.type='U'";
    using (var r = cmd.ExecuteReader()) { while (r.Read()) tables.Add((r.GetString(0), r.GetString(1))); }
    foreach (var (s, t) in tables) Exec($"DROP TABLE [{s}].[{t}]");
    Exec($"DELETE FROM dbo.__snapshots WHERE Id={snapshotId}");
    Console.WriteLine($"  已删除 {tables.Count} 张影子表");
}

// ──────────────────────────────── 清空 Data Schema ────────────────────────────────

// 只清空 Data schema 下所有用户表的数据，其它 schema 一律不动。
// GetUserTables 已排除快照影子表(_s%)/旧备份(_bak_%)/EF 迁移表/__snapshots，故不会误删。
void ClearDataSchema()
{
    const string targetSchema = "Data";

    var tables = GetUserTables().Where(t => t.schema == targetSchema).ToList();
    if (tables.Count == 0) { Console.WriteLine($"\nSchema [{targetSchema}] 下没有用户表"); return; }

    Console.WriteLine($"\n=== 清空 [{targetSchema}] Schema 所有数据 ===");
    Console.WriteLine($"将清空以下 {tables.Count} 张表的全部数据（仅 [{targetSchema}] Schema，其它 Schema 不受影响）：");
    foreach (var (s, t) in tables)
    {
        var rows = Scalar($"SELECT COUNT(*) FROM [{s}].[{t}]");
        Console.WriteLine($"    - [{s}].[{t}]  ({rows} rows)");
    }

    Console.Write($"\n⚠ 此操作不可恢复！确认清空 [{targetSchema}] Schema 所有数据？(y/N) ");
    if (Console.ReadLine()?.Trim().ToLower() != "y") { Console.WriteLine("已取消"); return; }
    Console.Write($"再次确认，请输入 schema 名 [{targetSchema}] 以继续> ");
    if (Console.ReadLine()?.Trim() != targetSchema) { Console.WriteLine("输入不匹配，已取消"); return; }

    // 仅禁用与 Data schema 相关的外键（父表或被引用表在 Data schema 内），删除后恢复。
    // 禁用/启用约束不改任何数据，且不涉及其它 schema 的表数据。
    Console.WriteLine("\nStep 1: 禁用相关外键...");
    var fks = GetForeignKeysInvolvingSchema(targetSchema);
    foreach (var (s, t, f) in fks) Exec($"ALTER TABLE [{s}].[{t}] NOCHECK CONSTRAINT [{f}]");
    Console.WriteLine($"  已禁用 {fks.Count} 个外键");

    Console.WriteLine("Step 2: 删除数据...");
    int ok = 0, fail = 0;
    foreach (var (schema, name) in tables)
    {
        try
        {
            Exec($"DELETE FROM [{schema}].[{name}]");
            var hasId = Convert.ToInt32(Scalar(
                $"SELECT COUNT(*) FROM sys.identity_columns WHERE object_id=OBJECT_ID('[{schema}].[{name}]')"));
            if (hasId > 0) Exec($"DBCC CHECKIDENT ('[{schema}].[{name}]', RESEED, 0)");
            Console.WriteLine($"  OK [{schema}].{name}");
            ok++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL [{schema}].{name}: {ex.Message}");
            fail++;
        }
    }

    Console.WriteLine("Step 3: 启用外键...");
    var (fkOk, fkFail) = ReenableForeignKeys(fks);
    Console.WriteLine($"\n清空完成: {ok} 成功, {fail} 失败, FK {fkOk}/{fks.Count}");
}

// 取所有「父表或被引用表位于指定 schema」的外键，供清空前临时禁用。
List<(string schema, string table, string fk)> GetForeignKeysInvolvingSchema(string schema)
{
    var list = new List<(string, string, string)>();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = $@"SELECT ps.name, pt.name, fk.name
        FROM sys.foreign_keys fk
        JOIN sys.tables pt ON fk.parent_object_id = pt.object_id
        JOIN sys.schemas ps ON pt.schema_id = ps.schema_id
        JOIN sys.tables rt ON fk.referenced_object_id = rt.object_id
        JOIN sys.schemas rs ON rt.schema_id = rs.schema_id
        WHERE ps.name = N'{Escape(schema)}' OR rs.name = N'{Escape(schema)}'";
    using var r = cmd.ExecuteReader();
    while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
    return list;
}

void CleanupLegacyBackups()
{
    var baks = new List<(string s, string t)>();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT s.name, t.name FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE t.name LIKE '_bak_%' AND t.type='U'";
    using (var r = cmd.ExecuteReader()) { while (r.Read()) baks.Add((r.GetString(0), r.GetString(1))); }
    Console.Write($"确认删除 {baks.Count} 张旧版 _bak_* 表？(y/N) ");
    if (Console.ReadLine()?.Trim().ToLower() != "y") { Console.WriteLine("已取消"); return; }
    foreach (var (s, t) in baks) { Exec($"DROP TABLE [{s}].[{t}]"); Console.WriteLine($"  OK [{s}].[{t}]"); }
    Console.WriteLine($"\n已清理 {baks.Count} 张旧备份表");
}

// ──────────────────────────────── Helpers ────────────────────────────────

List<(int id, string name, DateTime createdAt, int tableCount, string createdBy)> GetSnapshotList()
{
    var list = new List<(int, string, DateTime, int, string)>();
    if (Scalar("SELECT OBJECT_ID('dbo.__snapshots','U')") == DBNull.Value) return list;
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT Id, Name, CreatedAt, TableCount, CreatedBy FROM dbo.__snapshots ORDER BY Id";
    using var r = cmd.ExecuteReader();
    while (r.Read()) list.Add((r.GetInt32(0), r.GetString(1), r.GetDateTime(2), r.GetInt32(3), r.GetString(4)));
    return list;
}

List<(string schema, string name)> GetUserTables()
{
    var list = new List<(string, string)>();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"SELECT s.name, t.name FROM sys.tables t
        JOIN sys.schemas s ON t.schema_id=s.schema_id
        WHERE t.type='U' AND t.name NOT LIKE '_bak_%' AND t.name NOT LIKE '_s[0-9]%'
        AND t.name NOT LIKE '__EF%' AND t.name <> '__snapshots'
        ORDER BY s.name, t.name";
    using var r = cmd.ExecuteReader();
    while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
    return list;
}

List<(string schema, string table, string fk)> GetForeignKeys()
{
    var list = new List<(string, string, string)>();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT SCHEMA_NAME(schema_id), OBJECT_NAME(parent_object_id), name FROM sys.foreign_keys";
    using var r = cmd.ExecuteReader();
    while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
    return list;
}

(int ok, int fail) ReenableForeignKeys(List<(string schema, string table, string fk)> fks)
{
    int ok = 0, fail = 0;
    foreach (var (s, t, f) in fks)
    {
        try { Exec($"ALTER TABLE [{s}].[{t}] WITH CHECK CHECK CONSTRAINT [{f}]"); ok++; }
        catch (Exception ex) { Console.WriteLine($"  FK FAIL [{s}].[{t}].[{f}]: {ex.Message}"); fail++; }
    }
    return (ok, fail);
}

string GetColumnList(string schema, string name)
{
    var cols = new List<string>();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = $@"SELECT c.name FROM sys.columns c
        JOIN sys.types t ON c.system_type_id = t.system_type_id AND c.user_type_id = t.user_type_id
        WHERE c.object_id = OBJECT_ID('[{schema}].[{name}]') AND t.name <> 'timestamp' AND c.is_computed = 0
        ORDER BY c.column_id";
    using var r = cmd.ExecuteReader();
    while (r.Read()) cols.Add($"[{r.GetString(0)}]");
    return string.Join(", ", cols);
}

string Escape(string s) => s.Replace("'", "''");
object Scalar(string sql) { using var c = conn.CreateCommand(); c.CommandText = sql; return c.ExecuteScalar()!; }
void Exec(string sql) { using var c = conn.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }

// ──────────────────────────────── Nacos ────────────────────────────────

async Task<string> GetConnectionStringFromNacos()
{
    var projectDir = Path.Combine(AppContext.BaseDirectory, "..", "KMoldApp");
    if (!Directory.Exists(projectDir))
        projectDir = Path.Combine(Directory.GetCurrentDirectory(), "KMoldApp");

    var appSettingsPath = Path.Combine(projectDir, "appsettings.json");
    if (!File.Exists(appSettingsPath))
        throw new FileNotFoundException($"找不到配置文件: {appSettingsPath}");

    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
    var envSettingsPath = Path.Combine(projectDir, $"appsettings.{env}.json");

    var baseConfig = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
    var nacosSection = baseConfig.RootElement.GetProperty("NacosConfig");
    var listener = nacosSection.GetProperty("Listeners")[0];
    var nacosUrl = nacosSection.GetProperty("ServerAddresses")[0].GetString()!;
    var dataId = listener.GetProperty("DataId").GetString()!;
    var group = listener.GetProperty("Group").GetString()!;

    var tenant = "";
    if (File.Exists(envSettingsPath))
    {
        var envConfig = JsonDocument.Parse(File.ReadAllText(envSettingsPath));
        if (envConfig.RootElement.TryGetProperty("NacosConfig", out var envNacos) &&
            envNacos.TryGetProperty("Namespace", out var ns))
            tenant = ns.GetString()!;
    }

    var nacosUser = Environment.GetEnvironmentVariable("NACOS_USERNAME")
        ?? throw new Exception("环境变量 NACOS_USERNAME 未设置");
    var nacosPass = Environment.GetEnvironmentVariable("NACOS_PASSWORD")
        ?? throw new Exception("环境变量 NACOS_PASSWORD 未设置");

    Console.WriteLine($"Nacos: {nacosUrl} | env={env} | dataId={dataId} | group={group}");

    using var http = new HttpClient();
    var loginResp = await http.PostAsync(
        $"{nacosUrl}nacos/v1/auth/login?username={nacosUser}&password={nacosPass}", null);
    loginResp.EnsureSuccessStatusCode();
    var loginJson = JsonDocument.Parse(await loginResp.Content.ReadAsStringAsync());
    var token = loginJson.RootElement.GetProperty("accessToken").GetString()!;

    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    var url = $"{nacosUrl}nacos/v1/cs/configs?dataId={dataId}&group={group}";
    if (!string.IsNullOrEmpty(tenant)) url += $"&tenant={tenant}";
    var configRaw = await http.GetStringAsync(url);
    var cleanJson = string.Join('\n', configRaw.Split('\n')
        .Where(l => !l.TrimStart().StartsWith("//")));
    var config = JsonDocument.Parse(cleanJson);
    var cs = config.RootElement.GetProperty("ConnectionStrings")
        .GetProperty("DefaultConnection").GetString()!;

    if (!cs.Contains("Command Timeout", StringComparison.OrdinalIgnoreCase))
        cs += "Command Timeout=300;";
    return cs;
}
