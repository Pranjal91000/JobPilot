namespace JobPilot.Domain.Enums;

public enum VerificationStatus
{
    Verified,
    ExtractedUnconfirmed,
    AiSuggested
}

public enum FactSource
{
    UserEntered,
    ResumeExtraction,
    AiSuggestion,
    Import
}

public enum LinkType
{
    LinkedIn,
    GitHub,
    Portfolio,
    Other
}

public enum RemotePreference
{
    Onsite,
    Hybrid,
    Remote,
    Any
}

public enum SavedAnswerScope
{
    Global,
    Company,
    Tenant
}

public enum ApplicationStatus
{
    Discovered,
    Qualified,
    ReadyToApply,
    InProgress,
    AwaitingUserReview,
    ConfirmationPending,
    Submitted,
    Failed,
    Rejected,
    Interview,
    Assessment,
    Offer,
    Withdrawn
}

public enum ReviewStatus
{
    NotStarted,
    InReview,
    Approved
}

public enum ChangeSource
{
    User,
    Automation,
    Detector,
    System
}

public enum SubmissionConfirmationSource
{
    Detector,
    User
}

public enum SessionStatus
{
    Pending,
    Launching,
    AwaitingAuthentication,
    AwaitingUser,
    Inspecting,
    Mapping,
    Filling,
    Navigating,
    WatchingForConfirmation,
    Paused,
    Completed,
    Cancelled,
    Interrupted,
    Failed
}

public enum AuthState
{
    SignedOut,
    SignInRequired,
    CreateAccount,
    VerificationPending,
    InApplyFlow,
    CandidateHome,
    Unknown
}

public enum FieldType
{
    Text,
    SingleSelect,
    MultiSelect,
    Radio,
    Checkbox,
    Date,
    FileUpload,
    Unknown
}

public enum MappingStatus
{
    Proposed,
    AutoApproved,
    NeedsReview,
    Blocked,
    Approved,
    Rejected,
    ManualRequired
}

public enum MappingMethod
{
    AdapterRule,
    Dictionary,
    SavedAnswer,
    Llm,
    User
}

public enum MappingSource
{
    VerifiedProfile,
    SavedAnswer,
    UserEdit,
    None
}

public enum AnswerSource
{
    Profile,
    SavedAnswer,
    UserEntered,
    AiDraft
}

public enum SensitiveCategory
{
    None,
    Demographic,
    Disability,
    Veteran,
    CriminalHistory,
    LegalAuthorization,
    Sponsorship,
    Age,
    Health,
    Attestation,
    Consent,
    Compensation
}

public enum AutomationCommandType
{
    StartSession,
    Inspect,
    MapFields,
    Autofill,
    Navigate,
    Validate,
    WatchForConfirmation,
    Pause,
    Resume,
    Cancel,
    CloseBrowser
}

public enum AutomationCommandStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Expired,
    Interrupted
}

public enum AutomationEventStatus
{
    Info,
    Warning,
    Error
}

public enum ResumeType
{
    General,
    Backend,
    Frontend,
    FullStack,
    DevOps,
    Management,
    Custom
}

public enum SourceDocumentKind
{
    Resume,
    LinkedInExport,
    Other
}

