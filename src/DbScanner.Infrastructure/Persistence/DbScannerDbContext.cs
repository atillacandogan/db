using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DbScanner.Infrastructure.Persistence;

public sealed class DbScannerDbContext(DbContextOptions<DbScannerDbContext> options) : DbContext(options)
{
    public DbSet<Target> Targets => Set<Target>();
    public DbSet<Scan> Scans => Set<Scan>();
    public DbSet<ScanScriptResult> ScanScriptResults => Set<ScanScriptResult>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<RuleEngineSupport> RuleEngineSupport => Set<RuleEngineSupport>();
    public DbSet<RuleNseScript> RuleNseScripts => Set<RuleNseScript>();
    public DbSet<NseScript> NseScripts => Set<NseScript>();
    public DbSet<NseScriptMatcher> NseScriptMatchers => Set<NseScriptMatcher>();
    public DbSet<ScanScopeAllowlist> ScanScopeAllowlist => Set<ScanScopeAllowlist>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<DiscoveryJob> DiscoveryJobs => Set<DiscoveryJob>();
    public DbSet<DiscoveredDatabase> DiscoveredDatabases => Set<DiscoveredDatabase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureTargets(modelBuilder);
        ConfigureScans(modelBuilder);
        ConfigureScriptResults(modelBuilder);
        ConfigureRules(modelBuilder);
        ConfigureNseScripts(modelBuilder);
        ConfigureFindings(modelBuilder);
        ConfigureScope(modelBuilder);
        ConfigureDiscovery(modelBuilder);
        SeedData(modelBuilder);
    }

    private static void ConfigureTargets(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Target>(entity =>
        {
            entity.ToTable("targets");
            entity.HasKey(target => target.Id);
            entity.Property(target => target.Name).HasMaxLength(160).IsRequired();
            entity.Property(target => target.Host).HasMaxLength(255).IsRequired();
            entity.Property(target => target.NormalizedHost).HasMaxLength(255).IsRequired();
            entity.Property(target => target.Engine).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(target => target.ConnectionMode).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(target => target.EnvironmentTag).HasMaxLength(64);
            entity.Property(target => target.ProtectedCredentials).HasMaxLength(4096);
            entity.HasIndex(target => new { target.NormalizedHost, target.Port, target.Engine }).IsUnique();
        });
    }

    private static void ConfigureScans(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Scan>(entity =>
        {
            entity.ToTable("scans");
            entity.HasKey(scan => scan.Id);
            entity.Property(scan => scan.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(scan => scan.OverallSeverity).HasConversion<string>().HasMaxLength(32);
            entity.Property(scan => scan.ErrorMessage).HasMaxLength(2048);
            entity.HasOne(scan => scan.Target)
                .WithMany(target => target.Scans)
                .HasForeignKey(scan => scan.TargetId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(scan => new { scan.TargetId, scan.Status });
        });
    }

    private static void ConfigureScriptResults(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScanScriptResult>(entity =>
        {
            entity.ToTable("scan_script_results");
            entity.HasKey(result => result.Id);
            entity.Property(result => result.NseScriptId).HasMaxLength(128);
            entity.Property(result => result.ScriptName).HasMaxLength(128).IsRequired();
            entity.Property(result => result.ResultStatus).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(result => result.Scan)
                .WithMany(scan => scan.ScriptResults)
                .HasForeignKey(result => result.ScanId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(result => result.NseScript)
                .WithMany(script => script.ScanResults)
                .HasForeignKey(result => result.NseScriptId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureRules(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Rule>(entity =>
        {
            entity.ToTable("rules");
            entity.HasKey(rule => rule.Id);
            entity.Property(rule => rule.Id).HasMaxLength(128);
            entity.Property(rule => rule.Title).HasMaxLength(240).IsRequired();
            entity.Property(rule => rule.Description).HasMaxLength(2048).IsRequired();
            entity.Property(rule => rule.Engine).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(rule => rule.Severity).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(rule => rule.ParserType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(rule => rule.MatcherType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(rule => rule.MatcherExpression).HasMaxLength(2048).IsRequired();
            entity.Property(rule => rule.Remediation).HasMaxLength(2048).IsRequired();
        });

        modelBuilder.Entity<RuleEngineSupport>(entity =>
        {
            entity.ToTable("rule_engine_support");
            entity.HasKey(support => support.Id);
            entity.Property(support => support.Id).ValueGeneratedNever();
            entity.Property(support => support.RuleId).HasMaxLength(128).IsRequired();
            entity.Property(support => support.Engine).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(support => support.UnsupportedStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(support => new { support.RuleId, support.Engine }).IsUnique();
            entity.HasOne(support => support.Rule)
                .WithMany(rule => rule.EngineSupport)
                .HasForeignKey(support => support.RuleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RuleNseScript>(entity =>
        {
            entity.ToTable("rule_nse_scripts");
            entity.HasKey(mapping => new { mapping.RuleId, mapping.NseScriptId });
            entity.Property(mapping => mapping.RuleId).HasMaxLength(128);
            entity.Property(mapping => mapping.NseScriptId).HasMaxLength(128);
            entity.HasOne(mapping => mapping.Rule)
                .WithMany(rule => rule.RuleScripts)
                .HasForeignKey(mapping => mapping.RuleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(mapping => mapping.NseScript)
                .WithMany(script => script.RuleScripts)
                .HasForeignKey(mapping => mapping.NseScriptId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureNseScripts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NseScript>(entity =>
        {
            entity.ToTable("nse_scripts");
            entity.HasKey(script => script.Id);
            entity.Property(script => script.Id).HasMaxLength(128);
            entity.Property(script => script.NseScriptName).HasMaxLength(128).IsRequired();
            entity.Property(script => script.DisplayName).HasMaxLength(240).IsRequired();
            entity.Property(script => script.Description).HasMaxLength(2048).IsRequired();
            entity.Property(script => script.SupportedEngines).HasMaxLength(256).IsRequired();
            entity.Property(script => script.Categories).HasMaxLength(256);
            entity.Property(script => script.AllowedArgsSchema).HasColumnType("jsonb");
            entity.Property(script => script.ParserType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(script => script.MatcherType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(script => script.MatcherExpression).HasMaxLength(2048);
            entity.Property(script => script.Severity).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(script => script.RemediationTemplate).HasMaxLength(2048);
            entity.Property(script => script.DocUrl).HasMaxLength(512);
            entity.Property(script => script.ApprovalStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(script => script.Source).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(script => script.IsInstalled);
            entity.HasIndex(script => script.NseScriptName).IsUnique();
        });

        modelBuilder.Entity<NseScriptMatcher>(entity =>
        {
            entity.ToTable("nse_script_matchers");
            entity.HasKey(matcher => matcher.Id);
            entity.Property(matcher => matcher.NseScriptId).HasMaxLength(128).IsRequired();
            entity.Property(matcher => matcher.RuleId).HasMaxLength(128);
            entity.Property(matcher => matcher.ParserType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(matcher => matcher.MatcherType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(matcher => matcher.MatcherExpression).HasMaxLength(2048).IsRequired();
            entity.Property(matcher => matcher.Engine).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasOne(matcher => matcher.NseScript)
                .WithMany(script => script.Matchers)
                .HasForeignKey(matcher => matcher.NseScriptId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(matcher => matcher.Rule)
                .WithMany()
                .HasForeignKey(matcher => matcher.RuleId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureFindings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Finding>(entity =>
        {
            entity.ToTable("findings");
            entity.HasKey(finding => finding.Id);
            entity.Property(finding => finding.RuleId).HasMaxLength(128).IsRequired();
            entity.Property(finding => finding.Title).HasMaxLength(240).IsRequired();
            entity.Property(finding => finding.Severity).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(finding => finding.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(finding => finding.Evidence).HasMaxLength(4096).IsRequired();
            entity.Property(finding => finding.Remediation).HasMaxLength(2048).IsRequired();
            entity.Property(finding => finding.SourceScript).HasMaxLength(128).IsRequired();
            entity.HasOne(finding => finding.Scan)
                .WithMany(scan => scan.Findings)
                .HasForeignKey(finding => finding.ScanId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(finding => finding.Target)
                .WithMany(target => target.Findings)
                .HasForeignKey(finding => finding.TargetId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(finding => finding.Rule)
                .WithMany(rule => rule.Findings)
                .HasForeignKey(finding => finding.RuleId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureScope(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScanScopeAllowlist>(entity =>
        {
            entity.ToTable("scan_scope_allowlist");
            entity.HasKey(scope => scope.Id);
            entity.Property(scope => scope.Name).HasMaxLength(160).IsRequired();
            entity.Property(scope => scope.EnvironmentTag).HasMaxLength(64).IsRequired();
            entity.Property(scope => scope.ScopeValue).HasMaxLength(255).IsRequired();
            entity.Property(scope => scope.ScopeType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(scope => scope.AllowedEngines).HasMaxLength(128).IsRequired();
            entity.Property(scope => scope.AllowedPorts).HasMaxLength(1024).IsRequired();
            entity.Property(scope => scope.Description).HasMaxLength(512);
            entity.HasIndex(scope => new { scope.EnvironmentTag, scope.ScopeType, scope.ScopeValue }).IsUnique();
        });
    }

    private static void ConfigureDiscovery(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DiscoveryJob>(entity =>
        {
            entity.ToTable("discovery_jobs");
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Name).HasMaxLength(160).IsRequired();
            entity.Property(job => job.ScopeInput).HasMaxLength(4096).IsRequired();
            entity.Property(job => job.EnvironmentTag).HasMaxLength(64).IsRequired();
            entity.Property(job => job.SupportedEngineFilter).HasMaxLength(128).IsRequired();
            entity.Property(job => job.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(job => job.ErrorMessage).HasMaxLength(512);
            entity.HasIndex(job => job.CreatedAt);
        });

        modelBuilder.Entity<DiscoveredDatabase>(entity =>
        {
            entity.ToTable("discovered_databases");
            entity.HasKey(result => result.Id);
            entity.Property(result => result.Host).HasMaxLength(255).IsRequired();
            entity.Property(result => result.NormalizedHost).HasMaxLength(255).IsRequired();
            entity.Property(result => result.EnvironmentTag).HasMaxLength(64).IsRequired();
            entity.Property(result => result.DetectedService).HasMaxLength(128).IsRequired();
            entity.Property(result => result.DetectedEngine).HasConversion<string>().HasMaxLength(32);
            entity.Property(result => result.FingerprintLevel).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(result => result.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(result => result.Reason).HasMaxLength(512).IsRequired();
            entity.HasIndex(result => new
            {
                result.NormalizedHost,
                result.Port,
                result.DetectedEngine,
                result.EnvironmentTag
            }).IsUnique();
            entity.HasOne(result => result.DiscoveryJob)
                .WithMany(job => job.Results)
                .HasForeignKey(result => result.DiscoveryJobId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Target>()
                .WithMany()
                .HasForeignKey(result => result.TargetId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        var createdAt = new DateTimeOffset(2026, 4, 13, 0, 0, 0, TimeSpan.Zero);
        var demoPostgresTargetId = Guid.Parse("3d79cfbe-dbb5-4d95-b476-854ed970d43d");
        var demoMySqlTargetId = Guid.Parse("b2fd6c5a-98b4-4641-8e98-4b95c5ba98db");

        modelBuilder.Entity<Target>().HasData(
            new Target
            {
                Id = demoPostgresTargetId,
                Name = "Demo PostgreSQL",
                Host = "demo-postgres-target",
                NormalizedHost = "demo-postgres-target",
                Port = 5432,
                Engine = DatabaseEngine.PostgreSql,
                EnvironmentTag = "demo",
                Enabled = true,
                CreatedAt = createdAt
            },
            new Target
            {
                Id = demoMySqlTargetId,
                Name = "Demo MySQL",
                Host = "demo-mysql-target",
                NormalizedHost = "demo-mysql-target",
                Port = 3306,
                Engine = DatabaseEngine.MySql,
                EnvironmentTag = "demo",
                Enabled = true,
                CreatedAt = createdAt
            });

        modelBuilder.Entity<ScanScopeAllowlist>().HasData(
            new ScanScopeAllowlist
            {
                Id = Guid.Parse("25585dc7-f312-44ba-b01d-ef31cc312d10"),
                Name = "Demo PostgreSQL discovery scope",
                EnvironmentTag = "demo",
                ScopeValue = "demo-postgres-target",
                ScopeType = ScanScopeType.DemoTarget,
                AllowedEngines = "postgresql",
                AllowedPorts = "postgresql:5432",
                Port = 5432,
                Description = "Aspire demo PostgreSQL target",
                Enabled = true,
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            },
            new ScanScopeAllowlist
            {
                Id = Guid.Parse("aa02f7f9-7a0e-4609-9e85-53b1bda8b1e3"),
                Name = "Demo MySQL discovery scope",
                EnvironmentTag = "demo",
                ScopeValue = "demo-mysql-target",
                ScopeType = ScanScopeType.DemoTarget,
                AllowedEngines = "mysql",
                AllowedPorts = "mysql:3306",
                Port = 3306,
                Description = "Aspire demo MySQL target",
                Enabled = true,
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            });

        modelBuilder.Entity<NseScript>().HasData(
            new NseScript
            {
                Id = "banner",
                NseScriptName = "banner",
                DisplayName = "Banner",
                Description = "Generic service banner collection.",
                SupportedEngines = "generic,postgresql,mysql",
                Categories = "safe,discovery",
                DefaultEnabled = true,
                RequiresCredentials = false,
                IsIntrusive = false,
                ParserType = ParserType.GenericOutput,
                MatcherType = MatcherType.Contains,
                Severity = Severity.Info,
                Weight = 0,
                ApprovalStatus = ScriptApprovalStatus.Approved,
                Source = NseScriptSource.Installed
            },
            new NseScript
            {
                Id = "mysql-info",
                NseScriptName = "mysql-info",
                DisplayName = "MySQL Info",
                Description = "Collects safe unauthenticated MySQL service metadata where available.",
                SupportedEngines = "mysql",
                Categories = "safe,discovery",
                DefaultEnabled = true,
                RequiresCredentials = false,
                IsIntrusive = false,
                ParserType = ParserType.StructuredXml,
                MatcherType = MatcherType.Contains,
                Severity = Severity.Info,
                Weight = 0,
                ApprovalStatus = ScriptApprovalStatus.Approved,
                Source = NseScriptSource.Installed
            });

        modelBuilder.Entity<Rule>().HasData(
            new Rule
            {
                Id = "generic_db_port_open",
                Title = "DB port open",
                Description = "Database service port is reachable.",
                Engine = DatabaseEngine.Generic,
                Severity = Severity.Info,
                Weight = 0,
                Enabled = true,
                ParserType = ParserType.StructuredXml,
                MatcherType = MatcherType.Contains,
                MatcherExpression = "open",
                Remediation = "Confirm the database service is expected to be reachable from the scanner network."
            },
            new Rule
            {
                Id = "generic_service_fingerprint_detected",
                Title = "Service fingerprint detected",
                Description = "Nmap service fingerprint returned database metadata.",
                Engine = DatabaseEngine.Generic,
                Severity = Severity.Low,
                Weight = 1,
                Enabled = true,
                ParserType = ParserType.StructuredXml,
                MatcherType = MatcherType.Contains,
                MatcherExpression = "service",
                Remediation = "Reduce unnecessary service metadata exposure where possible."
            },
            new Rule
            {
                Id = "pg_service_detected",
                Title = "PostgreSQL service detected",
                Description = "PostgreSQL service is detected on the target.",
                Engine = DatabaseEngine.PostgreSql,
                Severity = Severity.Info,
                Weight = 0,
                Enabled = true,
                ParserType = ParserType.StructuredXml,
                MatcherType = MatcherType.Contains,
                MatcherExpression = "postgresql",
                Remediation = "Confirm the PostgreSQL exposure is expected."
            },
            new Rule
            {
                Id = "mysql_service_detected",
                Title = "MySQL service detected",
                Description = "MySQL service is detected on the target.",
                Engine = DatabaseEngine.MySql,
                Severity = Severity.Info,
                Weight = 0,
                Enabled = true,
                ParserType = ParserType.StructuredXml,
                MatcherType = MatcherType.Contains,
                MatcherExpression = "mysql",
                Remediation = "Confirm the MySQL exposure is expected."
            });

        modelBuilder.Entity<RuleEngineSupport>().HasData(
            Support("1ef15d1d-1b8e-485f-a852-3cfc7d4bdc5d", "generic_db_port_open", DatabaseEngine.PostgreSql),
            Support("5d232cfa-7a8f-40c8-926d-1056ce698b47", "generic_db_port_open", DatabaseEngine.MySql),
            Support("fd89de34-36ee-42ac-a16d-27a97760bc97", "generic_service_fingerprint_detected", DatabaseEngine.PostgreSql),
            Support("8c4e5d35-9b8e-44e7-bbcb-c80b0e7d14c5", "generic_service_fingerprint_detected", DatabaseEngine.MySql),
            Support("6477a0ab-d8c6-474e-b2d2-554e4d7e9e28", "pg_service_detected", DatabaseEngine.PostgreSql),
            Support("3642e3b4-ea54-4a91-9731-ded6e7ee17c7", "mysql_service_detected", DatabaseEngine.MySql));

        modelBuilder.Entity<RuleNseScript>().HasData(
            new RuleNseScript { RuleId = "generic_db_port_open", NseScriptId = "banner" },
            new RuleNseScript { RuleId = "generic_service_fingerprint_detected", NseScriptId = "banner" },
            new RuleNseScript { RuleId = "pg_service_detected", NseScriptId = "banner" },
            new RuleNseScript { RuleId = "mysql_service_detected", NseScriptId = "mysql-info" });

        modelBuilder.Entity<NseScriptMatcher>().HasData(
            Matcher("b09ad4e0-e2f9-4ea4-aea0-6bf5d433d4d7", "banner", "generic_db_port_open", DatabaseEngine.Generic, "open"),
            Matcher("46971bbd-f10f-4d30-8d0a-8d48a273854b", "banner", "pg_service_detected", DatabaseEngine.PostgreSql, "postgresql"),
            Matcher("67a5a68b-f8d1-4234-b430-8c96e1b16868", "mysql-info", "mysql_service_detected", DatabaseEngine.MySql, "mysql"));

        static RuleEngineSupport Support(string id, string ruleId, DatabaseEngine engine) => new()
        {
            Id = Guid.Parse(id),
            RuleId = ruleId,
            Engine = engine,
            UnsupportedStatus = RuleResultStatus.NotSupported
        };

        static NseScriptMatcher Matcher(string id, string scriptId, string ruleId, DatabaseEngine engine, string expression) => new()
        {
            Id = Guid.Parse(id),
            NseScriptId = scriptId,
            RuleId = ruleId,
            Engine = engine,
            ParserType = ParserType.GenericOutput,
            MatcherType = MatcherType.Contains,
            MatcherExpression = expression,
            Enabled = true
        };
    }
}
