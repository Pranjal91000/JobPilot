import React, { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '../../api/client';
import type { ApplicationStatus, JobApplicationSummaryDto, UrlClassification } from '../../api/types';
import {
  Briefcase,
  Plus,
  Clock,
  CheckCircle,
  AlertCircle,
  Check,
  ChevronRight,
  Filter,
  Sparkles,
  ShieldCheck,
} from 'lucide-react';

interface Props {
  onSelectApplication: (applicationId: string) => void;
  onOpenAiSettings: () => void;
}

export const ApplicationsView: React.FC<Props> = ({ onSelectApplication, onOpenAiSettings }) => {
  const queryClient = useQueryClient();

  const [urlInput, setUrlInput] = useState('');
  const [classification, setClassification] = useState<UrlClassification | null>(null);
  const [isClassifying, setIsClassifying] = useState(false);
  const [duplicateWarning, setDuplicateWarning] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<string>('All');

  // Status transition modal state
  const [transitionApp, setTransitionApp] = useState<JobApplicationSummaryDto | null>(null);
  const [selectedTargetStatus, setSelectedTargetStatus] = useState<ApplicationStatus>('InProgress');
  const [transitionNotes, setTransitionNotes] = useState('');

  // Submission confirmation modal state
  const [confirmApp, setConfirmApp] = useState<JobApplicationSummaryDto | null>(null);
  const [submissionRef, setSubmissionRef] = useState('');

  const { data: applications = [] } = useQuery<JobApplicationSummaryDto[]>({
    queryKey: ['applications'],
    queryFn: apiClient.listApplications,
  });

  // Debounced URL classification
  useEffect(() => {
    if (!urlInput.trim() || !urlInput.startsWith('http')) {
      setClassification(null);
      setDuplicateWarning(null);
      return;
    }

    const timer = setTimeout(async () => {
      setIsClassifying(true);
      try {
        const result = await apiClient.classifyUrl(urlInput.trim());
        setClassification(result);
      } catch (err) {
        setClassification(null);
      } finally {
        setIsClassifying(false);
      }
    }, 400);

    return () => clearTimeout(timer);
  }, [urlInput]);

  const intakeMutation = useMutation({
    mutationFn: apiClient.intakeApplication,
    onSuccess: (res) => {
      queryClient.invalidateQueries({ queryKey: ['applications'] });
      setUrlInput('');
      setClassification(null);
      if (res.duplicateWarnings && res.duplicateWarnings.length > 0) {
        setDuplicateWarning(res.duplicateWarnings[0]);
      } else {
        setDuplicateWarning(null);
        onSelectApplication(res.application.applicationId);
      }
    },
    onError: (err: any) => {
      alert(err.message || 'Intake failed');
    },
  });

  const statusMutation = useMutation({
    mutationFn: ({ id, status, notes }: { id: string; status: string; notes?: string }) =>
      apiClient.updateApplicationStatus(id, status, notes),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['applications'] });
      setTransitionApp(null);
      setTransitionNotes('');
    },
    onError: (err: any) => {
      alert(err.message || 'Failed to update status');
    },
  });

  const confirmSubmissionMutation = useMutation({
    mutationFn: ({ id, reference }: { id: string; reference?: string }) =>
      apiClient.confirmSubmission(id, reference),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['applications'] });
      setConfirmApp(null);
      setSubmissionRef('');
    },
    onError: (err: any) => {
      alert(err.message || 'Failed to confirm submission');
    },
  });

  const handleIntakeSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!urlInput.trim()) return;
    intakeMutation.mutate({ url: urlInput.trim(), resumeId: null });
  };

  const filteredApps = applications.filter((app) => {
    if (statusFilter === 'All') return true;
    return app.status === statusFilter;
  });

  const getStatusColor = (status: ApplicationStatus) => {
    switch (status) {
      case 'ReadyToApply':
        return { bg: '#eff6ff', text: '#1d4ed8', border: '#bfdbfe' };
      case 'InProgress':
        return { bg: '#f5f3ff', text: '#6d28d9', border: '#ddd6fe' };
      case 'AwaitingUserReview':
        return { bg: '#fefce8', text: '#a16207', border: '#fef08a' };
      case 'Submitted':
        return { bg: '#f0fdf4', text: '#15803d', border: '#bbf7d0' };
      case 'UnderReview':
        return { bg: '#ecfeff', text: '#0e7490', border: '#a5f3fc' };
      case 'Interview':
        return { bg: '#faf5ff', text: '#7e22ce', border: '#e9d5ff' };
      case 'Offer':
        return { bg: '#f0fdf4', text: '#166534', border: '#86efac' };
      case 'Rejected':
        return { bg: '#fef2f2', text: '#b91c1c', border: '#fecaca' };
      default:
        return { bg: '#f8fafc', text: '#475569', border: '#e2e8f0' };
    }
  };

  return (
    <div style={{ maxWidth: '1200px', margin: '0 auto', padding: '24px' }}>
      {/* Quick Intake Bar */}
      <div style={{
        background: '#ffffff',
        border: '1px solid #e2e8f0',
        borderRadius: '16px',
        padding: '24px',
        boxShadow: '0 4px 6px -1px rgba(0,0,0,0.05)',
        marginBottom: '28px',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '16px' }}>
          <div>
            <h2 style={{ margin: 0, fontSize: '18px', fontWeight: 600, color: '#0f172a' }}>
              Job Intake & Workday URL Normalizer
            </h2>
            <p style={{ margin: '4px 0 0 0', fontSize: '13px', color: '#64748b' }}>
              Paste any Workday job URL. Canonical requisition ID, tenant key, and locale are detected automatically.
            </p>
          </div>
          <button
            onClick={onOpenAiSettings}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '6px',
              padding: '6px 12px',
              borderRadius: '8px',
              border: '1px solid #cbd5e1',
              background: '#f8fafc',
              fontSize: '12px',
              fontWeight: 500,
              color: '#334155',
              cursor: 'pointer',
            }}
          >
            <Sparkles size={14} style={{ color: '#2563eb' }} />
            Configure AI Provider
          </button>
        </div>

        <form onSubmit={handleIntakeSubmit} style={{ display: 'flex', gap: '12px' }}>
          <div style={{ flex: 1, position: 'relative' }}>
            <input
              type="url"
              value={urlInput}
              onChange={(e) => setUrlInput(e.target.value)}
              placeholder="https://company.wd5.myworkdayjobs.com/en-US/careers/job/Location/Title_R-12345"
              style={{
                width: '100%',
                padding: '12px 16px',
                borderRadius: '10px',
                border: '1px solid #cbd5e1',
                fontSize: '14px',
                outline: 'none',
                boxSizing: 'border-box',
              }}
            />
          </div>
          <button
            type="submit"
            disabled={intakeMutation.isPending || !urlInput.trim()}
            style={{
              padding: '12px 24px',
              background: '#2563eb',
              color: '#ffffff',
              border: 'none',
              borderRadius: '10px',
              fontWeight: 600,
              fontSize: '14px',
              cursor: 'pointer',
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
              whiteSpace: 'nowrap',
            }}
          >
            <Plus size={16} />
            {intakeMutation.isPending ? 'Processing...' : 'Intake Application'}
          </button>
        </form>

        {isClassifying && (
          <div style={{ marginTop: '10px', fontSize: '12px', color: '#64748b' }}>
            Analyzing Workday URL & extracting canonical requisition...
          </div>
        )}

        {/* Real-time Classification Preview */}
        {classification && (
          <div style={{
            marginTop: '14px',
            padding: '12px 16px',
            background: classification.isSupported ? '#f0fdf4' : '#fffbeb',
            border: `1px solid ${classification.isSupported ? '#bbf7d0' : '#fef3c7'}`,
            borderRadius: '10px',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            fontSize: '13px',
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
              {classification.isSupported ? (
                <CheckCircle size={18} style={{ color: '#16a34a' }} />
              ) : (
                <AlertCircle size={18} style={{ color: '#d97706' }} />
              )}
              <span>
                <strong>{classification.isSupported ? 'Supported Workday Portal:' : 'Unsupported Portal:'}</strong>{' '}
                Tenant: <code>{classification.tenantKey || 'Generic'}</code> | Req ID:{' '}
                <code>{classification.externalJobId || 'Detected'}</code>
              </span>
            </div>
            <span style={{
              fontSize: '12px',
              padding: '2px 8px',
              background: '#ffffff',
              borderRadius: '6px',
              border: '1px solid #e2e8f0',
              color: '#475569',
            }}>
              Canonical URL ready
            </span>
          </div>
        )}

        {/* Duplicate Warning Banner */}
        {duplicateWarning && (
          <div style={{
            marginTop: '14px',
            padding: '12px 16px',
            background: '#fef2f2',
            border: '1px solid #fecaca',
            borderRadius: '10px',
            color: '#991b1b',
            display: 'flex',
            alignItems: 'center',
            gap: '10px',
            fontSize: '13px',
          }}>
            <AlertCircle size={18} />
            <span>{duplicateWarning}</span>
          </div>
        )}
      </div>

      {/* Filter Tabs */}
      <div style={{ display: 'flex', gap: '8px', marginBottom: '20px', alignItems: 'center' }}>
        <Filter size={16} style={{ color: '#64748b', marginRight: '4px' }} />
        {['All', 'ReadyToApply', 'InProgress', 'AwaitingUserReview', 'Submitted'].map((filter) => (
          <button
            key={filter}
            onClick={() => setStatusFilter(filter)}
            style={{
              padding: '6px 14px',
              borderRadius: '20px',
              border: statusFilter === filter ? '1px solid #2563eb' : '1px solid #e2e8f0',
              background: statusFilter === filter ? '#eff6ff' : '#ffffff',
              color: statusFilter === filter ? '#2563eb' : '#475569',
              fontSize: '13px',
              fontWeight: 500,
              cursor: 'pointer',
            }}
          >
            {filter === 'All' ? 'All Applications' : filter}
          </button>
        ))}
      </div>

      {/* Applications Grid */}
      {filteredApps.length === 0 ? (
        <div style={{
          textAlign: 'center',
          padding: '48px 24px',
          background: '#ffffff',
          borderRadius: '16px',
          border: '1px solid #e2e8f0',
        }}>
          <Briefcase size={40} style={{ color: '#94a3b8', marginBottom: '12px' }} />
          <h3 style={{ margin: '0 0 6px 0', fontSize: '16px', color: '#334155' }}>No applications in this view</h3>
          <p style={{ margin: 0, fontSize: '13px', color: '#64748b' }}>
            Paste a Workday job URL in the box above to start an application.
          </p>
        </div>
      ) : (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(360px, 1fr))', gap: '20px' }}>
          {filteredApps.map((app) => {
            const statusStyle = getStatusColor(app.status);
            return (
              <div
                key={app.applicationId}
                style={{
                  background: '#ffffff',
                  border: '1px solid #e2e8f0',
                  borderRadius: '14px',
                  padding: '20px',
                  boxShadow: '0 2px 4px rgba(0,0,0,0.03)',
                  display: 'flex',
                  flexDirection: 'column',
                  justifyContent: 'space-between',
                }}
              >
                <div>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '10px' }}>
                    <span style={{
                      padding: '4px 10px',
                      borderRadius: '12px',
                      fontSize: '12px',
                      fontWeight: 600,
                      background: statusStyle.bg,
                      color: statusStyle.text,
                      border: `1px solid ${statusStyle.border}`,
                    }}>
                      {app.status}
                    </span>
                    <span style={{ fontSize: '12px', color: '#94a3b8', display: 'flex', alignItems: 'center', gap: '4px' }}>
                      <Clock size={12} />
                      {new Date(app.lastActivityAt).toLocaleDateString()}
                    </span>
                  </div>

                  <h3 style={{ margin: '0 0 4px 0', fontSize: '16px', fontWeight: 600, color: '#0f172a' }}>
                    {app.jobTitle}
                  </h3>
                  <div style={{ fontSize: '14px', color: '#475569', marginBottom: '16px' }}>
                    {app.companyName}
                  </div>

                  {/* Completion Bar */}
                  <div style={{ marginBottom: '18px' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '12px', color: '#64748b', marginBottom: '4px' }}>
                      <span>Autofill Completeness</span>
                      <span>{Math.round(app.completionPercentage)}%</span>
                    </div>
                    <div style={{ height: '6px', background: '#e2e8f0', borderRadius: '3px', overflow: 'hidden' }}>
                      <div
                        style={{
                          height: '100%',
                          width: `${Math.max(5, app.completionPercentage)}%`,
                          background: app.completionPercentage > 80 ? '#16a34a' : '#2563eb',
                          borderRadius: '3px',
                        }}
                      />
                    </div>
                  </div>
                </div>

                {/* Card Actions */}
                <div style={{ borderTop: '1px solid #f1f5f9', paddingTop: '14px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <div style={{ display: 'flex', gap: '8px' }}>
                    <button
                      onClick={() => setTransitionApp(app)}
                      style={{
                        padding: '6px 10px',
                        fontSize: '12px',
                        borderRadius: '6px',
                        border: '1px solid #cbd5e1',
                        background: '#ffffff',
                        color: '#475569',
                        cursor: 'pointer',
                      }}
                    >
                      Status
                    </button>
                    {app.status !== 'Submitted' && (
                      <button
                        onClick={() => setConfirmApp(app)}
                        style={{
                          padding: '6px 10px',
                          fontSize: '12px',
                          borderRadius: '6px',
                          border: '1px solid #bbf7d0',
                          background: '#f0fdf4',
                          color: '#15803d',
                          cursor: 'pointer',
                          display: 'flex',
                          alignItems: 'center',
                          gap: '4px',
                        }}
                      >
                        <Check size={12} /> Confirm Submit
                      </button>
                    )}
                  </div>

                  <button
                    onClick={() => onSelectApplication(app.applicationId)}
                    style={{
                      padding: '7px 14px',
                      background: '#2563eb',
                      color: '#ffffff',
                      border: 'none',
                      borderRadius: '8px',
                      fontSize: '13px',
                      fontWeight: 600,
                      cursor: 'pointer',
                      display: 'flex',
                      alignItems: 'center',
                      gap: '4px',
                    }}
                  >
                    Review Fields <ChevronRight size={14} />
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Manual Status Transition Dialog */}
      {transitionApp && (
        <div style={{
          position: 'fixed', top: 0, left: 0, right: 0, bottom: 0,
          backgroundColor: 'rgba(0,0,0,0.5)',
          display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
        }}>
          <div style={{ background: '#fff', borderRadius: '12px', padding: '24px', width: '420px', maxWidth: '90vw' }}>
            <h3 style={{ margin: '0 0 12px 0', fontSize: '17px' }}>Update Status: {transitionApp.jobTitle}</h3>
            <p style={{ fontSize: '13px', color: '#64748b', margin: '0 0 16px 0' }}>
              Current: <strong>{transitionApp.status}</strong>
            </p>

            <div style={{ marginBottom: '14px' }}>
              <label style={{ display: 'block', fontSize: '13px', fontWeight: 600, marginBottom: '6px' }}>Target Status</label>
              <select
                value={selectedTargetStatus}
                onChange={(e) => setSelectedTargetStatus(e.target.value as ApplicationStatus)}
                style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1' }}
              >
                <option value="InProgress">In Progress</option>
                <option value="AwaitingUserReview">Awaiting User Review</option>
                <option value="Submitted">Submitted</option>
                <option value="UnderReview">Under Review</option>
                <option value="Interview">Interview</option>
                <option value="Offer">Offer</option>
                <option value="Rejected">Rejected</option>
                <option value="Archived">Archived</option>
              </select>
            </div>

            <div style={{ marginBottom: '20px' }}>
              <label style={{ display: 'block', fontSize: '13px', fontWeight: 600, marginBottom: '6px' }}>Notes</label>
              <input
                type="text"
                value={transitionNotes}
                onChange={(e) => setTransitionNotes(e.target.value)}
                placeholder="Optional transition note"
                style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1', boxSizing: 'border-box' }}
              />
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
              <button
                onClick={() => setTransitionApp(null)}
                style={{ padding: '8px 14px', borderRadius: '6px', border: '1px solid #cbd5e1', background: '#fff', cursor: 'pointer' }}
              >
                Cancel
              </button>
              <button
                onClick={() => statusMutation.mutate({
                  id: transitionApp.applicationId,
                  status: selectedTargetStatus,
                  notes: transitionNotes,
                })}
                style={{ padding: '8px 16px', borderRadius: '6px', border: 'none', background: '#2563eb', color: '#fff', fontWeight: 600, cursor: 'pointer' }}
              >
                Update
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Manual Submission Confirmation Dialog */}
      {confirmApp && (
        <div style={{
          position: 'fixed', top: 0, left: 0, right: 0, bottom: 0,
          backgroundColor: 'rgba(0,0,0,0.5)',
          display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
        }}>
          <div style={{ background: '#fff', borderRadius: '12px', padding: '24px', width: '440px', maxWidth: '90vw' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '12px' }}>
              <div style={{ width: '32px', height: '32px', borderRadius: '8px', background: '#f0fdf4', color: '#16a34a', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                <ShieldCheck size={20} />
              </div>
              <h3 style={{ margin: 0, fontSize: '17px' }}>Confirm Application Submission</h3>
            </div>

            <p style={{ fontSize: '13px', color: '#475569', lineHeight: 1.5, margin: '0 0 16px 0' }}>
              <strong>Rule:</strong> JobPilot never clicks the final Submit button. After you personally submit the application on Workday, confirm here to mark it <strong>Submitted</strong>.
            </p>

            <div style={{ marginBottom: '20px' }}>
              <label style={{ display: 'block', fontSize: '13px', fontWeight: 600, marginBottom: '6px' }}>Workday Confirmation Reference (Optional)</label>
              <input
                type="text"
                value={submissionRef}
                onChange={(e) => setSubmissionRef(e.target.value)}
                placeholder="e.g. WD-REF-109283"
                style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1', boxSizing: 'border-box' }}
              />
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
              <button
                onClick={() => setConfirmApp(null)}
                style={{ padding: '8px 14px', borderRadius: '6px', border: '1px solid #cbd5e1', background: '#fff', cursor: 'pointer' }}
              >
                Cancel
              </button>
              <button
                onClick={() => confirmSubmissionMutation.mutate({
                  id: confirmApp.applicationId,
                  reference: submissionRef.trim() ? submissionRef.trim() : undefined,
                })}
                style={{ padding: '8px 18px', borderRadius: '6px', border: 'none', background: '#16a34a', color: '#fff', fontWeight: 600, cursor: 'pointer' }}
              >
                Confirm Submitted
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
