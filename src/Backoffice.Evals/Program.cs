using System.Text.Json;
using Backoffice.Domain.Investigations;
using Backoffice.Domain.Recommendations;
using Backoffice.Evals;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

var repoRoot = ArchitectureRepoLocator.FindRepoRoot();
var datasetPath = Path.Combine(repoRoot, "evals", "datasets", "intelligence-v1.jsonl");
var thresholdsPath = Path.Combine(repoRoot, "evals", "thresholds.yaml");

var deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
var thresholds = deserializer.Deserialize<ThresholdsConfig>(File.ReadAllText(thresholdsPath));

var taskResults = new Dictionary<string, (int Correct, int Total)>();
var abstentionExpected = new Dictionary<string, int>();
var abstentionCorrect = new Dictionary<string, int>();

foreach (var line in File.ReadLines(datasetPath))
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    using var doc = JsonDocument.Parse(line);
    var root = doc.RootElement;
    var task = root.GetProperty("task").GetString()!;
    var input = root.GetProperty("input");
    var expected = root.GetProperty("expected");

    var correct = task switch
    {
        "document_classification" => EvaluateDocumentClassification(input, expected),
        "investigation" => EvaluateInvestigation(input, expected),
        "recommendation" => EvaluateRecommendation(input, expected),
        _ => throw new NotSupportedException($"Unknown eval task '{task}'."),
    };

    var (priorCorrect, priorTotal) = taskResults.GetValueOrDefault(task, (0, 0));
    taskResults[task] = (priorCorrect + (correct ? 1 : 0), priorTotal + 1);

    if (expected.TryGetProperty("abstained", out var expectedAbstainedProp) && expectedAbstainedProp.GetBoolean())
    {
        abstentionExpected[task] = abstentionExpected.GetValueOrDefault(task) + 1;
        if (correct)
        {
            abstentionCorrect[task] = abstentionCorrect.GetValueOrDefault(task) + 1;
        }
    }
}

Console.WriteLine("Evaluation results (evals/datasets/intelligence-v1.jsonl):");
var allTasksPassed = true;
foreach (var (task, (correct, total)) in taskResults)
{
    var score = total == 0 ? 1.0 : (double)correct / total;
    var minScore = thresholds.Tasks.TryGetValue(task, out var taskThreshold) ? taskThreshold.MinScore : thresholds.OverallMinScore;
    var passed = score >= minScore;
    allTasksPassed &= passed;
    Console.WriteLine($"  {task}: {correct}/{total} = {score:0.###} (min {minScore:0.###}) {(passed ? "PASS" : "FAIL")}");
}

Console.WriteLine("Guardrails:");
foreach (var (name, guardrail) in thresholds.Guardrails)
{
    var (matchedTask, rate) = name switch
    {
        "unknown_document_abstention_rate" => ("document_classification", AbstentionRate("document_classification")),
        "ungrounded_recommendation_abstention_rate" => ("recommendation", AbstentionRate("recommendation")),
        _ => throw new NotSupportedException($"Unknown guardrail '{name}'."),
    };

    var passed = rate >= guardrail.Min;
    allTasksPassed &= passed;
    Console.WriteLine($"  {name} ({matchedTask}): {rate:0.###} (min {guardrail.Min:0.###}) {(passed ? "PASS" : "FAIL")}");
}

if (!allTasksPassed)
{
    Console.Error.WriteLine("Evaluation FAILED: one or more tasks/guardrails are below threshold.");
    return 1;
}

Console.WriteLine("Evaluation PASSED: all tasks and guardrails meet evals/thresholds.yaml.");
return 0;

double AbstentionRate(string task)
{
    var expectedCount = abstentionExpected.GetValueOrDefault(task);
    var correctCount = abstentionCorrect.GetValueOrDefault(task);
    return expectedCount == 0 ? 1.0 : (double)correctCount / expectedCount;
}

static bool EvaluateDocumentClassification(JsonElement input, JsonElement expected)
{
    var filename = input.GetProperty("filename").GetString()!;
    var contentType = input.GetProperty("content_type").GetString()!;
    var (documentType, abstained) = EvalDocumentClassifier.Classify(filename, contentType);

    var expectedType = expected.GetProperty("document_type").GetString();
    var expectedAbstained = expected.GetProperty("abstained").GetBoolean();

    return documentType == expectedType && abstained == expectedAbstained;
}

static bool EvaluateInvestigation(JsonElement input, JsonElement expected)
{
    var evidenceCount = input.GetProperty("evidence_references").GetArrayLength();
    var evidenceIds = Enumerable.Range(0, evidenceCount).Select(_ => Guid.NewGuid()).ToList();

    var findings = InvestigationEngine.Run(evidenceIds);
    var finding = findings[0];

    var findingSummary = finding.Kind == FindingKind.ConfirmedFact ? "transaction-confirmed" : "insufficient-evidence";
    var grounded = finding.Kind == FindingKind.ConfirmedFact;
    var abstained = finding.Kind == FindingKind.MissingData;

    var expectedFinding = expected.GetProperty("finding").GetString();
    var expectedGrounded = expected.GetProperty("grounded").GetBoolean();
    var expectedAbstained = expected.GetProperty("abstained").GetBoolean();

    var matches = findingSummary == expectedFinding && grounded == expectedGrounded && abstained == expectedAbstained;

    if (expected.TryGetProperty("source_count", out var sourceCountProp))
    {
        matches &= finding.EvidenceReferences.Count == sourceCountProp.GetInt32();
    }

    return matches;
}

static bool EvaluateRecommendation(JsonElement input, JsonElement expected)
{
    var findingText = input.GetProperty("finding").GetString();
    var evidenceCount = input.GetProperty("evidence_references").GetArrayLength();
    var evidenceIds = Enumerable.Range(0, evidenceCount).Select(_ => Guid.NewGuid()).ToList();

    var findings = new List<Finding>
    {
        findingText == "transaction-confirmed"
            ? new Finding(FindingKind.ConfirmedFact, "transaction-confirmed", evidenceIds)
            : new Finding(FindingKind.MissingData, "insufficient-evidence", []),
    };

    var decision = RecommendationEngine.Decide(findings, evidenceIds);

    var expectedOutcome = expected.GetProperty("outcome").GetString();
    var expectedGrounded = expected.GetProperty("grounded").GetBoolean();
    var expectedAbstained = expected.GetProperty("abstained").GetBoolean();

    var actualOutcome = decision.Outcome == RecommendationOutcome.Approve ? "APPROVE" : "ABSTAIN";
    var actualGrounded = decision.Outcome == RecommendationOutcome.Approve;
    var actualAbstained = decision.Outcome == RecommendationOutcome.Abstain;

    return actualOutcome == expectedOutcome && actualGrounded == expectedGrounded && actualAbstained == expectedAbstained;
}
