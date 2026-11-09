import React, { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '../../api/client';
import type { AiSettingsDto, UpdateAiSettingsRequest } from '../../api/types';
import { Sparkles, Key, CheckCircle, AlertTriangle, X, Shield, RefreshCw } from 'lucide-react';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

export const AiSettingsModal: React.FC<Props> = ({ isOpen, onClose }) => {
  const queryClient = useQueryClient();

  const { data: settings } = useQuery<AiSettingsDto>({
    queryKey: ['ai-settings'],
    queryFn: apiClient.getAiSettings,
    enabled: isOpen,
  });

  const [provider, setProvider] = useState('Google Gemini');
  const [model, setModel] = useState('gemini-1.5-flash');
  const [isEnabled, setIsEnabled] = useState(false);
  const [apiKey, setApiKey] = useState('');
  const [testResult, setTestResult] = useState<{ success: boolean; message: string; latency?: number | null } | null>(null);

  useEffect(() => {
    if (settings) {
      setProvider(settings.providerName || 'Google Gemini');
      setModel(settings.defaultModel || 'gemini-1.5-flash');
      setIsEnabled(settings.isEnabled);
    }
  }, [settings]);

  const updateMutation = useMutation({
    mutationFn: (data: UpdateAiSettingsRequest) => apiClient.updateAiSettings(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['ai-settings'] });
      setApiKey('');
    },
  });

  const testMutation = useMutation({
    mutationFn: apiClient.testAiConnection,
    onSuccess: (res) => {
      setTestResult({ success: res.success, message: res.message, latency: res.latencyMs });
    },
    onError: (err: any) => {
      setTestResult({ success: false, message: err.message || 'Connection test failed' });
    },
  });

  if (!isOpen) return null;

  const handleSave = (e: React.FormEvent) => {
    e.preventDefault();
    updateMutation.mutate({
      providerName: provider,
      defaultModel: model,
      isEnabled,
      apiKey: apiKey.trim() ? apiKey.trim() : null,
    });
  };

  return (
    <div style={{
      position: 'fixed',
      top: 0,
      left: 0,
      right: 0,
      bottom: 0,
      backgroundColor: 'rgba(0,0,0,0.6)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      zIndex: 1000,
      backdropFilter: 'blur(4px)',
    }}>
      <div style={{
        background: '#ffffff',
        borderRadius: '16px',
        width: '560px',
        maxWidth: '90vw',
        maxHeight: '90vh',
        overflowY: 'auto',
        boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04)',
      }}>
        {/* Header */}
        <div style={{
          padding: '20px 24px',
          borderBottom: '1px solid #e2e8f0',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <div style={{
              width: '36px',
              height: '36px',
              borderRadius: '10px',
              background: '#eff6ff',
              color: '#2563eb',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}>
              <Sparkles size={20} />
            </div>
            <div>
              <h2 style={{ margin: 0, fontSize: '18px', fontWeight: 600, color: '#0f172a' }}>AI Provider Settings</h2>
              <p style={{ margin: 0, fontSize: '12px', color: '#64748b' }}>Configure hosted LLM semantic mapping & safeguards</p>
            </div>
          </div>
          <button
            onClick={onClose}
            style={{
              background: 'transparent',
              border: 'none',
              cursor: 'pointer',
              color: '#94a3b8',
              padding: '4px',
              borderRadius: '6px',
            }}
          >
            <X size={20} />
          </button>
        </div>

        {/* Content */}
        <form onSubmit={handleSave} style={{ padding: '24px' }}>
          {/* AI Toggle */}
          <div style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '14px 16px',
            background: isEnabled ? '#f0fdf4' : '#f8fafc',
            border: `1px solid ${isEnabled ? '#bbf7d0' : '#e2e8f0'}`,
            borderRadius: '12px',
            marginBottom: '20px',
          }}>
            <div>
              <div style={{ fontWeight: 600, fontSize: '14px', color: isEnabled ? '#166534' : '#334155' }}>
                {isEnabled ? 'AI Semantic Mapping Enabled' : 'AI Mapping Disabled (Deterministic Only)'}
              </div>
              <div style={{ fontSize: '12px', color: '#64748b' }}>
                When disabled, JobPilot runs 100% locally with Workday rules and exact dictionaries.
              </div>
            </div>
            <label style={{ position: 'relative', display: 'inline-block', width: '48px', height: '26px' }}>
              <input
                type="checkbox"
                checked={isEnabled}
                onChange={(e) => setIsEnabled(e.target.checked)}
                style={{ opacity: 0, width: 0, height: 0 }}
              />
              <span style={{
                position: 'absolute',
                cursor: 'pointer',
                top: 0, left: 0, right: 0, bottom: 0,
                backgroundColor: isEnabled ? '#2563eb' : '#cbd5e1',
                transition: '0.2s',
                borderRadius: '34px',
              }}>
                <span style={{
                  position: 'absolute',
                  content: '""',
                  height: '20px',
                  width: '20px',
                  left: isEnabled ? '25px' : '3px',
                  bottom: '3px',
                  backgroundColor: 'white',
                  transition: '0.2s',
                  borderRadius: '50%',
                }} />
              </span>
            </label>
          </div>

          {/* Provider Selection */}
          <div style={{ marginBottom: '16px' }}>
            <label style={{ display: 'block', fontSize: '13px', fontWeight: 600, color: '#334155', marginBottom: '6px' }}>
              Hosted AI Provider
            </label>
            <select
              value={provider}
              onChange={(e) => setProvider(e.target.value)}
              style={{
                width: '100%',
                padding: '9px 12px',
                borderRadius: '8px',
                border: '1px solid #cbd5e1',
                fontSize: '14px',
                color: '#0f172a',
                outline: 'none',
              }}
            >
              <option value="Google Gemini">Google Gemini (Gemini 1.5 Flash)</option>
              <option value="OpenAI">OpenAI (GPT-4o mini)</option>
              <option value="Groq">Groq (Llama 3)</option>
              <option value="OpenRouter">OpenRouter</option>
            </select>
          </div>

          {/* Model Name */}
          <div style={{ marginBottom: '16px' }}>
            <label style={{ display: 'block', fontSize: '13px', fontWeight: 600, color: '#334155', marginBottom: '6px' }}>
              Model Name
            </label>
            <input
              type="text"
              value={model}
              onChange={(e) => setModel(e.target.value)}
              placeholder="e.g. gemini-1.5-flash or gpt-4o-mini"
              style={{
                width: '100%',
                padding: '9px 12px',
                borderRadius: '8px',
                border: '1px solid #cbd5e1',
                fontSize: '14px',
                color: '#0f172a',
                outline: 'none',
              }}
            />
          </div>

          {/* API Key */}
          <div style={{ marginBottom: '16px' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '6px' }}>
              <label style={{ fontSize: '13px', fontWeight: 600, color: '#334155' }}>
                API Key
              </label>
              {settings?.hasApiKey && (
                <span style={{ fontSize: '12px', color: '#16a34a', display: 'flex', alignItems: 'center', gap: '4px' }}>
                  <CheckCircle size={13} /> Stored securely (DPAPI encrypted)
                </span>
              )}
            </div>
            <div style={{ position: 'relative' }}>
              <input
                type="password"
                value={apiKey}
                onChange={(e) => setApiKey(e.target.value)}
                placeholder={settings?.hasApiKey ? '•••••••••••••••••••• (leave blank to keep existing)' : 'Enter API Key'}
                style={{
                  width: '100%',
                  padding: '9px 12px 9px 36px',
                  borderRadius: '8px',
                  border: '1px solid #cbd5e1',
                  fontSize: '14px',
                  color: '#0f172a',
                  outline: 'none',
                }}
              />
              <Key size={16} style={{ position: 'absolute', left: '12px', top: '12px', color: '#94a3b8' }} />
            </div>
          </div>

          {/* Connection Test Results */}
          {testResult && (
            <div style={{
              padding: '12px 14px',
              borderRadius: '8px',
              fontSize: '13px',
              marginBottom: '16px',
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
              background: testResult.success ? '#f0fdf4' : '#fef2f2',
              color: testResult.success ? '#15803d' : '#b91c1c',
              border: `1px solid ${testResult.success ? '#bbf7d0' : '#fecaca'}`,
            }}>
              {testResult.success ? <CheckCircle size={16} /> : <AlertTriangle size={16} />}
              <span>{testResult.message} {testResult.latency ? `(${testResult.latency}ms)` : ''}</span>
            </div>
          )}

          {/* Privacy & Safety Callout */}
          <div style={{
            padding: '12px 14px',
            background: '#f8fafc',
            border: '1px solid #e2e8f0',
            borderRadius: '10px',
            fontSize: '12px',
            color: '#475569',
            marginBottom: '20px',
            display: 'flex',
            gap: '8px',
          }}>
            <Shield size={18} style={{ color: '#3b82f6', flexShrink: 0, marginTop: '2px' }} />
            <div>
              <strong>Strict Privacy & Grounding Safeguards:</strong>
              <ul style={{ margin: '4px 0 0 0', paddingLeft: '16px' }}>
                <li>No sensitive demographic, disability, veteran, or attestation questions are ever sent to LLMs.</li>
                <li>All suggested values must be mathematically grounded in verified profile data.</li>
                <li>Every LLM suggestion requires your explicit approval before fill.</li>
              </ul>
            </div>
          </div>

          {/* Action Buttons */}
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <button
              type="button"
              disabled={testMutation.isPending}
              onClick={() => testMutation.mutate()}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '6px',
                padding: '9px 16px',
                borderRadius: '8px',
                border: '1px solid #cbd5e1',
                background: '#ffffff',
                color: '#334155',
                fontSize: '13px',
                fontWeight: 500,
                cursor: 'pointer',
              }}
            >
              <RefreshCw size={14} className={testMutation.isPending ? 'spin' : ''} />
              {testMutation.isPending ? 'Testing...' : 'Test Connection'}
            </button>

            <div style={{ display: 'flex', gap: '8px' }}>
              <button
                type="button"
                onClick={onClose}
                style={{
                  padding: '9px 16px',
                  borderRadius: '8px',
                  border: '1px solid #cbd5e1',
                  background: '#ffffff',
                  color: '#475569',
                  fontSize: '13px',
                  fontWeight: 500,
                  cursor: 'pointer',
                }}
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={updateMutation.isPending}
                style={{
                  padding: '9px 20px',
                  borderRadius: '8px',
                  border: 'none',
                  background: '#2563eb',
                  color: '#ffffff',
                  fontSize: '13px',
                  fontWeight: 600,
                  cursor: 'pointer',
                }}
              >
                {updateMutation.isPending ? 'Saving...' : 'Save Settings'}
              </button>
            </div>
          </div>
        </form>
      </div>
    </div>
  );
};
