import React from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiClient } from '../../api/client';
import type { RunStatusDto } from '../../api/types';
import { Loader2, AlertTriangle, CheckCircle, Clock, XCircle } from 'lucide-react';

/** Polls run state for every application (fast while anything is queued or running). */
export function useRunStatuses() {
  return useQuery<RunStatusDto[]>({
    queryKey: ['run-status'],
    queryFn: apiClient.getRunStatuses,
    refetchInterval: (query) => {
      const data = query.state.data as RunStatusDto[] | undefined;
      const busy = data?.some((r) => r.runState === 'Queued' || r.runState === 'Running');
      return busy ? 2000 : 8000;
    },
  });
}

const STYLES: Record<string, { label: string; bg: string; color: string; border: string; icon: React.ReactNode }> = {
  Queued: { label: 'Queued', bg: '#f8fafc', color: '#475569', border: '#cbd5e1', icon: <Clock size={12} /> },
  Running: { label: 'Running', bg: '#eff6ff', color: '#1d4ed8', border: '#bfdbfe', icon: <Loader2 size={12} className="spin" /> },
  NeedsUser: { label: 'Needs you', bg: '#fffbeb', color: '#b45309', border: '#fde68a', icon: <AlertTriangle size={12} /> },
  ReadyForReview: { label: 'Ready for review', bg: '#f0fdf4', color: '#15803d', border: '#bbf7d0', icon: <CheckCircle size={12} /> },
  Failed: { label: 'Stopped', bg: '#fef2f2', color: '#b91c1c', border: '#fecaca', icon: <XCircle size={12} /> },
};

export const RunBadge: React.FC<{ status?: RunStatusDto | null }> = ({ status }) => {
  const s = status?.runState ? STYLES[status.runState] : null;
  if (!s) return null;
  return (
    <span
      title={status?.runMessage ?? ''}
      style={{
        display: 'inline-flex', alignItems: 'center', gap: '4px', fontSize: '11px', fontWeight: 600,
        padding: '2px 8px', borderRadius: '999px', background: s.bg, color: s.color, border: `1px solid ${s.border}`,
        whiteSpace: 'nowrap',
      }}
    >
      {s.icon} {s.label}
    </span>
  );
};

export function isRunBusy(status?: RunStatusDto | null) {
  return status?.runState === 'Queued' || status?.runState === 'Running';
}
