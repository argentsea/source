using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec sections argent-sea, argent-sea-core, sql-server-provider-package, postgres-provider-package,
    /// orleans-grain-storage-package, orleans-sql-server-package and the Orleans PostgreSQL package: the invariants about the solution file and the six
    /// product project files, asserted by parsing them as XML. Each theory row names the project file it checks.
    /// </summary>
    public class PackageInvariantTests
    {
        private const string CoreProject = "ArgentSea.Shared/src/ArgentSea.csproj";
        private const string SqlProject = "ArgentSea.Sql/src/ArgentSea.Sql.csproj";
        private const string PgProject = "ArgentSea.Pg/src/ArgentSea.Pg.csproj";
        private const string OrleansProject = "ArgentSea.Orleans/src/ArgentSea.Orleans.csproj";
        private const string OrleansSqlProject = "ArgentSea.Orleans.Sql/src/ArgentSea.Orleans.Sql.csproj";
        private const string OrleansPgProject = "ArgentSea.Orleans.Pg/src/ArgentSea.Orleans.Pg.csproj";
        private const string TestAssemblyGrant = "$(MSBuildProjectName).Test";

        private static readonly string[] ProductProjects = { CoreProject, SqlProject, PgProject, OrleansProject, OrleansSqlProject, OrleansPgProject };

        private static readonly Lazy<string> RepositoryRoot = new Lazy<string>(() =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ArgentSea.slnx")))
            {
                directory = directory.Parent;
            }
            return directory?.FullName ?? throw new InvalidOperationException("ArgentSea.slnx was not found above the test assembly.");
        });

        private static XDocument Load(string relativePath) => XDocument.Load(Path.Combine(RepositoryRoot.Value, relativePath));

        /// <summary>A property declaration with the condition that applies to it (its own or its PropertyGroup's).</summary>
        private sealed record Property(string Name, string Value, string? Condition);

        private static IReadOnlyList<Property> Properties(XDocument project, string name)
            => project.Root!.Elements("PropertyGroup")
                .SelectMany(g => g.Elements(name).Select(e => new Property(name, e.Value.Trim(), (string?)e.Attribute("Condition") ?? (string?)g.Attribute("Condition"))))
                .ToList();

        /// <summary>The single unconditioned value of a property, failing when it is missing, conditioned or declared more than once.</summary>
        private static string Single(XDocument project, string name)
        {
            var declarations = Properties(project, name);
            declarations.Should().ContainSingle($"{name} is declared exactly once").Which.Condition.Should().BeNull($"{name} is unconditioned");
            return declarations[0].Value;
        }

        private sealed record Item(string Include, string? Version, string? Condition, XElement Element);

        private static IReadOnlyList<Item> Items(XDocument project, string itemType)
            => project.Root!.Elements("ItemGroup")
                .SelectMany(g => g.Elements(itemType).Select(e => new Item(
                    (string?)e.Attribute("Include") ?? string.Empty,
                    (string?)e.Attribute("Version") ?? e.Element("Version")?.Value,
                    (string?)e.Attribute("Condition") ?? (string?)g.Attribute("Condition"),
                    e)))
                .ToList();

        private static IReadOnlyDictionary<string, string?> PackageReferences(XDocument project)
        {
            var items = Items(project, "PackageReference");
            items.Should().OnlyContain(i => i.Condition == null, "package references are declared unconditionally");
            return items.ToDictionary(i => i.Include, i => i.Version);
        }

        private static IReadOnlyList<string> ProjectReferences(XDocument project)
            => Items(project, "ProjectReference").Select(i => Path.GetFileName(i.Include.Replace('\\', '/'))).ToList();

        private static IReadOnlyList<string> InternalsVisibleTo(XDocument project)
            => Items(project, "InternalsVisibleTo").Select(i => i.Include).ToList();

        private static void AssertPacked(XDocument project, string file)
        {
            var item = Items(project, "None").Should().ContainSingle(i => i.Include == file, $"{file} is packed").Subject;
            ((string?)item.Element.Attribute("Pack")).Should().BeEquivalentTo("true");
            ((string?)item.Element.Attribute("PackagePath")).Should().Be("\\", $"{file} is packed at the package root");
        }

        #region argent-sea: the solution file

        private static XDocument Solution => Load("ArgentSea.slnx");

        private static IReadOnlyList<XElement> SolutionProjects => Solution.Root!.Elements("Project").ToList();

        private static string ProjectName(XElement project) => Path.GetFileNameWithoutExtension(((string)project.Attribute("Path")!).Replace('\\', '/'));

        [Fact]
        [Trait("Spec", "blk-642b4e9041b64a96a018c2c5e13b2f9b")]
        public void TheSolutionHoldsTheSixProductProjectsEachWithOneDedicatedTestProject()
        {
            var names = SolutionProjects.Select(ProjectName).ToList();

            var products = new[] { "ArgentSea", "ArgentSea.Sql", "ArgentSea.Pg", "ArgentSea.Orleans", "ArgentSea.Orleans.Sql", "ArgentSea.Orleans.Pg" };
            names.Should().Contain(products);
            names.Should().Contain(products.Select(p => p + ".Test"), "each product project has a dedicated test project");
            // The two shared test harness projects added by the coverage work are the only other members.
            names.Except(products).Except(products.Select(p => p + ".Test")).Should().BeEquivalentTo(new[] { "ArgentSea.TestKit", "ArgentSea.Integration.Test" });
            names.Should().OnlyHaveUniqueItems();
            foreach (var project in SolutionProjects)
            {
                File.Exists(Path.Combine(RepositoryRoot.Value, (string)project.Attribute("Path")!)).Should().BeTrue($"{ProjectName(project)} exists on disk");
            }
        }

        [Fact]
        [Trait("Spec", "blk-d448f3ce7fdc4737973d92d770542285")]
        public void OnlyTheOrleansProviderPackagesDeclareBuildDependenciesAndReleaseBuildTypeIsDeclaredForProductsAndTheCoreTests()
        {
            var withDependencies = SolutionProjects.Where(p => p.Elements("BuildDependency").Any()).ToList();
            withDependencies.Select(ProjectName).Should().BeEquivalentTo(new[] { "ArgentSea.Orleans.Sql", "ArgentSea.Orleans.Pg" });
            string[] Dependencies(string name) => withDependencies.Single(p => ProjectName(p) == name).Elements("BuildDependency")
                .Select(d => Path.GetFileNameWithoutExtension(((string)d.Attribute("Project")!).Replace('\\', '/'))).ToArray();
            Dependencies("ArgentSea.Orleans.Sql").Should().BeEquivalentTo(new[] { "ArgentSea.Orleans", "ArgentSea.Sql" });
            Dependencies("ArgentSea.Orleans.Pg").Should().BeEquivalentTo(new[] { "ArgentSea.Orleans", "ArgentSea.Pg" });

            var release = SolutionProjects.Where(p => p.Elements("BuildType").Any(b => (string?)b.Attribute("Project") == "Release")).Select(ProjectName);
            release.Should().BeEquivalentTo(new[] { "ArgentSea", "ArgentSea.Sql", "ArgentSea.Pg", "ArgentSea.Orleans", "ArgentSea.Orleans.Sql", "ArgentSea.Orleans.Pg", "ArgentSea.Test" });
        }

        #endregion

        #region Invariants shared by every product project

        public static IEnumerable<object[]> EveryProductProject => ProductProjects.Select(p => new object[] { p });

        [Theory]
        [MemberData(nameof(EveryProductProject))]
        [Trait("Spec", "blk-10f9b8096ab847a6aeef176265cc5056")]
        [Trait("Spec", "blk-3e123f837b6a41cab66454c8f256160f")]
        [Trait("Spec", "blk-546540800dec48db88a259cdfb7e2a76")]
        [Trait("Spec", "blk-fa3bc0981d6d4da7b945b3149ec9a309")]
        public void EveryProductProjectTargetsNet10WithNullableEnabledAndPublishesAVersionedMitPackageWithSymbols(string projectPath)
        {
            var project = Load(projectPath);

            Single(project, "TargetFramework").Should().Be("net10.0");
            Single(project, "Nullable").Should().Be("enable");
            Single(project, "Version").Should().Be("2.0.4");
            Single(project, "PackageLicenseExpression").Should().Be("MIT");
            Single(project, "PackageIcon").Should().Be("argentsea-logo.png");
            Single(project, "GeneratePackageOnBuild").Should().BeEquivalentTo("true");
            Single(project, "Title").Should().NotBeNullOrWhiteSpace();
            Single(project, "Description").Should().NotBeNullOrWhiteSpace();
            Single(project, "Copyright").Should().Be("© John Hicks");
            // IncludeSymbols / SymbolPackageFormat may be declared more than once (the core project repeats them literally); every declaration must agree.
            Properties(project, "IncludeSymbols").Should().NotBeEmpty().And.OnlyContain(p => p.Condition == null && p.Value.Equals("true", StringComparison.OrdinalIgnoreCase));
            Properties(project, "SymbolPackageFormat").Should().NotBeEmpty().And.OnlyContain(p => p.Condition == null && p.Value == "snupkg");
            AssertPacked(project, "argentsea-logo.png");
            AssertPacked(project, "readme.md");
        }

        [Theory]
        [MemberData(nameof(EveryProductProject))]
        [Trait("Spec", "blk-c43ece75687a45afb0bae1ed2cc7a7cf")]
        [Trait("Spec", "blk-1bf2e9b856024458aca7786aafa4c318")]
        [Trait("Spec", "blk-9889eb3ca10942edae2414b68ff4be42")]
        [Trait("Spec", "blk-3e123f837b6a41cab66454c8f256160f")]
        public void EveryProductProjectGrantsInternalsToItsOwnTestAssembly(string projectPath)
        {
            InternalsVisibleTo(Load(projectPath)).Should().Contain(TestAssemblyGrant);
        }

        #endregion

        #region argent-sea-core

        [Fact]
        [Trait("Spec", "blk-10f9b8096ab847a6aeef176265cc5056")]
        public void CorePackageVersionIsTrackedIndependentlyOfItsAssemblyVersion()
        {
            var project = Load(CoreProject);

            Path.GetFileNameWithoutExtension(CoreProject).Should().Be("ArgentSea", "the package id is the project name");
            Properties(project, "AssemblyName").Should().BeEmpty("the assembly is named after the project");
            Single(project, "Title").Should().Be("ArgentSea core library");
            Single(project, "AssemblyVersion").Should().Be("2.0.0.0");
            Single(project, "FileVersion").Should().Be("2.0.0.0");
            Single(project, "Version").Should().NotBe("2.0.0.0", "the package version moves independently of the assembly version");
        }

        [Fact]
        [Trait("Spec", "blk-12002539c042447ab39231eb4524b990")]
        [Trait("Spec", "blk-90f700c00b3c44629a1e61bfa8bf08cb")]
        public void CoreAndSqlOptimizeReleaseBuildsAndNotDebugBuilds()
        {
            foreach (var path in new[] { CoreProject, SqlProject })
            {
                var optimize = Properties(Load(path), "Optimize");
                optimize.Should().HaveCount(2, $"{path} declares Optimize for exactly two configuration/platform combinations");
                optimize.Should().ContainSingle(p => p.Condition == "'$(Configuration)|$(Platform)'=='Release|AnyCPU'").Which.Value.Should().BeEquivalentTo("true", $"{path} optimizes Release builds");
                optimize.Should().ContainSingle(p => p.Condition == "'$(Configuration)|$(Platform)'=='Debug|AnyCPU'").Which.Value.Should().BeEquivalentTo("false", $"{path} does not optimize Debug builds");
            }
        }

        [Fact]
        [Trait("Spec", "blk-c43ece75687a45afb0bae1ed2cc7a7cf")]
        public void CoreGrantsInternalsOnlyToItsTestAssembly()
        {
            InternalsVisibleTo(Load(CoreProject)).Should().Equal(TestAssemblyGrant);
        }

        [Fact]
        [Trait("Spec", "blk-ebdfb0922b5147a2aacc35bebc2fe2c9")]
        public void CoreDeclaresExactlyItsPinnedDependenciesWithPollyTheOnlyNonMicrosoftOne()
        {
            var packages = PackageReferences(Load(CoreProject));

            packages.Should().BeEquivalentTo(new Dictionary<string, string?>
            {
                ["Microsoft.CSharp"] = "4.7.0",
                ["Microsoft.Extensions.Configuration"] = "10.0.12",
                ["Microsoft.Extensions.Logging"] = "10.0.12",
                ["Microsoft.Extensions.Options"] = "10.0.12",
                ["Microsoft.Extensions.Options.ConfigurationExtensions"] = "10.0.12",
                ["Polly"] = "8.7.0",
                ["System.Collections.Immutable"] = "10.0.12",
            });
            packages.Keys.Where(k => !k.StartsWith("Microsoft.", StringComparison.Ordinal) && !k.StartsWith("System.", StringComparison.Ordinal)).Should().Equal("Polly");
            ProjectReferences(Load(CoreProject)).Should().BeEmpty("the core depends on no other ArgentSea project");
        }

        #endregion

        #region sql-server-provider-package

        [Fact]
        [Trait("Spec", "blk-355413da27f54806a6fab98cc5dc02fc")]
        [Trait("Spec", "blk-c5de2d8847b04c5e8682009ffeae03c0")]
        public void SqlDependsOnSqlClientLoggingAndTheCoreUnconditionally()
        {
            var project = Load(SqlProject);

            PackageReferences(project).Should().BeEquivalentTo(new Dictionary<string, string?>
            {
                ["Microsoft.Data.SqlClient"] = "7.0.3",
                ["Microsoft.Extensions.Logging"] = "10.0.12",
            });
            Items(project, "PackageReference").Single(i => i.Include == "Microsoft.Data.SqlClient").Condition.Should().BeNull("the SqlClient reference carries no Condition");
            ProjectReferences(project).Should().Equal("ArgentSea.csproj");
        }

        [Fact]
        [Trait("Spec", "blk-fa3bc0981d6d4da7b945b3149ec9a309")]
        public void SqlPinsItsAssemblyVersionAndPacksItsReadmeAndIcon()
        {
            var project = Load(SqlProject);

            Single(project, "AssemblyVersion").Should().Be("2.0.0.0");
            Single(project, "FileVersion").Should().Be("2.0.0.0");
            Single(project, "IncludeSymbols").Should().BeEquivalentTo("true");
            Single(project, "SymbolPackageFormat").Should().Be("snupkg");
        }

        [Fact]
        [Trait("Spec", "blk-d6d3d68dcb7e43fc98ee6052f1c330f8")]
        [Trait("Spec", "blk-1bf2e9b856024458aca7786aafa4c318")]
        public void SqlGrantsInternalsToItsTestAssemblyAndTheIntegrationTests()
        {
            // The spec recorded a single grant; the integration test project (phase 4) is the one addition.
            InternalsVisibleTo(Load(SqlProject)).Should().BeEquivalentTo(new[] { TestAssemblyGrant, "ArgentSea.Integration.Test" });
        }

        #endregion

        #region postgres-provider-package

        [Fact]
        [Trait("Spec", "blk-96f6f094e7c5415183fd216c44b77c35")]
        public void PgDependsOnNpgsqlAsItsOnlyDatabaseClient()
        {
            var project = Load(PgProject);

            PackageReferences(project).Should().BeEquivalentTo(new Dictionary<string, string?>
            {
                ["Microsoft.Extensions.Configuration"] = "10.0.12",
                ["Microsoft.Extensions.Logging"] = "10.0.12",
                ["Npgsql"] = "10.0.3",
            });
            ProjectReferences(project).Should().Equal("ArgentSea.csproj");
            PackageReferences(project).Keys.Should().NotContain(k => k.Contains("SqlClient", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        [Trait("Spec", "blk-2376ac2a538d42b790c174ea0935a68f")]
        [Trait("Spec", "blk-414fb7fbb20f4d139b6c95c168512330")]
        [Trait("Spec", "blk-9889eb3ca10942edae2414b68ff4be42")]
        public void PgPacksASeparateSnupkgSymbolPackageOnBuildAndGrantsInternalsOnlyToItsTests()
        {
            var project = Load(PgProject);

            Single(project, "GeneratePackageOnBuild").Should().BeEquivalentTo("true");
            Single(project, "IncludeSymbols").Should().BeEquivalentTo("true");
            Single(project, "SymbolPackageFormat").Should().Be("snupkg");
            InternalsVisibleTo(project).Should().Equal(TestAssemblyGrant);
        }

        #endregion

        #region orleans-grain-storage-package

        [Fact]
        [Trait("Spec", "blk-d9e5aef9e5504b31a8cd3f4f5f0cd8b0")]
        [Trait("Spec", "blk-10084c48c0764214be41173256ac7357")]
        public void OrleansDependsOnOrleansPackagesAndTheCoreAndOnNoDatabasePlatform()
        {
            var project = Load(OrleansProject);

            PackageReferences(project).Should().BeEquivalentTo(new Dictionary<string, string?>
            {
                ["Microsoft.Orleans.Reminders"] = "10.3.1",
                ["Microsoft.Orleans.Runtime"] = "10.3.1",
                ["Microsoft.Orleans.Sdk"] = "10.3.1",
            });
            ProjectReferences(project).Should().Equal("ArgentSea.csproj");
            var everyReference = PackageReferences(project).Keys.Concat(ProjectReferences(project));
            everyReference.Should().NotContain(r => r.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) || r.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                || r.Contains("ArgentSea.Sql", StringComparison.OrdinalIgnoreCase) || r.Contains("ArgentSea.Pg", StringComparison.OrdinalIgnoreCase));
            InternalsVisibleTo(project).Should().Equal(TestAssemblyGrant);
        }

        [Fact]
        [Trait("Spec", "blk-546540800dec48db88a259cdfb7e2a76")]
        public void OrleansManifestDeclaresItsIdentityOnce()
        {
            var project = Load(OrleansProject);

            Single(project, "ImplicitUsings").Should().Be("enable");
            Single(project, "AssemblyVersion").Should().Be("2.0.0.0");
            Single(project, "FileVersion").Should().Be("2.0.0.0");
            Single(project, "Title").Should().Be("ArgentSea core library for Microsoft Orleans persistence");
            Single(project, "Description").Should().Be("Abstractions which enable ArgentSea to serve as a data provider for Microsoft Orleans.");
        }

        #endregion

        #region orleans-sql-server-package

        [Fact]
        [Trait("Spec", "blk-3e123f837b6a41cab66454c8f256160f")]
        public void OrleansSqlManifestDeclaresEachSettingOnceAndNoAssemblyVersion()
        {
            var project = Load(OrleansSqlProject);

            Single(project, "ImplicitUsings").Should().Be("enable");
            Properties(project, "AssemblyVersion").Should().BeEmpty();
            Properties(project, "FileVersion").Should().BeEmpty();
            Single(project, "Title").Should().Be("ArgentSea Microsoft Orleans persistence for SQL Server");
            Single(project, "IncludeSymbols").Should().BeEquivalentTo("true");
            Single(project, "SymbolPackageFormat").Should().Be("snupkg");
            InternalsVisibleTo(project).Should().Equal(TestAssemblyGrant);
        }

        [Fact]
        [Trait("Spec", "blk-5053da46df184628a6864355f3cfd8b0")]
        [Trait("Spec", "blk-63bb73ed85f646cfaf161b5676410bc6")]
        public void OrleansSqlDependsOnTheThreeOrleansPackagesAndBothTheOrleansAndSqlProjects()
        {
            var project = Load(OrleansSqlProject);

            PackageReferences(project).Should().BeEquivalentTo(new Dictionary<string, string?>
            {
                ["Microsoft.Orleans.Reminders"] = "10.3.1",
                ["Microsoft.Orleans.Runtime"] = "10.3.1",
                ["Microsoft.Orleans.Sdk"] = "10.3.1",
            });
            ProjectReferences(project).Should().BeEquivalentTo(new[] { "ArgentSea.Orleans.csproj", "ArgentSea.Sql.csproj" });
        }

        [Fact]
        [Trait("Spec", "blk-2c4d613b2b0443ffa64bf739405ec9a7")]
        public void OrleansPgMatchesOrleansSqlVersionsAndDependsOnTheOrleansAndPgProjects()
        {
            var project = Load(OrleansPgProject);
            var sqlCounterpart = Load(OrleansSqlProject);

            Single(project, "Version").Should().Be(Single(sqlCounterpart, "Version"), "the two Orleans provider packages are versioned together");
            PackageReferences(project).Should().BeEquivalentTo(PackageReferences(sqlCounterpart), "both provider packages build on the same Orleans release");
            ProjectReferences(project).Should().BeEquivalentTo(new[] { "ArgentSea.Orleans.csproj", "ArgentSea.Pg.csproj" });
        }

        [Fact]
        [Trait("Spec", "blk-2c4d613b2b0443ffa64bf739405ec9a7")]
        public void OrleansPgPacksItsIconReadmeAndBothSchemaScripts()
        {
            var project = Load(OrleansPgProject);

            AssertPacked(project, "argentsea-logo.png");
            AssertPacked(project, "readme.md");
            foreach (var script in new[] { "common.psql", "tenant.psql" })
            {
                var item = Items(project, "None").Should().ContainSingle(i => i.Include == script, $"{script} is packed").Subject;
                ((string?)item.Element.Attribute("Pack")).Should().BeEquivalentTo("true");
                ((string?)item.Element.Attribute("PackagePath")).Should().Be("sql\\", "the schema scripts ship in the package's sql folder");
            }
            foreach (var file in new[] { "readme.md", "common.psql", "tenant.psql", "argentsea-logo.png" })
            {
                File.Exists(Path.Combine(RepositoryRoot.Value, Path.GetDirectoryName(OrleansPgProject)!, file)).Should().BeTrue($"{file} is in the project folder");
            }
        }

        [Fact]
        [Trait("Spec", "blk-cac267d8b32b44c4b680141e3226b8ff")]
        public void OrleansSqlPacksItsIconAndReadme()
        {
            // The spec recorded the absence of a packed readme; the project folder has one, so it is now packed like every sibling's.
            var project = Load(OrleansSqlProject);

            AssertPacked(project, "argentsea-logo.png");
            AssertPacked(project, "readme.md");
            File.Exists(Path.Combine(RepositoryRoot.Value, Path.GetDirectoryName(OrleansSqlProject)!, "readme.md")).Should().BeTrue();
        }

        #endregion
    }
}
