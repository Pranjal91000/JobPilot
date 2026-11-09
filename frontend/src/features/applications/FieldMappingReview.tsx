import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '../../api/client';
import type { FieldDetailDto, JobApplicationDto } from '../../api/types';
import {
  ArrowLeft,
  Sparkles,
  CheckCircle,
  ShieldAlert,
  ShieldCheck,
  Check,
  Edit2,
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

  const { data: application } = useQuery<JobApplicationDto>({
    queryKey: ['application', applicationId],
    queryFn: () => apiClient.getApplication(applicationId),
  });

  const { data: fields = [] } = useQuery<FieldDetailDto[]>({
    queryKey: ['application-fields', applicationId],
    queryFn: () => apiClient.getApplicationFields(applicationId),
  });

  const { data: aiSettings } = useQuery({
    queryKey: ['ai-settings'],
    queryFn: apiClient.getAiSettings,
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
    <div style={{ maxWidth: '1200px', margin: '0 auto', padding: '24px' }}>
      {/* Top Navigation */}
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

        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
          <div style={{
            fontSize: '12px',
            padding: '4px 10px',
            borderRadius: '12px',
            background: aiSettings?.isEnabled && aiSettings?.hasApiKey ? '#f0fdf4' : '#f8fafc',
            border: `1px solid ${aiSettings?.isEnabled && aiSettings?.hasApiKey ? '#bbf7d0' : '#e2e8f0'}`,
            color: aiSettings?.isEnabled && aiSettings?.hasApiKey ? '#15803d' : '#64748b',
            display: 'flex',
            alignItems: 'center',
            gap: '6px',
          }}>
            <Sparkles size={13} />
            {aiSettings?.isEnabled && aiSettings?.hasApiKey ? 'AI Engine: Enabled' : 'AI Engine: Deterministic Only'}
          </div>
          <button
            onClick={onOpenAiSettings}
            style={{
              fontSize: '12px',
              padding: '4px 10px',
              borderRadius: '6px',
              border: '1px solid #cbd5e1',
              background: '#fff',
              cursor: 'pointer',
              color: '#334155',
            }}
          >
            Settings
          </button>
        </div>
      </div>

      {/* Application Summary Header */}
      <div style={{
        background: '#ffffff',
        border: '1px solid #e2e8f0',
        borderRadius: '16px',
        padding: '24px',
        boxShadow: '0 4px 6px -1px rgba(0,0,0,0.04)',
        marginBottom: '20px',
      }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '16px' }}>
          <div>
            <span style={{ fontSize: '13px', fontWeight: 600, color: '#2563eb' }}>{application?.companyName || 'Workday Application'}</span>
            <h1 style={{ margin: '4px 0 6px 0', fontSize: '22px', fontWeight: 700, color: '#0f172a' }}>
              {application?.jobTitle || 'Application Field Review & Autofill'}
            </h1>
            <p style={{ margin: 0, fontSize: '13px', color: '#64748b' }}>
              {fields.length} detected fields | Review mappings, verify grounding, and approve values before writing to browser.
            </p>
          </div>

          <div style={{ display: 'flex', gap: '10px' }}>
            <button
              onClick={() => mapMutation.mutate()}
              disabled={mapMutation.isPending}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                padding: '10px 18px',
                background: '#2563eb',
                color: '#fff',
                border: 'none',
                borderRadius: '10px',
                fontWeight: 600,
                fontSize: '13px',
                cursor: 'pointer',
              }}
            >
              <Sparkles size={16} />
              {mapMutation.isPending ? 'Running Pipeline...' : 'Run Mapping Engine'}
            </button>

            <button
              onClick={() => bulkApproveMutation.mutate()}
              disabled={bulkApproveMutation.isPending}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                padding: '10px 18px',
                background: '#f0fdf4',
                color: '#166534',
                border: '1px solid #bbf7d0',
                borderRadius: '10px',
                fontWeight: 600,
                fontSize: '13px',
                cursor: 'pointer',
              }}
            >
              <Check size={16} />
              Bulk Approve Deterministic (≥90%)
            </button>
          </div>
        </div>
      </div>

      {/* Safety Invariant Notice */}
      <div style={{
        padding: '14px 18px',
        background: '#eff6ff',
        border: '1px solid #bfdbfe',
        borderRadius: '12px',
        marginBottom: '24px',
        display: 'flex',
        alignItems: 'center',
        gap: '12px',
        color: '#1e40af',
        fontSize: '13px',
      }}>
        <ShieldCheck size={24} style={{ color: '#2563eb', flexShrink: 0 }} />
        <div>
          <strong>Safety Boundary Guarantee (§4):</strong> JobPilot will <em>never</em> click, trigger, or automate Workday's final Submit action.
          You personally review all autofilled fields in visible Chromium and execute the final submission.
        </div>
      </div>

      {/* Section Tabs */}
      <div style={{ display: 'flex', gap: '8px', marginBottom: '20px', overflowX: 'auto', paddingBottom: '4px' }}>
        {sections.map((section) => (
          <button
            key={section}
            onClick={() => setActiveSection(section)}
            style={{
              padding: '8px 16px',
              borderRadius: '20px',
              border: activeSection === section ? '1px solid #2563eb' : '1px solid #e2e8f0',
              background: activeSection === section ? '#2563eb' : '#ffffff',
              color: activeSection === section ? '#ffffff' : '#475569',
              fontSize: '13px',
              fontWeight: 500,
              cursor: 'pointer',
              whiteSpace: 'nowrap',
            }}
          >
            {section}
          </button>
        ))}
      </div>

      {/* Fields List */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
        {filteredFields.map((field) => {
          const mapping = field.mapping;
          const methodStyle = getMethodBadge(mapping?.method);
          const confStyle = mapping ? getConfidenceBadge(mapping.confidenceScore) : null;
          const isApproved = field.answer?.isUserApproved;
          const isSensitive = mapping?.isSensitive;
          const isEditing = editingFieldId === field.fieldId;

          return (
            <div
              key={field.fieldId}
              style={{
                background: '#ffffff',
                border: `1px solid ${isSensitive ? '#fecaca' : isApproved ? '#bbf7d0' : '#e2e8f0'}`,
                borderRadius: '12px',
                padding: '18px 20px',
                boxShadow: '0 2px 4px rgba(0,0,0,0.02)',
              }}
            >
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '12px' }}>
                <div style={{ flex: 1, minWidth: '280px' }}>
                  {/* Field Label & Flags */}
                  <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '4px' }}>
                    <h3 style={{ margin: 0, fontSize: '15px', fontWeight: 600, color: '#0f172a' }}>
                      {field.label} {field.required && <span style={{ color: '#ef4444' }}>*</span>}
                    </h3>
                    <span style={{ fontSize: '11px', color: '#64748b', background: '#f1f5f9', padding: '2px 6px', borderRadius: '4px' }}>
                      {field.type}
                    </span>
                  </div>

                  <div style={{ fontSize: '12px', color: '#94a3b8', marginBottom: '10px' }}>
                    ID: <code>{field.fieldIdentifier}</code> | Section: {field.section || 'General'}
                  </div>

                  {/* Sensitive Question Banner */}
                  {isSensitive && (
                    <div style={{
                      padding: '8px 12px',
                      background: '#fef2f2',
                      border: '1px solid #fee2e2',
                      borderRadius: '8px',
                      color: '#991b1b',
                      fontSize: '12px',
                      display: 'flex',
                      alignItems: 'center',
                      gap: '8px',
                      marginBottom: '10px',
                    }}>
                      <ShieldAlert size={16} style={{ color: '#dc2626' }} />
                      <span><strong>Sensitive Question Gate:</strong> Automatic fill blocked by safety policy. Manual decision required.</span>
                    </div>
                  )}

                  {/* Value / Suggestion Display */}
                  {isApproved ? (
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '14px', color: '#166534' }}>
                      <CheckCircle size={16} />
                      <span><strong>Approved Value:</strong> {field.answer?.answerValue || '(Empty)'}</span>
                    </div>
                  ) : isEditing ? (
                    <div style={{ display: 'flex', gap: '8px', marginTop: '6px' }}>
                      <input
                        type="text"
                        value={overrideValue}
                        onChange={(e) => setOverrideValue(e.target.value)}
                        placeholder="Enter custom value"
                        style={{ padding: '6px 10px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '13px', width: '260px' }}
                      />
                      <button
                        onClick={() => decisionMutation.mutate({
                          fieldId: field.fieldId,
                          action: 'SetValue',
                          value: overrideValue,
                        })}
                        style={{ padding: '6px 12px', background: '#2563eb', color: '#fff', border: 'none', borderRadius: '6px', fontSize: '12px', fontWeight: 600, cursor: 'pointer' }}
                      >
                        Save
                      </button>
                      <button
                        onClick={() => setEditingFieldId(null)}
                        style={{ padding: '6px 10px', background: '#fff', border: '1px solid #cbd5e1', borderRadius: '6px', fontSize: '12px', cursor: 'pointer' }}
                      >
                        Cancel
                      </button>
                    </div>
                  ) : mapping?.suggestedValue ? (
                    <div style={{ fontSize: '14px', color: '#334155' }}>
                      <strong>Suggested Value:</strong>{' '}
                      <span style={{ background: '#f8fafc', padding: '2px 8px', borderRadius: '4px', border: '1px solid #e2e8f0' }}>
                        {mapping.suggestedValue}
                      </span>
                      {mapping.profilePath && (
                        <span style={{ fontSize: '12px', color: '#64748b', marginLeft: '8px' }}>
                          (from <code>{mapping.profilePath}</code>)
                        </span>
                      )}
                    </div>
                  ) : (
                    <div style={{ fontSize: '13px', color: '#94a3b8', fontStyle: 'italic' }}>
                      No value suggested yet. Click 'Run Mapping Engine' or type a manual answer.
                    </div>
                  )}
                </div>

                {/* Right Badges & Controls */}
                <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: '8px' }}>
                  <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
                    {mapping && (
                      <span style={{
                        padding: '3px 8px',
                        borderRadius: '6px',
                        fontSize: '11px',
                        fontWeight: 600,
                        background: methodStyle.bg,
                        color: methodStyle.text,
                        border: `1px solid ${methodStyle.border}`,
                      }}>
                        {methodStyle.label}
                      </span>
                    )}

                    {confStyle && (
                      <span style={{
                        padding: '3px 8px',
                        borderRadius: '6px',
                        fontSize: '11px',
                        fontWeight: 700,
                        background: confStyle.bg,
                        color: confStyle.color,
                        border: `1px solid ${confStyle.border}`,
                      }}>
                        {confStyle.pct}% Conf
                      </span>
                    )}
                  </div>

                  {/* Actions */}
                  <div style={{ display: 'flex', gap: '6px', marginTop: '6px' }}>
                    {!isApproved && mapping?.suggestedValue && (
                      <button
                        onClick={() => decisionMutation.mutate({ fieldId: field.fieldId, action: 'Approve' })}
                        style={{
                          padding: '6px 12px',
                          background: '#16a34a',
                          color: '#fff',
                          border: 'none',
                          borderRadius: '6px',
                          fontSize: '12px',
                          fontWeight: 600,
                          cursor: 'pointer',
                          display: 'flex',
                          alignItems: 'center',
                          gap: '4px',
                        }}
                      >
                        <Check size={13} /> Approve
                      </button>
                    )}

                    <button
                      onClick={() => {
                        setEditingFieldId(field.fieldId);
                        setOverrideValue(field.answer?.answerValue || mapping?.suggestedValue || '');
                      }}
                      style={{
                        padding: '6px 10px',
                        background: '#fff',
                        border: '1px solid #cbd5e1',
                        borderRadius: '6px',
                        fontSize: '12px',
                        color: '#475569',
                        cursor: 'pointer',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '4px',
                      }}
                    >
                      <Edit2 size={12} /> {isApproved ? 'Change' : 'Override'}
                    </button>

                    <button
                      onClick={() => decisionMutation.mutate({ fieldId: field.fieldId, action: 'Reject' })}
                      style={{
                        padding: '6px 10px',
                        background: '#fff',
                        border: '1px solid #fecaca',
                        borderRadius: '6px',
                        fontSize: '12px',
                        color: '#b91c1c',
                        cursor: 'pointer',
                      }}
                    >
                      Reject
                    </button>
                  </div>
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};
