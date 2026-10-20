using System.Text.Json;
using Tamaneen.Crisis.Rules;
using Tamaneen.SharedKernel;
using Xunit.Abstractions;

namespace Tamaneen.UnitTests.Crisis;

/// <summary>
/// The release gate for rule-pack changes. Thresholds are deliberately strict on misses and tolerant of
/// over-triggering: a false alarm costs a clinician a few minutes, a miss can cost a life.
/// </summary>
public class CrisisEvaluationSetTests
{
    private const double MinCrisisRecall = 1.0;     // every High/Critical case is detected at High or above
    private const double MinCriticalRecall = 0.95;  // Critical cases are detected as Critical
    private const double MaxFalsePositiveRate = 0.05;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ITestOutputHelper _output;

    public CrisisEvaluationSetTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Rule_packs_meet_the_release_thresholds()
    {
        var evaluator = CrisisRuleCatalog.CreateEvaluator();
        using var stream = CrisisRuleCatalog.OpenEvaluationSet();
        var set = JsonSerializer.Deserialize<EvalSet>(stream, JsonOptions)!;
        set.Cases.Should().HaveCountGreaterThan(150);

        var results = set.Cases.Select(c => (Case: c, Expected: Enum.Parse<RiskLevel>(c.Expected), Actual: evaluator.Assess(c.Text).Level)).ToList();
        var crisis = results.Where(r => r.Expected >= RiskLevel.High).ToList();
        var critical = results.Where(r => r.Expected == RiskLevel.Critical).ToList();
        var benign = results.Where(r => r.Expected == RiskLevel.None).ToList();

        var missed = crisis.Where(r => r.Actual < RiskLevel.High).ToList();
        var underRated = critical.Where(r => r.Actual < RiskLevel.Critical).ToList();
        var falseAlarms = benign.Where(r => r.Actual >= RiskLevel.High).ToList();

        var crisisRecall = 1.0 - (double)missed.Count / crisis.Count;
        var criticalRecall = 1.0 - (double)underRated.Count / critical.Count;
        var fpr = (double)falseAlarms.Count / benign.Count;

        _output.WriteLine($"cases={results.Count} crisis={crisis.Count} critical={critical.Count} benign={benign.Count}");
        _output.WriteLine($"crisis recall={crisisRecall:P1} critical recall={criticalRecall:P1} false-positive rate={fpr:P1}");
        foreach (var variety in results.GroupBy(r => r.Case.Variety))
        {
            var c = variety.Where(r => r.Expected >= RiskLevel.High).ToList();
            _output.WriteLine($"  {variety.Key}: {c.Count(r => r.Actual >= RiskLevel.High)}/{c.Count} crisis detected");
        }

        string List(IEnumerable<(EvalCase Case, RiskLevel Expected, RiskLevel Actual)> rows) =>
            string.Join("\n", rows.Select(r => $"  [{r.Case.Variety}] expected {r.Expected}, got {r.Actual}: {r.Case.Text}"));

        crisisRecall.Should().BeGreaterThanOrEqualTo(MinCrisisRecall, "missed crisis cases:\n" + List(missed));
        criticalRecall.Should().BeGreaterThanOrEqualTo(MinCriticalRecall, "critical cases rated too low:\n" + List(underRated));
        fpr.Should().BeLessThanOrEqualTo(MaxFalsePositiveRate, "false alarms:\n" + List(falseAlarms));
    }

    private sealed class EvalSet
    {
        public List<EvalCase> Cases { get; set; } = new();
    }

    private sealed class EvalCase
    {
        public string Text { get; set; } = string.Empty;

        public string Variety { get; set; } = string.Empty;

        public string Expected { get; set; } = string.Empty;
    }
}
