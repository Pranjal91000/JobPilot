import React, { useState } from 'react';
import type { ProfileDto, UpdateProfileRequest } from '../../api/types';
import { apiClient } from '../../api/client';
import { VerificationBadge } from '../../components/VerificationBadge';
import { Save, User, Briefcase, GraduationCap, Award, Check } from 'lucide-react';

interface Props {
  profile: ProfileDto;
  onProfileUpdated: (updated: ProfileDto) => void;
}

export const ProfileView: React.FC<Props> = ({ profile, onProfileUpdated }) => {
  const [activeTab, setActiveTab] = useState<'personal' | 'experience' | 'education' | 'skills'>('personal');
  const [formData, setFormData] = useState<UpdateProfileRequest>({
    firstName: profile.firstName ?? '',
    middleName: profile.middleName ?? '',
    lastName: profile.lastName ?? '',
    preferredName: profile.preferredName ?? '',
    email: profile.email ?? '',
    phoneE164: profile.phoneE164 ?? '',
    phoneCountryCode: profile.phoneCountryCode ?? '+91',
    addressLine1: profile.addressLine1 ?? '',
    addressLine2: profile.addressLine2 ?? '',
    city: profile.city ?? '',
    stateRegion: profile.stateRegion ?? '',
    postalCode: profile.postalCode ?? '',
    countryCode: profile.countryCode ?? 'IN',
    professionalSummary: profile.professionalSummary ?? '',
    currentLocation: profile.currentLocation ?? '',
    noticePeriodDays: profile.noticePeriodDays ?? 30,
    expectedSalary: profile.expectedSalary ?? '',
    expectedSalaryCurrency: profile.expectedSalaryCurrency ?? 'INR',
  });

  const [saving, setSaving] = useState(false);
  const [savedSuccess, setSavedSuccess] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setError(null);
    setSavedSuccess(false);

    try {
      const updated = await apiClient.updateProfile(formData);
      onProfileUpdated(updated);
      setSavedSuccess(true);
      setTimeout(() => setSavedSuccess(false), 3000);
    } catch (err: any) {
      setError(err.message || 'Failed to save changes');
    } finally {
      setSaving(false);
    }
  };

  const handleConfirmFact = async (entityType: string, entityId: string) => {
    try {
      await apiClient.confirmFacts([{ entityType, entityId }]);
      const refreshed = await apiClient.getProfile();
      onProfileUpdated(refreshed);
    } catch (err: any) {
      alert('Failed to confirm fact: ' + err.message);
    }
  };

  return (
    <div>
      <div className="tabs">
        <button
          className={`tab-btn ${activeTab === 'personal' ? 'active' : ''}`}
          onClick={() => setActiveTab('personal')}
        >
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem' }}>
            <User size={16} /> Personal & Contact
          </span>
        </button>
        <button
          className={`tab-btn ${activeTab === 'experience' ? 'active' : ''}`}
          onClick={() => setActiveTab('experience')}
        >
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem' }}>
            <Briefcase size={16} /> Work Experience ({profile.employmentHistories.length})
          </span>
        </button>
        <button
          className={`tab-btn ${activeTab === 'education' ? 'active' : ''}`}
          onClick={() => setActiveTab('education')}
        >
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem' }}>
            <GraduationCap size={16} /> Education ({profile.educations.length})
          </span>
        </button>
        <button
          className={`tab-btn ${activeTab === 'skills' ? 'active' : ''}`}
          onClick={() => setActiveTab('skills')}
        >
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem' }}>
            <Award size={16} /> Skills ({profile.skills.length})
          </span>
        </button>
      </div>

      {activeTab === 'personal' && (
        <form onSubmit={handleSubmit} className="card">
          <div className="card-title">
            <span>Core Personal & Contact Information</span>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              {savedSuccess && (
                <span style={{ color: '#16a34a', fontSize: '0.85rem', display: 'flex', alignItems: 'center', gap: '0.25rem' }}>
                  <Check size={16} /> Saved & Verified
                </span>
              )}
              <button type="submit" className="btn btn-primary btn-sm" disabled={saving}>
                <Save size={16} />
                {saving ? 'Saving...' : 'Save & Verify Facts'}
              </button>
            </div>
          </div>

          {error && (
            <div style={{ backgroundColor: '#fee2e2', color: '#dc2626', padding: '0.5rem', borderRadius: '4px', marginBottom: '1rem', fontSize: '0.85rem' }}>
              {error}
            </div>
          )}

          <div className="grid-2">
            <div className="form-group">
              <label className="form-label" style={{ display: 'flex', justifyContent: 'space-between' }}>
                First Name *
                <VerificationBadge status={profile.fieldVerifications['Personal.FirstName']} />
              </label>
              <input
                type="text"
                className="form-control"
                value={formData.firstName}
                onChange={e => setFormData({ ...formData, firstName: e.target.value })}
                required
              />
            </div>

            <div className="form-group">
              <label className="form-label" style={{ display: 'flex', justifyContent: 'space-between' }}>
                Last Name *
                <VerificationBadge status={profile.fieldVerifications['Personal.LastName']} />
              </label>
              <input
                type="text"
                className="form-control"
                value={formData.lastName}
                onChange={e => setFormData({ ...formData, lastName: e.target.value })}
                required
              />
            </div>
          </div>

          <div className="grid-2">
            <div className="form-group">
              <label className="form-label" style={{ display: 'flex', justifyContent: 'space-between' }}>
                Email Address *
                <VerificationBadge status={profile.fieldVerifications['Contact.Email']} />
              </label>
              <input
                type="email"
                className="form-control"
                value={formData.email}
                onChange={e => setFormData({ ...formData, email: e.target.value })}
                required
              />
            </div>

            <div className="form-group">
              <label className="form-label" style={{ display: 'flex', justifyContent: 'space-between' }}>
                Phone Number
                <VerificationBadge status={profile.fieldVerifications['Contact.PhoneNational']} />
              </label>
              <input
                type="text"
                className="form-control"
                placeholder="+919876543210"
                value={formData.phoneE164 ?? ''}
                onChange={e => setFormData({ ...formData, phoneE164: e.target.value })}
              />
            </div>
          </div>

          <div className="grid-2">
            <div className="form-group">
              <label className="form-label">Address Line 1</label>
              <input
                type="text"
                className="form-control"
                value={formData.addressLine1 ?? ''}
                onChange={e => setFormData({ ...formData, addressLine1: e.target.value })}
              />
            </div>

            <div className="form-group">
              <label className="form-label">City</label>
              <input
                type="text"
                className="form-control"
                value={formData.city ?? ''}
                onChange={e => setFormData({ ...formData, city: e.target.value })}
              />
            </div>
          </div>

          <div className="grid-2">
            <div className="form-group">
              <label className="form-label">Notice Period (Days)</label>
              <input
                type="number"
                className="form-control"
                value={formData.noticePeriodDays ?? 30}
                onChange={e => setFormData({ ...formData, noticePeriodDays: parseInt(e.target.value) || 0 })}
              />
            </div>

            <div className="form-group">
              <label className="form-label" style={{ display: 'flex', justifyContent: 'space-between' }}>
                Expected Salary (Annual)
                <span style={{ fontSize: '0.75rem', color: '#64748b' }}>Encrypted at rest</span>
              </label>
              <div style={{ display: 'flex', gap: '0.5rem' }}>
                <input
                  type="text"
                  className="form-control"
                  style={{ width: '80px' }}
                  value={formData.expectedSalaryCurrency ?? 'INR'}
                  onChange={e => setFormData({ ...formData, expectedSalaryCurrency: e.target.value })}
                />
                <input
                  type="text"
                  className="form-control"
                  placeholder="e.g. 2500000"
                  value={formData.expectedSalary ?? ''}
                  onChange={e => setFormData({ ...formData, expectedSalary: e.target.value })}
                />
              </div>
            </div>
          </div>

          <div className="form-group">
            <label className="form-label">Professional Summary</label>
            <textarea
              className="form-control"
              rows={4}
              value={formData.professionalSummary ?? ''}
              onChange={e => setFormData({ ...formData, professionalSummary: e.target.value })}
              placeholder="Brief summary of professional experience, accomplishments, and focus areas..."
            />
          </div>
        </form>
      )}

      {activeTab === 'experience' && (
        <div className="card">
          <div className="card-title">
            <span>Employment History</span>
            <span style={{ fontSize: '0.85rem', color: '#64748b' }}>
              Used in repeatable Work Experience sections
            </span>
          </div>

          {profile.employmentHistories.length === 0 ? (
            <div style={{ textAlign: 'center', padding: '2rem', color: '#64748b' }}>
              No employment history records present yet. Upload a resume or add employment items.
            </div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
              {profile.employmentHistories.map(emp => (
                <div key={emp.employmentId} style={{ padding: '1rem', border: '1px solid #e2e8f0', borderRadius: '6px' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <div>
                      <h4 style={{ fontWeight: 600 }}>{emp.jobTitle} at {emp.companyName}</h4>
                      <p style={{ fontSize: '0.85rem', color: '#64748b' }}>
                        {emp.startDate} to {emp.isCurrent ? 'Present' : (emp.endDate ?? 'N/A')} {emp.location && `• ${emp.location}`}
                      </p>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                      <VerificationBadge status={emp.verificationStatus} />
                      {emp.verificationStatus !== 'Verified' && (
                        <button
                          className="btn btn-outline btn-sm"
                          onClick={() => handleConfirmFact('employment', emp.employmentId)}
                        >
                          Confirm Fact
                        </button>
                      )}
                    </div>
                  </div>
                  {emp.responsibilities && (
                    <p style={{ marginTop: '0.5rem', fontSize: '0.9rem', color: '#334155' }}>
                      {emp.responsibilities}
                    </p>
                  )}
                  {emp.technologies && emp.technologies.length > 0 && (
                    <div style={{ marginTop: '0.5rem', display: 'flex', flexWrap: 'wrap', gap: '0.35rem' }}>
                      {emp.technologies.map(t => (
                        <span key={t} style={{ fontSize: '0.75rem', backgroundColor: '#f1f5f9', padding: '0.15rem 0.4rem', borderRadius: '4px' }}>
                          {t}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {activeTab === 'education' && (
        <div className="card">
          <div className="card-title">
            <span>Education</span>
            <span style={{ fontSize: '0.85rem', color: '#64748b' }}>
              Used in repeatable Education sections
            </span>
          </div>

          {profile.educations.length === 0 ? (
            <div style={{ textAlign: 'center', padding: '2rem', color: '#64748b' }}>
              No education records present yet.
            </div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
              {profile.educations.map(edu => (
                <div key={edu.educationId} style={{ padding: '1rem', border: '1px solid #e2e8f0', borderRadius: '6px' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <div>
                      <h4 style={{ fontWeight: 600 }}>{edu.degree} {edu.specialization && `in ${edu.specialization}`}</h4>
                      <p style={{ fontSize: '0.85rem', color: '#64748b' }}>
                        {edu.institution} {edu.endDate && `(${edu.endDate})`} {edu.grade && `• Grade: ${edu.grade}`}
                      </p>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                      <VerificationBadge status={edu.verificationStatus} />
                      {edu.verificationStatus !== 'Verified' && (
                        <button
                          className="btn btn-outline btn-sm"
                          onClick={() => handleConfirmFact('education', edu.educationId)}
                        >
                          Confirm Fact
                        </button>
                      )}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {activeTab === 'skills' && (
        <div className="card">
          <div className="card-title">
            <span>Skills & Expertise</span>
          </div>

          {profile.skills.length === 0 ? (
            <div style={{ textAlign: 'center', padding: '2rem', color: '#64748b' }}>
              No skills added yet.
            </div>
          ) : (
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: '0.75rem' }}>
              {profile.skills.map(skill => (
                <div key={skill.skillId} style={{ padding: '0.75rem', border: '1px solid #e2e8f0', borderRadius: '6px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <div>
                    <div style={{ fontWeight: 600, fontSize: '0.9rem' }}>{skill.skillName}</div>
                    {skill.yearsOfExperience && (
                      <div style={{ fontSize: '0.75rem', color: '#64748b' }}>{skill.yearsOfExperience} yrs exp</div>
                    )}
                  </div>
                  <VerificationBadge status={skill.verificationStatus} />
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
};

