using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Tamaneen.Api.Sessions;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Clinical.Sessions;
using Tamaneen.Infrastructure.Auth;
using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests;

public sealed class SessionRoomUnitTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 13, 0, 0, TimeSpan.Zero);

    private static Appointment Booked(TestClock clock) =>
        Appointment.Book(Guid.NewGuid(), AvailabilitySlot.Create(Guid.NewGuid(), Start, 50), 1, "room", clock);

    [Fact]
    public void The_room_opens_ten_minutes_before_and_closes_at_the_booked_end()
    {
        var a = Booked(new TestClock(Start.AddDays(-2)));
        SessionWindow.IsOpen(a, Start.AddMinutes(-11), null).Should().BeFalse();
        SessionWindow.IsOpen(a, Start.AddMinutes(-10), null).Should().BeTrue();
        SessionWindow.IsOpen(a, Start.AddMinutes(50), null).Should().BeTrue();
        SessionWindow.IsOpen(a, Start.AddMinutes(51), null).Should().BeFalse();
    }

    [Fact]
    public void A_late_therapist_does_not_shorten_the_session()
    {
        var a = Booked(new TestClock(Start.AddDays(-2)));
        SessionWindow.ClosesAt(a, Start.AddMinutes(7)).Should().Be(Start.AddMinutes(57));
        SessionWindow.ClosesAt(a, Start.AddMinutes(-3)).Should().Be(Start.AddMinutes(50), "joining early does not move the end");
        SessionWindow.TherapistLate(a, Start.AddMinutes(1), null).Should().BeTrue();
        SessionWindow.TherapistLate(a, Start.AddMinutes(1), Start.AddMinutes(-2)).Should().BeFalse();
        SessionWindow.TherapistLate(a, Start.AddMinutes(-1), null).Should().BeFalse();
    }

    [Fact]
    public void Moving_for_a_late_therapist_ignores_the_24_hour_rule_but_only_after_the_start()
    {
        var clock = new TestClock(Start.AddDays(-2));
        var a = Booked(clock);
        var therapist = a.TherapistUserId;
        var later = AvailabilitySlot.Create(therapist, Start.AddDays(2), 50);

        clock.Advance(TimeSpan.FromDays(2) - TimeSpan.FromMinutes(30));
        var early = () => a.MoveAfterTherapistLate(later, clock);
        early.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.AppointmentNotChangeable);

        clock.Advance(TimeSpan.FromMinutes(45));
        a.MoveAfterTherapistLate(later, clock);
        a.StartsAtUtc.Should().Be(Start.AddDays(2));
        a.Rescheduled.Should().BeTrue();
        var other = () => a.MoveAfterTherapistLate(AvailabilitySlot.Create(Guid.NewGuid(), Start.AddDays(3), 50), clock);
        other.Should().Throw<DomainException>();
    }

    [Fact]
    public void Presence_lapses_without_a_heartbeat_and_ends_on_leave()
    {
        var clock = new TestClock(Start);
        var p = SessionPresence.Join(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SessionParticipant.Therapist, clock);
        p.IsPresent(Start.AddSeconds(40)).Should().BeTrue();
        p.IsPresent(Start.AddSeconds(46)).Should().BeFalse();
        p.Leave(clock);
        p.IsPresent(Start).Should().BeFalse();
        p.Beat(clock);
        p.IsPresent(Start).Should().BeTrue();
    }

    [Fact]
    public void Feedback_needs_something_and_a_known_rating()
    {
        SessionFeedback.ParseRating("helpful").Should().Be(3);
        SessionFeedback.ParseRating(null).Should().BeNull();
        var bad = () => SessionFeedback.ParseRating("great");
        bad.Should().Throw<DomainException>();
        var empty = () => SessionFeedback.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, false, null, new TestClock(Start));
        empty.Should().Throw<DomainException>();
        SessionFeedback.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, false, null, new TestClock(Start)).RatingCode.Should().Be("not_helpful");
    }

    [Fact]
    public void A_session_pass_is_bound_to_appointment_person_side_and_time()
    {
        var signer = new SessionPassSigner(new ServerSecrets(Options.Create(new ServerSecretsOptions { ServerKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) })));
        var appointment = Guid.NewGuid();
        var user = Guid.NewGuid();
        var pass = signer.Issue(new SessionPassClaims(appointment, user, "patient", Start.AddMinutes(15)));

        signer.Validate(pass, appointment, user, "patient", Start).UserId.Should().Be(user);
        foreach (var attempt in new Action[]
        {
            () => signer.Validate(pass, Guid.NewGuid(), user, "patient", Start),
            () => signer.Validate(pass, appointment, Guid.NewGuid(), "patient", Start),
            () => signer.Validate(pass, appointment, user, "therapist", Start),
            () => signer.Validate(pass, appointment, user, "patient", Start.AddMinutes(16)),
            () => signer.Validate(pass + "x", appointment, user, "patient", Start),
            () => signer.Validate("garbage", appointment, user, "patient", Start),
        })
        {
            attempt.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.SessionTokenInvalid);
        }
    }

    [Fact]
    public async Task The_embed_adapter_frames_a_signed_room_url_and_refuses_bad_settings()
    {
        var options = new VideoOptions { Embed = new EmbedVideoOptions { UrlTemplate = "https://video.example/{room}?jwt={token}", SigningKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) } };
        var provider = new EmbedVideoProvider(Options.Create(options));
        var room = await provider.EnsureRoomAsync(new VideoRoomRequest(Guid.NewGuid(), "abc123", Start, Start.AddMinutes(50)), default);
        var join = await provider.IssueJoinAsync(room, new VideoParticipant(Guid.NewGuid(), "patient"), DateTimeOffset.UtcNow.AddMinutes(10), default);
        join.Mode.Should().Be("embed");
        join.Url.Should().StartWith("https://video.example/abc123?jwt=ey");

        var insecure = () => new EmbedVideoProvider(Options.Create(new VideoOptions { Embed = new EmbedVideoOptions { UrlTemplate = "http://video.example/{room}?t={token}", SigningKeyBase64 = options.Embed.SigningKeyBase64 } }));
        insecure.Should().Throw<InvalidOperationException>();
        var weak = () => new EmbedVideoProvider(Options.Create(new VideoOptions { Embed = new EmbedVideoOptions { UrlTemplate = options.Embed.UrlTemplate, SigningKeyBase64 = "c2hvcnQ=" } }));
        weak.Should().Throw<InvalidOperationException>();
    }
}
