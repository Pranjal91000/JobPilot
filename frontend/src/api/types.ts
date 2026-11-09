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

