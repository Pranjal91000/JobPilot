using FluentAssertions;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Domain.Policies;
using JobPilot.Domain.ValueObjects;

namespace JobPilot.Domain.Tests;

public class DomainUnitTests
{
    [Fact]
    public void Provenance_UserEntered_ShouldBeVerified()
    {
        var now = DateTimeOffset.UtcNow;
        var p = Provenance.UserEntered(now);

        p.Status.Should().Be(VerificationStatus.Verified);
        p.Source.Should().Be(FactSource.UserEntered);
        p.SourceDocumentId.Should().BeNull();
        p.VerifiedAt.Should().Be(now);
    }

    [Fact]
    public void Provenance_Extracted_ShouldBeUnconfirmed()
    {
        var docId = Guid.NewGuid();
        var p = Provenance.Extracted(docId);

        p.Status.Should().Be(VerificationStatus.ExtractedUnconfirmed);
        p.Source.Should().Be(FactSource.ResumeExtraction);
        p.SourceDocumentId.Should().Be(docId);
        p.VerifiedAt.Should().BeNull();
    }

    [Fact]
    public void Provenance_Confirm_ShouldPromoteToVerified()
    {
        var docId = Guid.NewGuid();
        var p = Provenance.Extracted(docId);
        var now = DateTimeOffset.UtcNow;

        var confirmed = p.Confirm(now);

        confirmed.Status.Should().Be(VerificationStatus.Verified);
        confirmed.VerifiedAt.Should().Be(now);
        confirmed.SourceDocumentId.Should().Be(docId);
    }

    [Theory]
    [InlineData(ApplicationStatus.ReadyToApply, ApplicationStatus.InProgress, ChangeSource.User, true)]
    [InlineData(ApplicationStatus.InProgress, ApplicationStatus.AwaitingUserReview, ChangeSource.Automation, true)]
    [InlineData(ApplicationStatus.AwaitingUserReview, ApplicationStatus.Submitted, ChangeSource.Detector, true)]
    [InlineData(ApplicationStatus.AwaitingUserReview, ApplicationStatus.Submitted, ChangeSource.User, true)]
    [InlineData(ApplicationStatus.AwaitingUserReview, ApplicationStatus.Submitted, ChangeSource.Automation, false)] // Rule: Automation cannot submit
    [InlineData(ApplicationStatus.Submitted, ApplicationStatus.Interview, ChangeSource.User, true)]
    [InlineData(ApplicationStatus.Submitted, ApplicationStatus.Interview, ChangeSource.Automation, false)] // Post-submit is user only
    [InlineData(ApplicationStatus.ReadyToApply, ApplicationStatus.Submitted, ChangeSource.User, false)] // Cannot skip InProgress/Review
    public void ApplicationStatusTransitions_CanTransition_ShouldFollowSpecificationRules(
        ApplicationStatus from, ApplicationStatus to, ChangeSource source, bool expected)
    {
        var result = ApplicationStatusTransitions.CanTransition(from, to, source);
        result.Should().Be(expected);
    }

    [Fact]
    public void JobApplication_ChangeStatus_Invalid_ThrowsException()
    {
        var app = new JobApplication { ApplicationUrl = "https://example.com" };
        var act = () => app.ChangeStatus(ApplicationStatus.Offer, ChangeSource.User, DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidStatusTransitionException>();
    }

    [Fact]
    public void JobApplication_ConfirmSubmission_TransitionsToSubmitted()
    {
        var app = new JobApplication { ApplicationUrl = "https://example.com" };
        var now = DateTimeOffset.UtcNow;

        app.ChangeStatus(ApplicationStatus.InProgress, ChangeSource.User, now);
        app.ChangeStatus(ApplicationStatus.AwaitingUserReview, ChangeSource.Automation, now);

        app.ConfirmSubmission(SubmissionConfirmationSource.Detector, "REF12345", now);

        app.Status.Should().Be(ApplicationStatus.Submitted);
        app.SubmissionConfirmedAt.Should().Be(now);
        app.SubmissionReference.Should().Be("REF12345");
        app.SubmissionConfirmationSource.Should().Be(SubmissionConfirmationSource.Detector);
    }

    [Theory]
    [InlineData("What is your gender?", "", SensitiveCategory.Demographic, true)]
    [InlineData("Do you have a disability?", "", SensitiveCategory.Disability, true)]
    [InlineData("Are you a protected veteran?", "", SensitiveCategory.Veteran, true)]
    [InlineData("Will you now or in future require visa sponsorship?", "", SensitiveCategory.Sponsorship, true)]
    [InlineData("I certify that all statements are true", "", SensitiveCategory.Attestation, true)]
    [InlineData("Given Name", "Personal Info", SensitiveCategory.None, false)]
    [InlineData("Phone Number", "Contact", SensitiveCategory.None, false)]
    [InlineData("Any question", "Voluntary Disclosures", SensitiveCategory.Demographic, true)]
    public void SensitiveQuestionPolicy_ShouldCorrectlyIdentifyCategories(
        string label, string section, SensitiveCategory expectedCategory, bool expectedSensitive)
    {
        var result = SensitiveQuestionPolicy.Evaluate(label, sectionTitle: section);

        result.IsSensitive.Should().Be(expectedSensitive);
        result.Category.Should().Be(expectedCategory);
    }

    [Theory]
    [InlineData(
        "https://acme.wd5.myworkdayjobs.com/en-US/AcmeCareers/job/Pune-India/Senior-NET-Developer_R-0123456",
        "https://acme.wd5.myworkdayjobs.com/acmecareers/job/pune-india/senior-net-developer_r-0123456")]
    [InlineData(
        "https://acme.wd5.myworkdayjobs.com/AcmeCareers/job/Pune-India/Senior-NET-Developer_R-0123456/apply",
        "https://acme.wd5.myworkdayjobs.com/acmecareers/job/pune-india/senior-net-developer_r-0123456")]
    [InlineData(
        "https://acme.wd5.myworkdayjobs.com/en-US/AcmeCareers/job/Pune-India/Senior-NET-Developer_R-0123456/",
        "https://acme.wd5.myworkdayjobs.com/acmecareers/job/pune-india/senior-net-developer_r-0123456")]
    public void NormalizedJobUrl_ShouldNormalizeUrlConsistently(string input, string expected)
    {
        var normalized = new NormalizedJobUrl(input);
        normalized.Value.Should().Be(expected);
    }
}

