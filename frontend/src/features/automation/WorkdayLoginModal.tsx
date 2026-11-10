import React, { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '../../api/client';
import type { WorkdayLoginDto } from '../../api/types';
import { KeyRound, X, CheckCircle, AlertTriangle } from 'lucide-react';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

/**
 * The login JobPilot uses to sign in to, or create, your candidate account on each company's
 * Workday site. Most people use one email and password everywhere.
 */
export const WorkdayLoginModal: React.FC<Props> = ({ isOpen, onClose }) => {
  const queryClient = useQueryClient();
  const { data } = useQuery<WorkdayLoginDto>({
    queryKey: ['workday-login'],
    queryFn: apiClient.getWorkdayLogin,
    enabled: isOpen,
  });

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (data?.email) setEmail(data.email);
  }, [data]);

  const save = useMutation({
    mutationFn: () => apiClient.saveWorkdayLogin(email.trim(), password),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workday-login'] });
      setPassword('');
      setError(null);
      onClose();
    },
    onError: (e: any) => setError(e.message || 'Failed to save'),
  });

  if (!isOpen) return null;

  const input: React.CSSProperties = {
    width: '100%', padding: '9px 12px', borderRadius: '8px', border: '1px solid #cbd5e1', fontSize: '14px',
  };

  return (
    <div style={{ position: 'fixed', inset: 0, background: 'rgba(15,23,42,0.6)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000, padding: '16px' }}>
      <form
        onSubmit={(e) => { e.preventDefault(); save.mutate(); }}
        style={{ background: '#fff', borderRadius: '16px', width: '480px', maxWidth: '100%', maxHeight: '90vh', overflowY: 'auto', padding: '24px' }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
          <h2 style={{ fontSize: '18px', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px' }}>
            <KeyRound size={18} /> Workday login
          </h2>
          <button type="button" onClick={onClose} style={{ background: 'none', border: 'none', color: '#94a3b8' }}><X size={20} /></button>
        </div>

        <p style={{ fontSize: '13px', color: '#475569', marginBottom: '16px' }}>
          Each company's Workday site needs its own candidate account. JobPilot signs in with this email and password,
          and creates the account if it doesn't exist yet. If Workday asks you to verify your email, the run pauses until you do.
        </p>

        <label style={{ display: 'block', fontSize: '13px', fontWeight: 600, marginBottom: '6px' }}>Email</label>
        <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} style={{ ...input, marginBottom: '12px' }} />

        <label style={{ display: 'block', fontSize: '13px', fontWeight: 600, marginBottom: '6px' }}>Password</label>
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          placeholder={data?.hasPassword ? 'Saved (leave blank to keep it)' : 'Password for Workday accounts'}
          required={!data?.hasPassword}
          style={input}
        />
        <div style={{ fontSize: '12px', color: '#64748b', marginTop: '6px' }}>
          Stored encrypted on this computer. Workday usually requires 8+ characters with upper and lower case, a number and a symbol.
        </div>

        {data && data.sitesWithAccounts.length > 0 && (
          <div style={{ fontSize: '12px', color: '#15803d', marginTop: '12px', display: 'flex', gap: '6px', alignItems: 'center' }}>
            <CheckCircle size={14} /> Accounts exist on: {data.sitesWithAccounts.join(', ')}
          </div>
        )}

        {error && (
          <div style={{ fontSize: '13px', color: '#b91c1c', marginTop: '12px', display: 'flex', gap: '6px', alignItems: 'center' }}>
            <AlertTriangle size={14} /> {error}
          </div>
        )}

        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px', marginTop: '20px' }}>
          <button type="button" onClick={onClose} style={{ padding: '9px 16px', borderRadius: '8px', border: '1px solid #cbd5e1', background: '#fff' }}>Cancel</button>
          <button type="submit" disabled={save.isPending} style={{ padding: '9px 18px', borderRadius: '8px', border: 'none', background: '#2563eb', color: '#fff', fontWeight: 600 }}>
            {save.isPending ? 'Saving...' : 'Save'}
          </button>
        </div>
      </form>
    </div>
  );
};
