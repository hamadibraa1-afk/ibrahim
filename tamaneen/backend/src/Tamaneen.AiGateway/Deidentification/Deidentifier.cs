using System.Text;
using Microsoft.Extensions.Logging;

namespace Tamaneen.AiGateway.Deidentification;

public sealed record DeidentifiedText(string Text, int ReplacedCount);

/// <summary>
/// Runs every registered detector, merges overlapping findings and swaps each span for its placeholder.
/// The in-process pattern detector is mandatory; extra detectors (Presidio) are best effort and a failure of
/// one of them only loses its extra recall. If the mandatory detector fails, the caller must not send the text.
/// </summary>
public sealed partial class Deidentifier
{
    private readonly PatternPiiDetector required;
    private readonly IReadOnlyList<IPiiDetector> optional;
    private readonly ILogger<Deidentifier> logger;

    public Deidentifier(PatternPiiDetector required, IEnumerable<IPiiDetector> optional, ILogger<Deidentifier> logger)
    {
        this.required = required;
        this.optional = optional.Where(d => d is not PatternPiiDetector).ToList();
        this.logger = logger;
    }

    public async Task<DeidentifiedText> DeidentifyAsync(string text, string language, KnownIdentifiers known, PiiTokenMap map, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(map);
        if (text.Length == 0)
        {
            return new DeidentifiedText(text, 0);
        }

        text = PiiTokenMap.Escape(text);
        var spans = new List<PiiSpan>(await required.DetectAsync(text, language, known, cancellationToken).ConfigureAwait(false));
        foreach (var detector in optional)
        {
            try
            {
                spans.AddRange(await detector.DetectAsync(text, language, known, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogDetectorFailed(logger, detector.GetType().Name, ex.GetType().Name);
            }
        }

        var merged = Merge(spans.Where(s => s.Start >= 0 && s.Length > 0 && s.End <= text.Length));
        var builder = new StringBuilder(text.Length);
        var cursor = 0;
        foreach (var span in merged)
        {
            builder.Append(text, cursor, span.Start - cursor);
            builder.Append(map.TokenFor(text.Substring(span.Start, span.Length), span.Type));
            cursor = span.End;
        }

        builder.Append(text, cursor, text.Length - cursor);
        return new DeidentifiedText(builder.ToString(), merged.Count);
    }

    /// <summary>
    /// Overlapping spans become one span covering both. Its type is the one of the most confident finding,
    /// longer spans winning ties ("Dr. Sara Ali" beats the single word "Sara").
    /// </summary>
    internal static IReadOnlyList<PiiSpan> Merge(IEnumerable<PiiSpan> spans)
    {
        var result = new List<PiiSpan>();
        foreach (var span in spans.OrderBy(s => s.Start).ThenByDescending(s => s.Length))
        {
            if (result.Count > 0 && span.Start < result[^1].End)
            {
                var last = result[^1];
                var winner = span.Score > last.Score || (span.Score.Equals(last.Score) && span.Length > last.Length) ? span : last;
                var end = Math.Max(last.End, span.End);
                result[^1] = new PiiSpan(last.Start, end - last.Start, winner.Type, Math.Max(last.Score, span.Score));
            }
            else
            {
                result.Add(span);
            }
        }

        return result;
    }

    [LoggerMessage(EventId = 3101, Level = LogLevel.Warning, Message = "Optional PII detector {Detector} failed ({Error}); continuing with the built-in recognizers.")]
    private static partial void LogDetectorFailed(ILogger logger, string detector, string error);
}
