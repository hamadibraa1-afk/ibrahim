using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tamaneen.AiGateway;
using Tamaneen.AiGateway.Deidentification;
using Tamaneen.AiGateway.Gateway;
using Tamaneen.AiGateway.Model;
using Tamaneen.SharedKernel;

namespace Tamaneen.IntegrationTests;

/// <summary>The gateway as the API host wires it, against SQL Server, with the model replaced by a stub.</summary>
[Collection(SqlServerGroup.Name)]
public sealed class AiGatewayTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sql;
    private readonly StubModel _model = new();
    private ApiFactory _api = null!;
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Tamaneen.Api.SharedResource> _host = null!;

    public AiGatewayTests(SqlServerFixture sql) => _sql = sql;

    public Task InitializeAsync()
    {
        _api = new ApiFactory(_sql);
        _host = _api.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILanguageModel>(_model)));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await _api.DisposeAsync();
    }

    [Theory]
    [InlineData("ar", "صديقي خالد قال إني ما أستاهل شي", "يبدو أن كلام <PERSON_1> جرحك. أنت تستحق اللطف. ما الذي ساعدك في مواقف مشابهة؟", "خالد")]
    [InlineData("en", "my friend Daniel said I'm worthless", "It sounds like what <PERSON_1> said really hurt. You deserve kindness. What has helped you before?", "Daniel")]
    public async Task A_call_is_answered_in_the_patients_language_and_only_metadata_is_stored(string language, string message, string modelReply, string name)
    {
        _model.Next = modelReply;
        var pseudonym = PseudonymId.New().Value;

        AiResponse response;
        using (var scope = _host.Services.CreateScope())
        {
            response = await scope.ServiceProvider.GetRequiredService<IAiGateway>()
                .AskAsync(new AiRequest(pseudonym, "companion", language, message, [], KnownIdentifiers.None), CancellationToken.None);
        }

        _model.LastRequest!.Messages.Single().Text.Should().NotContain(name);
        response.Should().BeEquivalentTo(new { Language = language, IsFallback = false, Outcome = AiCallOutcome.Succeeded });
        response.Text.Should().Contain(name);

        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Admin));
        var record = await db.AiCallRecords.SingleAsync(r => r.PseudonymId == pseudonym);
        record.Should().BeEquivalentTo(new { Purpose = "companion", Language = language, ModelId = "stub-model", Outcome = AiCallOutcome.Succeeded, InputTokens = 50, OutputTokens = 30 });
        record.PromptVersion.Should().StartWith("companion-");
    }

    [Fact]
    public async Task The_call_table_has_no_column_that_could_hold_conversation_text()
    {
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Admin));
        var columns = await db.Database.SqlQueryRaw<string>(
            "SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'ai' AND TABLE_NAME = 'AiCallRecords'").ToListAsync();

        columns.Should().BeEquivalentTo("Id", "PseudonymId", "Purpose", "PromptVersion", "ModelId", "Language", "Outcome", "LatencyMs", "InputTokens", "OutputTokens", "CreatedAtUtc");
    }

    private sealed class StubModel : ILanguageModel
    {
        public string Next { get; set; } = string.Empty;

        public LanguageModelRequest? LastRequest { get; private set; }

        public string ModelId => "stub-model";

        public Task<LanguageModelReply> CompleteAsync(LanguageModelRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new LanguageModelReply(Next, LanguageModelStop.Completed, ModelId, 50, 30));
        }
    }
}
