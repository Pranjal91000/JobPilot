export type VerificationStatus = 'Verified' | 'ExtractedUnconfirmed' | 'AiSuggested';

export interface ProfileDto {
  userId: string;
  firstName: string;
  middleName?: string | null;
  lastName: string;
  preferredName?: string | null;
  email: string;
  phoneE164?: string | null;
  phoneCountryCode?: string | null;
  addressLine1?: string | null;
  addressLine2?: string | null;
  city?: string | null;
  stateRegion?: string | null;
  postalCode?: string | null;
  countryCode?: string | null;
  professionalSummary?: string | null;
  currentLocation?: string | null;
  noticePeriodDays?: number | null;
  expectedSalary?: string | null;
  expectedSalaryCurrency?: string | null;
  totalExperienceMonths?: number | null;
  educations: EducationDto[];
  employmentHistories: EmploymentDto[];
  skills: SkillDto[];
  projects: ProjectDto[];
  certifications: CertificationDto[];
  achievements: AchievementDto[];
  links: ProfessionalLinkDto[];
  workAuthorizations: WorkAuthorizationDto[];
  locationPreferences: LocationPreferenceDto[];
  fieldVerifications: Record<string, VerificationStatus>;
}

export interface UpdateProfileRequest {
  firstName: string;
  middleName?: string | null;
  lastName: string;
  preferredName?: string | null;
  email: string;
  phoneE164?: string | null;
  phoneCountryCode?: string | null;
  addressLine1?: string | null;
  addressLine2?: string | null;
  city?: string | null;
  stateRegion?: string | null;
  postalCode?: string | null;
  countryCode?: string | null;
  professionalSummary?: string | null;
  currentLocation?: string | null;
  noticePeriodDays?: number | null;
  expectedSalary?: string | null;
  expectedSalaryCurrency?: string | null;
}

export interface EducationDto {
  educationId: string;
  institution: string;
  degree: string;
  specialization?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  grade?: string | null;
  description?: string | null;
  countryCode?: string | null;
  sortOrder: number;
  verificationStatus: VerificationStatus;
}

export interface EmploymentDto {
  employmentId: string;
  companyName: string;
  jobTitle: string;
  employmentType?: string | null;
  location?: string | null;
  startDate: string;
  endDate?: string | null;
  isCurrent: boolean;
  responsibilities?: string | null;
  technologies: string[];
  sortOrder: number;
  verificationStatus: VerificationStatus;
}

export interface SkillDto {
  skillId: string;
  skillName: string;
  normalizedName: string;
  proficiencyLevel?: string | null;
  yearsOfExperience?: number | null;
  verificationStatus: VerificationStatus;
}

export interface ProjectDto {
  projectId: string;
  name: string;
  role?: string | null;
  description?: string | null;
  technologies: string[];
  url?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  verificationStatus: VerificationStatus;
}

export interface CertificationDto {
  certificationId: string;
  name: string;
  issuer: string;
  credentialId?: string | null;
  issuedOn?: string | null;
  expiresOn?: string | null;
  credentialUrl?: string | null;
  verificationStatus: VerificationStatus;
}

export interface AchievementDto {
  achievementId: string;
  title: string;
  description?: string | null;
  achievedOn?: string | null;
  verificationStatus: VerificationStatus;
}

export interface ProfessionalLinkDto {
  linkId: string;
  linkType: 'LinkedIn' | 'GitHub' | 'Portfolio' | 'Other';
  url: string;
  verificationStatus: VerificationStatus;
}

export interface WorkAuthorizationDto {
  authorizationId: string;
  countryCode: string;
  statusDescription: string;
  requiresSponsorship?: boolean | null;
  validUntil?: string | null;
  verificationStatus: VerificationStatus;
}

export interface LocationPreferenceDto {
  preferenceId: string;
  locationText: string;
  countryCode?: string | null;
  isRelocationOk: boolean;
  remotePreference: 'Onsite' | 'Hybrid' | 'Remote' | 'Any';
  priority: number;
  verificationStatus: VerificationStatus;
}

export interface CompletenessIssue {
  fieldKey: string;
  description: string;
  category: string;
  isRequired: boolean;
}

export interface CompletenessReport {
  totalChecked: number;
  verifiedCount: number;
  missingCount: number;
  unconfirmedCount: number;
  completenessPercentage: number;
  missingOrUnconfirmed: CompletenessIssue[];
}

export interface ResumeDto {
  resumeId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  sha256: string;
  version: number;
  resumeType: string;
  label?: string | null;
  isActive: boolean;
  isDefault: boolean;
  createdAt: string;
}

export interface FactRef {
  entityType: string;
  entityId?: string | null;
  profilePath?: string | null;
}

// Applications & Intake
export type ApplicationStatus =
  | 'ReadyToApply'
  | 'InProgress'
  | 'AwaitingUserReview'
  | 'Submitted'
  | 'UnderReview'
  | 'Interview'
  | 'Offer'
  | 'Rejected'
  | 'Archived';

export type ReviewStatus = 'NotStarted' | 'InProgress' | 'Approved' | 'RequiresChanges';

export interface UrlClassification {
  isSupported: boolean;
  ats: string;
  tenantKey?: string | null;
  host?: string | null;
  siteKey?: string | null;
  externalJobId?: string | null;
  locale?: string | null;
  normalizedUrl: string;
}

export interface JobApplicationSummaryDto {
  applicationId: string;
  jobPostId: string;
  jobTitle: string;
  companyName: string;
  status: ApplicationStatus;
  reviewStatus: ReviewStatus;
  completionPercentage: number;
  lastActivityAt: string;
  appliedAt?: string | null;
}

export interface JobApplicationDto {
  applicationId: string;
  jobPostId: string;
  jobTitle: string;
  companyName: string;
  applicationUrl: string;
  status: ApplicationStatus;
  reviewStatus: ReviewStatus;
  completionPercentage: number;
  submissionReference?: string | null;
  submissionConfirmedAt?: string | null;
  lastActivityAt: string;
  history: Array<{
    historyId: string;
    previousStatus?: ApplicationStatus | null;
    newStatus: ApplicationStatus;
    changeSource: string;
    changedAt: string;
    notes?: string | null;
  }>;
}

export interface IntakeRequest {
  url: string;
  resumeId?: string | null;
}

export interface IntakeResult {
  classification: UrlClassification;
  jobPost: {
    jobPostId: string;
    created: boolean;
    jobTitle: string;
    companyName: string;
    location?: string | null;
    externalJobId?: string | null;
    normalizedUrl: string;
  };
  application: JobApplicationDto;
  duplicateWarnings: string[];
}

// Field Mapping & AI
export type FieldType = 'Text' | 'Textarea' | 'SingleSelect' | 'MultiSelect' | 'Radio' | 'Checkbox' | 'Date' | 'FileUpload' | 'Unknown';
export type MappingStatus = 'Proposed' | 'AutoApproved' | 'NeedsReview' | 'Blocked' | 'Approved' | 'Rejected' | 'ManualRequired';
export type MappingMethod = 'AdapterRule' | 'Dictionary' | 'SavedAnswer' | 'Llm' | 'User';

export interface FieldMappingDto {
  mappingId: string;
  profilePath?: string | null;
  suggestedValue?: string | null;
  suggestedOptionKey?: string | null;
  confidenceScore: number;
  status: MappingStatus;
  method: MappingMethod;
  source: string;
  requiresIntervention: boolean;
  isSensitive: boolean;
  reason?: string | null;
}

export interface AnswerDto {
  answerId: string;
  answerValue?: string | null;
  isUserApproved: boolean;
}

export interface FieldDetailDto {
  fieldId: string;
  fieldIdentifier: string;
  label: string;
  type: FieldType;
  required: boolean;
  section?: string | null;
  pageValue?: string | null;
  availableOptions?: any;
  mapping?: FieldMappingDto | null;
  answer?: AnswerDto | null;
}

// AI Settings
export interface AiSettingsDto {
  providerName: string;
  defaultModel: string;
  isEnabled: boolean;
  hasApiKey: boolean;
  lastTestedAt?: string | null;
  lastTestStatus?: string | null;
}

export interface UpdateAiSettingsRequest {
  providerName: string;
  defaultModel: string;
  isEnabled: boolean;
  apiKey?: string | null;
}

export interface TestAiConnectionResponse {
  success: boolean;
  message: string;
  latencyMs?: number | null;
}

export type SessionStatus =
  | 'Pending'
  | 'Launching'
  | 'AwaitingAuthentication'
  | 'AwaitingUser'
  | 'Inspecting'
  | 'Mapping'
  | 'Filling'
  | 'Navigating'
  | 'WatchingForConfirmation'
  | 'Paused'
  | 'Completed'
  | 'Cancelled'
  | 'Interrupted'
  | 'Failed';

export interface SessionDto {
  sessionId: string;
  applicationId: string;
  sessionStatus: SessionStatus;
  authState: string;
  currentStepKey?: string | null;
  completionPercentage: number;
  lastActivityAt: string;
  workerOnline: boolean;
}

export interface AutomationCommandDto {
  commandId: string;
  sessionId: string;
  commandType: string;
  status: string;
  outcome?: string | null;
  error?: string | null;
  enqueuedAt: string;
  startedAt?: string | null;
  finishedAt?: string | null;
}

export interface ValidationIssue {
  code: string;
  message: string;
  fieldId?: string | null;
  stepKey?: string | null;
}

export interface ValidationReport {
  reportId: string;
  createdAt: string;
  isSubmittable: boolean;
  blocking: ValidationIssue[];
  warnings: ValidationIssue[];
  fieldValuesHash: string;
}

export interface SubmissionApprovalDto {
  approvalId: string;
  submissionControl: string;
  instructions: string;
  watchingForConfirmationUntil: string;
}
