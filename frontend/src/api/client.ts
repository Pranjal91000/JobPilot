import type { CompletenessReport, FactRef, ProfileDto, ResumeDto, UpdateProfileRequest } from './types';

export const apiClient = {
  async getHealth(): Promise<{ status: string; database: string }> {
    const res = await fetch('/health');
    if (!res.ok) throw new Error('Health check failed');
    return res.json();
  },

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
    const res = await fetch('/api/profile/confirm', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(facts),
    });
    if (!res.ok) throw new Error('Failed to confirm facts');
  },

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
  }
};

