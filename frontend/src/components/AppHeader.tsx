import React from 'react';
import { Compass, User, FileText, CheckCircle2, AlertCircle, Briefcase, Sparkles } from 'lucide-react';

interface Props {
  currentTab: 'applications' | 'profile' | 'resumes';
  onTabChange: (tab: 'applications' | 'profile' | 'resumes') => void;
  health: { status: string; database: string } | null;
  onOpenAiSettings: () => void;
}

export const AppHeader: React.FC<Props> = ({ currentTab, onTabChange, health, onOpenAiSettings }) => {
  return (
    <header className="header" style={{ padding: '0.75rem 1.5rem', borderBottom: '1px solid #e2e8f0', background: '#ffffff', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: '2rem' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
          <div style={{ backgroundColor: '#2563eb', color: 'white', padding: '0.4rem', borderRadius: '6px', display: 'flex' }}>
            <Compass size={22} />
          </div>
          <div>
            <span style={{ fontWeight: 800, fontSize: '1.2rem', letterSpacing: '-0.02em', color: '#0f172a' }}>
              JobPilot
            </span>
            <span style={{ fontSize: '0.75rem', marginLeft: '0.4rem', color: '#64748b', fontWeight: 600, border: '1px solid #cbd5e1', padding: '0.1rem 0.35rem', borderRadius: '4px' }}>
              MVP v1.1
            </span>
          </div>
        </div>

        <nav className="nav-links" style={{ display: 'flex', gap: '0.5rem' }}>
          <button
            className={`nav-link ${currentTab === 'applications' ? 'active' : ''}`}
            onClick={() => onTabChange('applications')}
            style={{ border: 'none', background: 'none', display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 12px', cursor: 'pointer', borderRadius: '6px' }}
          >
            <Briefcase size={16} /> Applications
          </button>
          <button
            className={`nav-link ${currentTab === 'profile' ? 'active' : ''}`}
            onClick={() => onTabChange('profile')}
            style={{ border: 'none', background: 'none', display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 12px', cursor: 'pointer', borderRadius: '6px' }}
          >
            <User size={16} /> Central Profile
          </button>
          <button
            className={`nav-link ${currentTab === 'resumes' ? 'active' : ''}`}
            onClick={() => onTabChange('resumes')}
            style={{ border: 'none', background: 'none', display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 12px', cursor: 'pointer', borderRadius: '6px' }}
          >
            <FileText size={16} /> Resumes
          </button>
        </nav>
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: '1.25rem', fontSize: '0.8rem' }}>
        <button
          onClick={onOpenAiSettings}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '6px',
            padding: '6px 12px',
            borderRadius: '20px',
            border: '1px solid #bfdbfe',
            background: '#eff6ff',
            color: '#1d4ed8',
            fontSize: '12px',
            fontWeight: 600,
            cursor: 'pointer',
          }}
        >
          <Sparkles size={14} /> AI Settings
        </button>

        {health ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.35rem', color: health.database === 'Connected' ? '#16a34a' : '#dc2626' }}>
            {health.database === 'Connected' ? <CheckCircle2 size={14} /> : <AlertCircle size={14} />}
            <span>DB: {health.database}</span>
          </div>
        ) : (
          <span style={{ color: '#64748b' }}>Connecting...</span>
        )}
      </div>
    </header>
  );
};
