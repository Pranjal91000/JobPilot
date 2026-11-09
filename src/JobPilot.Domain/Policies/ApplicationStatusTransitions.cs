using JobPilot.Domain.Enums;

namespace JobPilot.Domain.Policies;

public class InvalidStatusTransitionException : Exception
{
    public ApplicationStatus From { get; }
    public ApplicationStatus To { get; }
    public ChangeSource TransitionSource { get; }

    public InvalidStatusTransitionException(ApplicationStatus from, ApplicationStatus to, ChangeSource source)
        : base($"Invalid status transition from '{from}' to '{to}' with source '{source}'.")
    {
        From = from;
        To = to;
        TransitionSource = source;
    }
}

public static class ApplicationStatusTransitions
{
    private static readonly Dictionary<ApplicationStatus, ApplicationStatus[]> Allowed = new()
    {
        [ApplicationStatus.Discovered]          = [ApplicationStatus.Qualified, ApplicationStatus.ReadyToApply],
        [ApplicationStatus.Qualified]           = [ApplicationStatus.ReadyToApply],
        [ApplicationStatus.ReadyToApply]        = [ApplicationStatus.InProgress, ApplicationStatus.Withdrawn],
        [ApplicationStatus.InProgress]          = [ApplicationStatus.AwaitingUserReview, ApplicationStatus.Failed, ApplicationStatus.Withdrawn],
        [ApplicationStatus.AwaitingUserReview]  = [ApplicationStatus.InProgress, ApplicationStatus.ConfirmationPending,
                                                   ApplicationStatus.Submitted, ApplicationStatus.Failed],
        [ApplicationStatus.ConfirmationPending] = [ApplicationStatus.Submitted, ApplicationStatus.AwaitingUserReview],
        [ApplicationStatus.Failed]              = [ApplicationStatus.InProgress],
        [ApplicationStatus.Submitted]           = [ApplicationStatus.Assessment, ApplicationStatus.Interview,
                                                   ApplicationStatus.Rejected, ApplicationStatus.Withdrawn],
        [ApplicationStatus.Assessment]          = [ApplicationStatus.Interview, ApplicationStatus.Rejected],
        [ApplicationStatus.Interview]           = [ApplicationStatus.Offer, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn],
        [ApplicationStatus.Offer]               = [ApplicationStatus.Withdrawn],
    };

    public static IReadOnlyList<ApplicationStatus> GetAllowedTargets(ApplicationStatus current)
    {
        return Allowed.TryGetValue(current, out var targets) ? targets : Array.Empty<ApplicationStatus>();
    }

    public static bool CanTransition(ApplicationStatus from, ApplicationStatus to, ChangeSource source)
    {
        if (!Allowed.TryGetValue(from, out var targets) || !targets.Contains(to))
            return false;

        // Submitted reachable only with change_source ∈ {Detector, User}
        if (to == ApplicationStatus.Submitted && source is not (ChangeSource.Detector or ChangeSource.User))
            return false;

        // Post-submission statuses only reachable by User
        bool userOnly = to is ApplicationStatus.Assessment or ApplicationStatus.Interview or ApplicationStatus.Offer
                               or ApplicationStatus.Rejected or ApplicationStatus.Withdrawn;
        if (userOnly && source != ChangeSource.User)
            return false;

        return true;
    }
}
