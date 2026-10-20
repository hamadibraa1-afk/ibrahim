using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Tamaneen.Crisis.Gate;
using Tamaneen.Crisis.Rules;
using Tamaneen.Hosting;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Worker.Endpoints;

public static class GateEndpoints
{
    public const string ServiceKeyHeader = "X-Tamaneen-Service-Key";
    public const int MaxTextLength = 8000;

    private static readonly HashSet<string> Sources = new(StringComparer.Ordinal) { "intake", "chat", "journal", "survey", "questionnaire", "help_now" };

    public sealed record EvaluateRequest(Guid PatientId, string? Language, string? Text, AnswersDto? Answers, string? Source);

    public sealed record AnswersDto(string Instrument, Dictionary<int, int> Scores);

    public sealed record EvaluateResponse(string Decision, int Level, Guid? CrisisEventId, string? Script, bool ChatLocked, IReadOnlyList<string> RuleIds, string Dir);

    public static IEndpointRouteBuilder MapGateEndpoints(this IEndpointRouteBuilder app)
    {
        // Service-to-service only: Tamaneen.Api calls this for every patient message before doing anything else.
        app.MapPost("/internal/v1/gate/evaluate", EvaluateAsync).AllowAnonymous().WithName("EvaluateMessage");
        return app;
    }

    internal static async Task<Results<Ok<EvaluateResponse>, UnauthorizedHttpResult, ValidationProblem>> EvaluateAsync(
        HttpContext http,
        EvaluateRequest request,
        RequestCurrentUser user,
        IOptions<WorkerOptions> options,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!ServiceKeyMatches(http.Request.Headers[ServiceKeyHeader].ToString(), options.Value.ServiceKeyBase64))
        {
            return TypedResults.Unauthorized();
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // From here on the request runs as the crisis service: it must see the patient's rows to protect them.
        user.ActAsService(options.Value.ServiceUserId, AppRoles.CrisisService);
        var gate = services.GetRequiredService<CrisisGate>();
        var answers = request.Answers is null ? null : new QuestionnaireAnswers(request.Answers.Instrument, request.Answers.Scores);
        var language = SupportedLanguages.Normalize(request.Language);
        var result = await gate.EvaluateAsync(new GateRequest(request.PatientId, language, request.Text, answers, request.Source!), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new EvaluateResponse(
            result.Decision == GateDecision.Pass ? "pass" : "blocked",
            (int)result.Level,
            result.CrisisEventId,
            result.Script,
            result.ChatLocked,
            result.RuleIds,
            SupportedLanguages.Direction(language)));
    }

    private static Dictionary<string, string[]> Validate(EvaluateRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.PatientId == Guid.Empty)
        {
            errors["patientId"] = new[] { "required" };
        }

        if (request.Source is null || !Sources.Contains(request.Source))
        {
            errors["source"] = new[] { $"one of: {string.Join(", ", Sources)}" };
        }

        if (request.Text is { Length: > MaxTextLength })
        {
            errors["text"] = new[] { $"at most {MaxTextLength} characters" };
        }

        if (string.IsNullOrWhiteSpace(request.Text) && request.Answers is null && request.Source != "help_now")
        {
            errors["text"] = new[] { "text or answers is required" };
        }

        return errors;
    }

    internal static bool ServiceKeyMatches(string presented, string configuredBase64)
    {
        if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(configuredBase64))
        {
            return false;
        }

        // Compare fixed-length digests in constant time so the key length and content leak nothing.
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(configuredBase64));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
