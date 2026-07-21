using System.Reflection;
using System.Text.RegularExpressions;
using WAID.Application.Services;
using WAID.Diagnosis;
using WAID.Domain.Diagnostics;
using WAID.EventAnalysis;
using WAID.Health;
using WAID.Infrastructure;
using WAID.KnowledgeBase;

namespace WAID.Infrastructure.Tests;

public sealed class ArchitectureDependencyTests
{
    private static readonly IReadOnlyDictionary<string, string[]> Allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["WAID.Domain"] = [],
        ["WAID.EventAnalysis"] = ["WAID.Domain"],
        ["WAID.Health"] = ["WAID.Domain"],
        ["WAID.KnowledgeBase"] = ["WAID.Domain"],
        ["WAID.Diagnosis"] = ["WAID.Domain", "WAID.EventAnalysis", "WAID.Health", "WAID.KnowledgeBase"],
        ["WAID.Application"] = ["WAID.Domain", "WAID.Diagnosis", "WAID.EventAnalysis", "WAID.Health"],
        ["WAID.Infrastructure"] = ["WAID.Application", "WAID.Domain", "WAID.Diagnosis", "WAID.EventAnalysis", "WAID.Health", "WAID.KnowledgeBase"]
    };

    [Fact]
    public void Production_project_references_follow_the_documented_dependency_rule()
    {
        Assembly[] assemblies = [typeof(DiagnosticFinding).Assembly, typeof(EventCorrelationEngine).Assembly,
            typeof(HealthScoreEngine).Assembly, typeof(DiagnosticKnowledgeBase).Assembly, typeof(DiagnosisEngine).Assembly,
            typeof(ScanOrchestrator).Assembly, typeof(DependencyInjection).Assembly];

        foreach (var assembly in assemblies)
        {
            var actual = assembly.GetReferencedAssemblies().Select(reference => reference.Name!)
                .Where(name => name.StartsWith("WAID.", StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            Assert.Equal(Allowed[assembly.GetName().Name!].OrderBy(name => name, StringComparer.Ordinal), actual);
        }
    }

    [Fact]
    public void Production_dependency_graph_is_acyclic()
    {
        foreach (var project in Allowed.Keys) Visit(project, [], []);
    }

    [Fact]
    public void ViewModels_and_application_services_do_not_use_a_service_locator()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src", "WAID.Application"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src", "WAID.Desktop", "ViewModels"), "*.cs", SearchOption.AllDirectories));
        var pattern = new Regex(@"\bIServiceProvider\b|\bGetRequiredService\s*\(|\bGetService\s*\(", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        foreach (var file in files) Assert.DoesNotMatch(pattern, File.ReadAllText(file));
    }

    private static void Visit(string project, HashSet<string> visited, HashSet<string> active)
    {
        if (active.Contains(project)) throw new Xunit.Sdk.XunitException($"Circular production dependency detected at {project}.");
        if (!visited.Add(project)) return;
        active.Add(project);
        foreach (var dependency in Allowed[project]) Visit(dependency, visited, active);
        active.Remove(project);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WindowsAIDoctor.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root could not be located.");
    }
}
