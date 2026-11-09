import React from 'react';
import { Compass, User, FileText, CheckCircle2, AlertCircle } from 'lucide-react';

interface Props {
  currentTab: 'profile' | 'resumes';
  onTabChange: (tab: 'profile' | 'resumes') => void;
  health: { status: string; database: string } | null;
}

export const AppHeader: React.FC<Props> = ({ currentTab, onTabChange, health }) => {
  return (
    <header className="header">
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

        <nav className="nav-links">
          <button
            className={`nav-link ${currentTab === 'profile' ? 'active' : ''}`}
            onClick={() => onTabChange('profile')}
            style={{ border: 'none', background: 'none' }}
          >
            <User size={16} /> Central Profile
          </button>
          <button
            className={`nav-link ${currentTab === 'resumes' ? 'active' : ''}`}
            onClick={() => onTabChange('resumes')}
            style={{ border: 'none', background: 'none' }}
          >
            <FileText size={16} /> Resumes
          </button>
        </nav>
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', fontSize: '0.8rem' }}>
        {health ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.35rem', color: health.database === 'Connected' ? '#16a34a' : '#dc2626' }}>
            {health.database === 'Connected' ? <CheckCircle2 size={14} /> : <AlertCircle size={14} />}
            <span>PostgreSQL: {health.database}</span>
          </div>
        ) : (
          <span style={{ color: '#64748b' }}>Connecting to local API...</span>
        )}
      </div>
    </header>
  );
};

