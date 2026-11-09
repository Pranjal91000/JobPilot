import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '../../api/client';
import type { FieldDetailDto, JobApplicationDto, SessionDto, ValidationReport } from '../../api/types';
import {
  ArrowLeft,
  Sparkles,
  CheckCircle,
  ShieldAlert,
  ShieldCheck,
  Check,
  Edit2,
  Play,
  Search,
  Send,
  ArrowRight,
  AlertTriangle,
  Info,
} from 'lucide-react';

interface Props {
  applicationId: string;
  onBack: () => void;
  onOpenAiSettings: () => void;
}

export const FieldMappingReview: React.FC<Props> = ({ applicationId, onBack, onOpenAiSettings }) => {
  const queryClient = useQueryClient();

  const [activeSection, setActiveSection] = useState<string>('All');
  const [editingFieldId, setEditingFieldId] = useState<string | null>(null);
  const [overrideValue, setOverrideValue] = useState<string>('');
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const [showApprovalModal, setShowApprovalModal] = useState<boolean>(false);
  const [validationReport, setValidationReport] = useState<ValidationReport | null>(null);

  const [statusNotification, setStatusNotification] = useState<string | null>(null);

  const { data: application } = useQuery<JobApplicationDto>({
    queryKey: ['application', applicationId],
    queryFn: () => apiClient.getApplication(applicationId),
  });

  // Query active session for this application on mount
  const { data: activeSessionData } = useQuery({
    queryKey: ['active-session', applicationId],
    queryFn: () => apiClient.getActiveSession(applicationId),
  });

  React.useEffect(() => {
    if (activeSessionData?.sessionId && !activeSessionId) {
      setActiveSessionId(activeSessionData.sessionId);
    }
  }, [activeSessionData, activeSessionId]);

  const { data: fields = [] } = useQuery<FieldDetailDto[]>({
    queryKey: ['application-fields', applicationId],
    queryFn: () => apiClient.getApplicationFields(applicationId),
  });

  const { data: aiSettings } = useQuery({
    queryKey: ['ai-settings'],
    queryFn: apiClient.getAiSettings,
  });

  // Active automation session polling
  const { data: session } = useQuery<SessionDto>({
    queryKey: ['session', activeSessionId],
    queryFn: () => apiClient.getSession(activeSessionId!),
    enabled: !!activeSessionId,
    refetchInterval: 2000,
  });

  const startSessionMutation = useMutation({
    mutationFn: () => apiClient.startAutomationSession(applicationId),
    onSuccess: (newSession) => {
      setActiveSessionId(newSession.sessionId);
      queryClient.invalidateQueries({ queryKey: ['application', applicationId] });
      queryClient.invalidateQueries({ queryKey: ['active-session', applicationId] });
    },
    onError: (err: any) => {
      alert(err.message || 'Failed to start browser session.');
    },
  });

  const inspectMutation = useMutation({
    mutationFn: () => apiClient.inspectSession(activeSessionId!),
    onSuccess: () => {
      setStatusNotification('Inspecting page elements and mapping profile facts...');
      setTimeout(() => {
        mapMutation.mutate();
        queryClient.invalidateQueries({ queryKey: ['application-fields', applicationId] });
        queryClient.invalidateQueries({ queryKey: ['session', activeSessionId] });
        setStatusNotification('Page inspected and mapped successfully!');
        setTimeout(() => setStatusNotification(null), 4000);
      }, 1500);
    },
    onError: (err: any) => {
      alert(err.message || 'Inspection failed. Ensure browser is open.');
    },
  });

  const autofillMutation = useMutation({
    mutationFn: () => apiClient.autofillSession(activeSessionId!),
    onSuccess: () => {
      setTimeout(() => {
        queryClient.invalidateQueries({ queryKey: ['application-fields', applicationId] });
        queryClient.invalidateQueries({ queryKey: ['session', activeSessionId] });
      }, 2000);
    },
  });

  const navigateMutation = useMutation({
    mutationFn: (direction: 'Next' | 'Back') => apiClient.navigateSession(activeSessionId!, direction),
    onSuccess: () => {
      setTimeout(() => {
        queryClient.invalidateQueries({ queryKey: ['application-fields', applicationId] });
        queryClient.invalidateQueries({ queryKey: ['session', activeSessionId] });
      }, 2000);
    },
  });

  const mapMutation = useMutation({
    mutationFn: () => apiClient.mapApplicationFields(applicationId),
    onSuccess: (newFields) => {
      queryClient.setQueryData(['application-fields', applicationId], newFields);
    },
  });

  const decisionMutation = useMutation({
    mutationFn: ({
      fieldId,
      action,
      value,
      optionKey,
    }: {
      fieldId: string;
      action: 'Approve' | 'SetValue' | 'Reject';
      value?: string;
      optionKey?: string;
    }) => apiClient.makeFieldDecision(applicationId, fieldId, action, value, optionKey),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['application-fields', applicationId] });
      setEditingFieldId(null);
      setOverrideValue('');
    },
  });

  const bulkApproveMutation = useMutation({
    mutationFn: () => apiClient.bulkApproveFields(applicationId),
    onSuccess: (res) => {
      queryClient.invalidateQueries({ queryKey: ['application-fields', applicationId] });
      alert(`Bulk approved ${res.approvedCount} high-confidence deterministic mappings!`);
    },
  });

  const handleValidateAndApprove = async () => {
    if (!activeSessionId) {
      alert('Please launch a browser automation session first.');
      return;
    }
    try {
      const report = await apiClient.validateSession(activeSessionId);
      setValidationReport(report);
      setShowApprovalModal(true);
    } catch (err: any) {
      alert(err.message || 'Validation failed');
    }
  };

  const handleConfirmApproval = async () => {
    if (!activeSessionId || !validationReport) return;
    try {
      await apiClient.approveSubmission(activeSessionId, validationReport.reportId);
      setShowApprovalModal(false);
      queryClient.invalidateQueries({ queryKey: ['application', applicationId] });
      queryClient.invalidateQueries({ queryKey: ['session', activeSessionId] });
    } catch (err: any) {
      alert(err.message || 'Failed to approve submission');
    }
  };

  const sections = ['All', ...Array.from(new Set(fields.map((f) => f.section || 'General')))];

  const filteredFields = fields.filter((f) => {
    if (activeSection === 'All') return true;
    return (f.section || 'General') === activeSection;
  });

  const getMethodBadge = (method?: string) => {
    switch (method) {
      case 'AdapterRule':
        return { label: 'Workday Rule', bg: '#eff6ff', text: '#1d4ed8', border: '#bfdbfe' };
      case 'Dictionary':
        return { label: 'Dictionary Match', bg: '#eef2ff', text: '#4338ca', border: '#c7d2fe' };
      case 'SavedAnswer':
        return { label: 'Saved Answer', bg: '#faf5ff', text: '#7e22ce', border: '#e9d5ff' };
      case 'Llm':
        return { label: 'AI Suggested', bg: '#fef3c7', text: '#b45309', border: '#fde68a' };
      default:
        return { label: 'Manual Entry', bg: '#f1f5f9', text: '#475569', border: '#cbd5e1' };
    }
  };

  const getConfidenceBadge = (score: number) => {
    const pct = Math.round(score * 100);
    if (pct >= 90) return { pct, color: '#15803d', bg: '#f0fdf4', border: '#bbf7d0' };
    if (pct >= 70) return { pct, color: '#b45309', bg: '#fffbeb', border: '#fde68a' };
    return { pct, color: '#b91c1c', bg: '#fef2f2', border: '#fecaca' };
  };

  return (
    <div style={{ maxWidth: '1240px', margin: '0 auto', padding: '24px' }}>
      {/* Top Bar */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '20px' }}>
        <button
          onClick={onBack}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '6px',
            background: 'transparent',
            border: 'none',
            fontSize: '14px',
            color: '#475569',
            fontWeight: 500,
            cursor: 'pointer',
          }}
        >
          <ArrowLeft size={16} /> Back to Applications
        </button>

        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
          <div
            style={{
              fontSize: '12px',
              padding: '5px 12px',
              borderRadius: '20px',
              background: session?.workerOnline ? '#f0fdf4' : '#f8fafc',
              border: `1px solid ${session?.workerOnline ? '#bbf7d0' : '#e2e8f0'}`,
              color: session?.workerOnline ? '#15803d' : '#64748b',
              display: 'flex',
              alignItems: 'center',
              gap: '6px',
              fontWeight: 600,
            }}
          >
            <span
              style={{
                width: '8px',
                height: '8px',
                borderRadius: '50%',
                background: session?.workerOnline ? '#22c55e' : '#94a3b8',
              }}
            />
            {session?.workerOnline ? 'Worker Online' : 'Worker Standby'}
          </div>

          <div
            style={{
              fontSize: '12px',
              padding: '4px 10px',
              borderRadius: '12px',
              background: aiSettings?.isEnabled && aiSettings?.hasApiKey ? '#eff6ff' : '#f8fafc',
              border: `1px solid ${aiSettings?.isEnabled && aiSettings?.hasApiKey ? '#bfdbfe' : '#e2e8f0'}`,
              color: aiSettings?.isEnabled && aiSettings?.hasApiKey ? '#1d4ed8' : '#64748b',
              display: 'flex',
              alignItems: 'center',
              gap: '6px',
            }}
          >
            <Sparkles size={13} />
            {aiSettings?.isEnabled && aiSettings?.hasApiKey ? 'AI Engine: Active' : 'AI Engine: Deterministic Only'}
          </div>

          <button
            onClick={onOpenAiSettings}
            style={{
              fontSize: '12px',
              padding: '5px 12px',
              borderRadius: '6px',
              border: '1px solid #cbd5e1',
              background: '#fff',
              cursor: 'pointer',
              color: '#334155',
              fontWeight: 500,
            }}
          >
            AI Settings
          </button>
        </div>
      </div>

      {/* Main Automation & Application Control Bar */}
      <div
        style={{
          background: '#ffffff',
          border: '1px solid #e2e8f0',
          borderRadius: '16px',
          padding: '24px',
          boxShadow: '0 4px 6px -1px rgba(0,0,0,0.04)',
          marginBottom: '20px',
        }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '16px' }}>
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <span style={{ fontSize: '13px', fontWeight: 600, color: '#2563eb' }}>
                {application?.companyName || 'Workday Application'}
              </span>
              {session?.currentStepKey && (
                <span style={{ background: '#f1f5f9', color: '#475569', fontSize: '12px', padding: '2px 8px', borderRadius: '4px', fontWeight: 600 }}>
                  Step: {session.currentStepKey}
                </span>
              )}
            </div>
            <h1 style={{ margin: '4px 0 6px 0', fontSize: '22px', fontWeight: 700, color: '#0f172a' }}>
              {application?.jobTitle || 'Application Field Review & Autofill'}
            </h1>
            <p style={{ margin: 0, fontSize: '13px', color: '#64748b' }}>
              {fields.length} detected fields | Session Status:{' '}
              <strong style={{ color: '#0f172a' }}>{session?.sessionStatus || 'Not Started'}</strong>
            </p>
          </div>

          {/* Action Button Strip */}
          <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
            {!activeSessionId ? (
              <button
                onClick={() => startSessionMutation.mutate()}
                disabled={startSessionMutation.isPending}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '6px',
                  padding: '9px 16px',
                  background: '#0284c7',
                  color: '#fff',
                  border: 'none',
                  borderRadius: '8px',
                  fontWeight: 600,
                  fontSize: '13px',
                  cursor: 'pointer',
                }}
              >
                <Play size={15} />
                {startSessionMutation.isPending ? 'Launching Browser...' : 'Launch Chromium Session'}
              </button>
            ) : (
              <>
                <button
                  onClick={() => inspectMutation.mutate()}
                  disabled={inspectMutation.isPending}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '6px',
                    padding: '9px 14px',
                    background: '#f8fafc',
                    color: '#334155',
                    border: '1px solid #cbd5e1',
                    borderRadius: '8px',
                    fontWeight: 600,
                    fontSize: '13px',
                    cursor: 'pointer',
                  }}
                  title="Inspect Workday page to extract detected fields"
                >
                  <Search size={15} />
                  {inspectMutation.isPending ? 'Inspecting...' : 'Inspect Page'}
                </button>

                <button
                  onClick={() => mapMutation.mutate()}
                  disabled={mapMutation.isPending}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '6px',
                    padding: '9px 14px',
                    background: '#2563eb',
                    color: '#fff',
                    border: 'none',
                    borderRadius: '8px',
                    fontWeight: 600,
                    fontSize: '13px',
                    cursor: 'pointer',
                  }}
                >
                  <Sparkles size={15} />
                  {mapMutation.isPending ? 'Mapping...' : 'Run Mapping'}
                </button>

                <button
                  onClick={() => bulkApproveMutation.mutate()}
                  disabled={bulkApproveMutation.isPending}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '6px',
                    padding: '9px 14px',
                    background: '#f0fdf4',
                    color: '#166534',
                    border: '1px solid #bbf7d0',
                    borderRadius: '8px',
                    fontWeight: 600,
                    fontSize: '13px',
                    cursor: 'pointer',
                  }}
                >
                  <Check size={15} />
                  Approve High-Confidence
                </button>

                <button
                  onClick={() => autofillMutation.mutate()}
                  disabled={autofillMutation.isPending}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '6px',
                    padding: '9px 16px',
                    background: '#16a34a',
                    color: '#fff',
                    border: 'none',
                    borderRadius: '8px',
                    fontWeight: 600,
                    fontSize: '13px',
                    cursor: 'pointer',
                  }}
                >
                  <Send size={15} />
                  {autofillMutation.isPending ? 'Filling in Chromium...' : 'Autofill Form'}
                </button>

                <button
                  onClick={() => navigateMutation.mutate('Next')}
                  disabled={navigateMutation.isPending}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '6px',
                    padding: '9px 14px',
                    background: '#f8fafc',
                    color: '#334155',
                    border: '1px solid #cbd5e1',
                    borderRadius: '8px',
                    fontWeight: 600,
                    fontSize: '13px',
                    cursor: 'pointer',
                  }}
                >
                  Next Step <ArrowRight size={15} />
                </button>

                <button
                  onClick={handleValidateAndApprove}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '6px',
                    padding: '9px 16px',
                    background: '#047857',
                    color: '#fff',
                    border: 'none',
                    borderRadius: '8px',
                    fontWeight: 600,
                    fontSize: '13px',
                    cursor: 'pointer',
                  }}
                >
                  <ShieldCheck size={15} />
                  Ready to Submit
                </button>
              </>
            )}
          </div>
        </div>

        {session?.sessionStatus === 'AwaitingAuthentication' && (
          <div
            style={{
              marginTop: '16px',
              padding: '14px 18px',
              borderRadius: '12px',
              background: '#fffbeb',
              border: '1px solid #fef3c7',
              color: '#92400e',
              fontSize: '13px',
              display: 'flex',
              alignItems: 'center',
              gap: '12px',
            }}
          >
            <AlertTriangle size={18} style={{ flexShrink: 0, color: '#d97706' }} />
            <div>
              <strong>Action Required in Chromium:</strong> The Workday portal is asking for candidate login or navigation. Please log in or navigate to the form in the Chromium browser window, then click <strong>Inspect Page</strong>.
            </div>
          </div>
        )}

        {statusNotification && (
          <div
            style={{
              marginTop: '16px',
              padding: '12px 18px',
              borderRadius: '12px',
              background: '#f0fdf4',
              border: '1px solid #bbf7d0',
              color: '#166534',
              fontSize: '13px',
              display: 'flex',
              alignItems: 'center',
              gap: '10px',
            }}
          >
            <CheckCircle size={18} style={{ color: '#16a34a' }} />
            <span>{statusNotification}</span>
          </div>
        )}

        {/* Workday Submission Safety Banner */}
        <div
          style={{
            marginTop: '18px',
            padding: '12px 16px',
            borderRadius: '10px',
            background: '#eff6ff',
            border: '1px solid #bfdbfe',
            display: 'flex',
            alignItems: 'center',
            gap: '10px',
            fontSize: '13px',
            color: '#1e40af',
          }}
        >
          <ShieldCheck size={18} style={{ flexShrink: 0 }} />
          <div>
            <strong>Strict Workday Safety Rule:</strong> JobPilot automates form field review and autofill with verified profile grounding, but will{' '}
            <strong>never click the final Workday Submit button</strong>. You will always review the filled form in the visible Chromium window and personally click Submit.
          </div>
        </div>
      </div>

      {/* Section Tabs */}
      <div style={{ display: 'flex', gap: '8px', marginBottom: '16px', overflowX: 'auto', paddingBottom: '4px' }}>
        {sections.map((sec) => (
          <button
            key={sec}
            onClick={() => setActiveSection(sec)}
            style={{
              padding: '6px 14px',
              borderRadius: '20px',
              border: activeSection === sec ? '1px solid #2563eb' : '1px solid #e2e8f0',
              background: activeSection === sec ? '#2563eb' : '#fff',
              color: activeSection === sec ? '#fff' : '#64748b',
              fontWeight: 600,
              fontSize: '12px',
              cursor: 'pointer',
              whiteSpace: 'nowrap',
            }}
          >
            {sec} ({sec === 'All' ? fields.length : fields.filter((f) => (f.section || 'General') === sec).length})
          </button>
        ))}
      </div>

      {/* Review Table */}
      <div
        style={{
          background: '#ffffff',
          border: '1px solid #e2e8f0',
          borderRadius: '16px',
          boxShadow: '0 4px 6px -1px rgba(0,0,0,0.04)',
          overflow: 'hidden',
        }}
      >
        <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left', fontSize: '13px' }}>
          <thead>
            <tr style={{ background: '#f8fafc', borderBottom: '1px solid #e2e8f0' }}>
              <th style={{ padding: '14px 16px', fontWeight: 600, color: '#475569' }}>Field & Section</th>
              <th style={{ padding: '14px 16px', fontWeight: 600, color: '#475569' }}>Type & Status</th>
              <th style={{ padding: '14px 16px', fontWeight: 600, color: '#475569' }}>Current Page Value</th>
              <th style={{ padding: '14px 16px', fontWeight: 600, color: '#475569' }}>Suggested Value</th>
              <th style={{ padding: '14px 16px', fontWeight: 600, color: '#475569' }}>Source & Method</th>
              <th style={{ padding: '14px 16px', fontWeight: 600, color: '#475569' }}>Confidence</th>
              <th style={{ padding: '14px 16px', fontWeight: 600, color: '#475569', textAlign: 'right' }}>Action</th>
            </tr>
          </thead>
          <tbody>
            {filteredFields.map((field) => {
              const isSensitive = field.mapping?.isSensitive || false;
              const methodBadge = getMethodBadge(field.mapping?.method);
              const conf = getConfidenceBadge(field.mapping?.confidenceScore || 0);
              const isApproved = field.mapping?.status === 'Approved';
              const isEditing = editingFieldId === field.fieldId;
              const displayVal = field.answer?.answerValue || field.mapping?.suggestedValue || '';

              return (
                <tr key={field.fieldId} style={{ borderBottom: '1px solid #f1f5f9', background: isSensitive ? '#fffdf5' : '#fff' }}>
                  <td style={{ padding: '14px 16px', verticalAlign: 'top' }}>
                    <div style={{ fontWeight: 600, color: '#0f172a' }}>
                      {field.label} {field.required && <span style={{ color: '#ef4444' }}>*</span>}
                    </div>
                    <div style={{ fontSize: '11px', color: '#64748b', marginTop: '2px' }}>
                      {field.section || 'General'} | <code>{field.fieldIdentifier}</code>
                    </div>
                  </td>

                  <td style={{ padding: '14px 16px', verticalAlign: 'top' }}>
                    <span style={{ fontSize: '11px', fontWeight: 600, color: '#475569', background: '#f1f5f9', padding: '2px 8px', borderRadius: '4px' }}>
                      {field.type}
                    </span>
                    {isSensitive && (
                      <div style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', fontSize: '11px', color: '#b45309', fontWeight: 600, marginTop: '4px', background: '#fef3c7', padding: '2px 6px', borderRadius: '4px' }}>
                        <ShieldAlert size={12} /> Sensitive (Manual)
                      </div>
                    )}
                  </td>

                  <td style={{ padding: '14px 16px', verticalAlign: 'top', color: field.pageValue ? '#0f172a' : '#94a3b8' }}>
                    {field.pageValue || <em>(empty)</em>}
                  </td>

                  <td style={{ padding: '14px 16px', verticalAlign: 'top' }}>
                    {isEditing ? (
                      <div style={{ display: 'flex', gap: '6px' }}>
                        <input
                          type="text"
                          value={overrideValue}
                          onChange={(e) => setOverrideValue(e.target.value)}
                          style={{
                            padding: '4px 8px',
                            border: '1px solid #2563eb',
                            borderRadius: '4px',
                            fontSize: '13px',
                            width: '180px',
                          }}
                        />
                        <button
                          onClick={() =>
                            decisionMutation.mutate({
                              fieldId: field.fieldId,
                              action: 'SetValue',
                              value: overrideValue,
                            })
                          }
                          style={{ padding: '4px 8px', background: '#2563eb', color: '#fff', border: 'none', borderRadius: '4px', cursor: 'pointer' }}
                        >
                          Save
                        </button>
                      </div>
                    ) : (
                      <div style={{ fontWeight: 600, color: displayVal ? '#0f172a' : '#94a3b8' }}>
                        {displayVal || <em>(none)</em>}
                        {field.mapping?.reason && (
                          <div style={{ fontSize: '11px', fontWeight: 400, color: '#64748b', marginTop: '2px' }}>
                            {field.mapping.reason}
                          </div>
                        )}
                      </div>
                    )}
                  </td>

                  <td style={{ padding: '14px 16px', verticalAlign: 'top' }}>
                    <span
                      style={{
                        fontSize: '11px',
                        fontWeight: 600,
                        padding: '3px 8px',
                        borderRadius: '6px',
                        background: methodBadge.bg,
                        color: methodBadge.text,
                        border: `1px solid ${methodBadge.border}`,
                      }}
                    >
                      {methodBadge.label}
                    </span>
                  </td>

                  <td style={{ padding: '14px 16px', verticalAlign: 'top' }}>
                    {field.mapping ? (
                      <span
                        style={{
                          fontSize: '11px',
                          fontWeight: 700,
                          padding: '2px 8px',
                          borderRadius: '10px',
                          background: conf.bg,
                          color: conf.color,
                          border: `1px solid ${conf.border}`,
                        }}
                      >
                        {conf.pct}%
                      </span>
                    ) : (
                      <span style={{ color: '#94a3b8' }}>-</span>
                    )}
                  </td>

                  <td style={{ padding: '14px 16px', verticalAlign: 'top', textAlign: 'right' }}>
                    <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '6px' }}>
                      {isApproved ? (
                        <span
                          style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            gap: '4px',
                            fontSize: '12px',
                            color: '#15803d',
                            fontWeight: 600,
                            padding: '4px 8px',
                            borderRadius: '6px',
                            background: '#f0fdf4',
                          }}
                        >
                          <CheckCircle size={14} /> Approved
                        </span>
                      ) : (
                        <button
                          onClick={() => decisionMutation.mutate({ fieldId: field.fieldId, action: 'Approve' })}
                          style={{
                            padding: '5px 10px',
                            borderRadius: '6px',
                            background: '#2563eb',
                            color: '#fff',
                            border: 'none',
                            fontSize: '12px',
                            fontWeight: 600,
                            cursor: 'pointer',
                          }}
                        >
                          Approve
                        </button>
                      )}

                      <button
                        onClick={() => {
                          setEditingFieldId(field.fieldId);
                          setOverrideValue(displayVal);
                        }}
                        style={{
                          padding: '5px 8px',
                          borderRadius: '6px',
                          background: '#f8fafc',
                          color: '#475569',
                          border: '1px solid #cbd5e1',
                          cursor: 'pointer',
                        }}
                        title="Edit value"
                      >
                        <Edit2 size={13} />
                      </button>
                    </div>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {/* Submission Approval Modal */}
      {showApprovalModal && validationReport && (
        <div
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(15, 23, 42, 0.6)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 1000,
            padding: '20px',
          }}
        >
          <div
            style={{
              background: '#ffffff',
              borderRadius: '16px',
              maxWidth: '560px',
              width: '100%',
              padding: '28px',
              boxShadow: '0 20px 25px -5px rgba(0,0,0,0.1)',
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '16px' }}>
              <div
                style={{
                  width: '40px',
                  height: '40px',
                  borderRadius: '50%',
                  background: validationReport.isSubmittable ? '#ecfdf5' : '#fef2f2',
                  color: validationReport.isSubmittable ? '#059669' : '#dc2626',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                }}
              >
                {validationReport.isSubmittable ? <ShieldCheck size={24} /> : <AlertTriangle size={24} />}
              </div>
              <div>
                <h2 style={{ margin: 0, fontSize: '18px', fontWeight: 700, color: '#0f172a' }}>
                  {validationReport.isSubmittable ? 'Application Ready for Manual Submission' : 'Validation Issues Detected'}
                </h2>
                <div style={{ fontSize: '13px', color: '#64748b' }}>
                  Report ID: <code>{validationReport.reportId.slice(0, 13)}</code>
                </div>
              </div>
            </div>

            {validationReport.blocking.length > 0 ? (
              <div>
                <p style={{ fontSize: '14px', color: '#ef4444', fontWeight: 600 }}>
                  The following required fields must be resolved before proceeding:
                </p>
                <ul style={{ fontSize: '13px', color: '#334155', paddingLeft: '20px' }}>
                  {validationReport.blocking.map((b, i) => (
                    <li key={i}>{b.message}</li>
                  ))}
                </ul>
              </div>
            ) : (
              <div>
                <div
                  style={{
                    background: '#f8fafc',
                    border: '1px solid #e2e8f0',
                    borderRadius: '10px',
                    padding: '16px',
                    marginBottom: '20px',
                    fontSize: '13px',
                    color: '#334155',
                    lineHeight: '1.6',
                  }}
                >
                  <p style={{ margin: '0 0 10px 0', fontWeight: 600, color: '#0f172a' }}>
                    ✓ All required form fields are verified and approved.
                  </p>
                  <p style={{ margin: 0 }}>
                    <strong>Next Step:</strong> Per JobPilot Safety Invariant §18, the system is technically prohibited from clicking the final Submit button.
                    Please switch to the visible <strong>Chromium browser window</strong>, review the final page, and click <strong>Submit Application</strong> personally.
                  </p>
                </div>

                <div style={{ fontSize: '12px', color: '#64748b', display: 'flex', alignItems: 'center', gap: '6px' }}>
                  <Info size={14} /> JobPilot will automatically watch for confirmation and record your submission receipt.
                </div>
              </div>
            )}

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '24px' }}>
              <button
                onClick={() => setShowApprovalModal(false)}
                style={{
                  padding: '9px 18px',
                  background: '#f8fafc',
                  border: '1px solid #cbd5e1',
                  borderRadius: '8px',
                  fontSize: '13px',
                  fontWeight: 600,
                  color: '#475569',
                  cursor: 'pointer',
                }}
              >
                Close
              </button>
              {validationReport.isSubmittable && (
                <button
                  onClick={handleConfirmApproval}
                  style={{
                    padding: '9px 20px',
                    background: '#059669',
                    border: 'none',
                    borderRadius: '8px',
                    fontSize: '13px',
                    fontWeight: 600,
                    color: '#fff',
                    cursor: 'pointer',
                  }}
                >
                  Confirm & Watch for Submission
                </button>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
