import type {
  AiSettingsDto,
  AutomationCommandDto,
  CompletenessReport,
  FactRef,
  FieldDetailDto,
  IntakeRequest,
  IntakeResult,
  JobApplicationDto,
  JobApplicationSummaryDto,
  ProfileDto,
  ResumeDto,
  SessionDto,
  SubmissionApprovalDto,
  TestAiConnectionResponse,
  UpdateAiSettingsRequest,
  UpdateProfileRequest,
  UrlClassification,
  ValidationReport,
} from './types';

export const apiClient = {
  async getHealth(): Promise<{ status: string; database: string }> {
    const res = await fetch('/health');
    if (!res.ok) throw new Error('Health check failed');
    return res.json();
  },

  // Profile & Completeness
  async getProfile(): Promise<ProfileDto> {
    const res = await fetch('/api/profile');
    if (!res.ok) throw new Error('Failed to load profile');
    return res.json();
  },

  async updateProfile(data: UpdateProfileRequest): Promise<ProfileDto> {
    const res = await fetch('/api/profile', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data),
    });
    if (!res.ok) throw new Error('Failed to update profile');
    return res.json();
  },

  async getCompleteness(): Promise<CompletenessReport> {
    const res = await fetch('/api/profile/completeness');
    if (!res.ok) throw new Error('Failed to load completeness report');
    return res.json();
  },

  async confirmFacts(facts: FactRef[]): Promise<void> {
    const res = await fetch('/api/profile/confirm-facts', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(facts),
    });
    if (!res.ok) throw new Error('Failed to confirm facts');
  },

  // Resumes
  async listResumes(): Promise<ResumeDto[]> {
    const res = await fetch('/api/resumes');
    if (!res.ok) throw new Error('Failed to load resumes');
    return res.json();
  },

  async uploadResume(formData: FormData): Promise<ResumeDto> {
    const res = await fetch('/api/resumes', {
      method: 'POST',
      body: formData,
    });
    if (!res.ok) {
      const err = await res.text();
      throw new Error(err || 'Failed to upload resume');
    }
    return res.json();
  },

  async deleteResume(resumeId: string): Promise<void> {
    const res = await fetch(`/api/resumes/${resumeId}`, {
      method: 'DELETE',
    });
    if (!res.ok) throw new Error('Failed to delete resume');
  },

  async updateResume(resumeId: string, meta: { resumeType: string; label?: string; isDefault: boolean; isActive: boolean }): Promise<ResumeDto> {
    const res = await fetch(`/api/resumes/${resumeId}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(meta),
    });
    if (!res.ok) throw new Error('Failed to update resume');
    return res.json();
  },

  getResumeDownloadUrl(resumeId: string): string {
    return `/api/resumes/${resumeId}/content`;
  },

  // Applications
  async classifyUrl(url: string): Promise<UrlClassification> {
    const res = await fetch('/api/jobs/classify-url', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ url }),
    });
    if (!res.ok) throw new Error('Failed to classify URL');
    return res.json();
  },

  async intakeApplication(data: IntakeRequest): Promise<IntakeResult> {
    const res = await fetch('/api/applications/intake', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data),
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ detail: 'Intake failed' }));
      throw new Error(err.detail || err.title || 'Failed to intake job application');
    }
    return res.json();
  },

  async listApplications(): Promise<JobApplicationSummaryDto[]> {
    const res = await fetch('/api/applications');
    if (!res.ok) throw new Error('Failed to load applications');
    return res.json();
  },

  async getApplication(id: string): Promise<JobApplicationDto> {
    const res = await fetch(`/api/applications/${id}`);
    if (!res.ok) throw new Error('Failed to load application');
    return res.json();
  },

  async updateApplicationStatus(id: string, status: string, notes?: string): Promise<JobApplicationDto> {
    const res = await fetch(`/api/applications/${id}/status`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ status, notes }),
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ detail: 'Status update failed' }));
      throw new Error(err.detail || err.title || 'Failed to update application status');
    }
    return res.json();
  },

  async confirmSubmission(id: string, reference?: string): Promise<JobApplicationDto> {
    const res = await fetch(`/api/applications/${id}/confirm-submission`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ reference }),
    });
    if (!res.ok) throw new Error('Failed to confirm submission');
    return res.json();
  },

  // Field Mapping & AI
  async getApplicationFields(applicationId: string): Promise<FieldDetailDto[]> {
    const res = await fetch(`/api/applications/${applicationId}/fields`);
    if (!res.ok) throw new Error('Failed to load application fields');
    return res.json();
  },

  async mapApplicationFields(applicationId: string): Promise<FieldDetailDto[]> {
    const res = await fetch(`/api/applications/${applicationId}/fields/map`, {
      method: 'POST',
    });
    if (!res.ok) throw new Error('Failed to run mapping pipeline');
    return res.json();
  },

  async makeFieldDecision(
    applicationId: string,
    fieldId: string,
    action: 'Approve' | 'SetValue' | 'Reject',
    value?: string,
    optionKey?: string
  ): Promise<{ success: boolean }> {
    const res = await fetch(`/api/applications/${applicationId}/fields/${fieldId}/decision`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ action, value, optionKey }),
    });
    if (!res.ok) throw new Error('Failed to save field decision');
    return res.json();
  },

  async bulkApproveFields(applicationId: string): Promise<{ success: boolean; approvedCount: number }> {
    const res = await fetch(`/api/applications/${applicationId}/fields/bulk-approve`, {
      method: 'POST',
    });
    if (!res.ok) throw new Error('Failed to bulk approve fields');
    return res.json();
  },

  // AI Settings
  async getAiSettings(): Promise<AiSettingsDto> {
    const res = await fetch('/api/settings/ai');
    if (!res.ok) throw new Error('Failed to load AI settings');
    return res.json();
  },

  async updateAiSettings(data: UpdateAiSettingsRequest): Promise<AiSettingsDto> {
    const res = await fetch('/api/settings/ai', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data),
    });
    if (!res.ok) throw new Error('Failed to update AI settings');
    return res.json();
  },

  async testAiConnection(): Promise<TestAiConnectionResponse> {
    const res = await fetch('/api/settings/ai/test', {
      method: 'POST',
    });
    if (!res.ok) throw new Error('Connection test request failed');
    return res.json();
  },

  // Automation Sessions & Worker
  async startAutomationSession(applicationId: string): Promise<SessionDto> {
    const res = await fetch(`/api/applications/${applicationId}/session/start`, {
      method: 'POST',
    });
    if (!res.ok) throw new Error('Failed to start browser automation session');
    return res.json();
  },

  async getActiveSession(applicationId: string): Promise<SessionDto | null> {
    const res = await fetch(`/api/applications/${applicationId}/session/active`);
    if (!res.ok) return null;
    return res.json();
  },

  async getSession(sessionId: string): Promise<SessionDto> {
    const res = await fetch(`/api/sessions/${sessionId}`);
    if (!res.ok) throw new Error('Failed to get session status');
    return res.json();
  },

  async inspectSession(sessionId: string): Promise<{ commandId: string }> {
    const res = await fetch(`/api/sessions/${sessionId}/inspect`, {
      method: 'POST',
    });
    if (!res.ok) throw new Error('Failed to enqueue inspect command');
    return res.json();
  },

  async autofillSession(sessionId: string): Promise<{ commandId: string }> {
    const res = await fetch(`/api/sessions/${sessionId}/autofill`, {
      method: 'POST',
    });
    if (!res.ok) throw new Error('Failed to enqueue autofill command');
    return res.json();
  },

  async navigateSession(sessionId: string, direction: 'Next' | 'Back'): Promise<{ commandId: string }> {
    const res = await fetch(`/api/sessions/${sessionId}/navigate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ direction }),
    });
    if (!res.ok) throw new Error(`Failed to navigate ${direction}`);
    return res.json();
  },

  async validateSession(sessionId: string): Promise<ValidationReport> {
    const res = await fetch(`/api/sessions/${sessionId}/validate`, {
      method: 'POST',
    });
    if (!res.ok) throw new Error('Failed to validate session fields');
    return res.json();
  },

  async approveSubmission(sessionId: string, reportId: string, acknowledgedWarnings?: string[]): Promise<SubmissionApprovalDto> {
    const res = await fetch(`/api/sessions/${sessionId}/approve-submission`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ reportId, acknowledgedWarnings }),
    });
    if (!res.ok) throw new Error('Failed to approve submission');
    return res.json();
  },

  async getSessionCommands(sessionId: string): Promise<AutomationCommandDto[]> {
    const res = await fetch(`/api/sessions/${sessionId}/commands`);
    if (!res.ok) throw new Error('Failed to load session commands');
    return res.json();
  },
};
