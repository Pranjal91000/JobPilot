import { useState, useEffect } from 'react';
import type { CompletenessReport, ProfileDto } from './api/types';
import { apiClient } from './api/client';
import { AppHeader } from './components/AppHeader';
import { ProfileView } from './features/profile/ProfileView';
import { CompletenessCard } from './features/profile/CompletenessCard';
import { ResumeManager } from './features/resumes/ResumeManager';

export function App() {
  const [currentTab, setCurrentTab] = useState<'profile' | 'resumes'>('profile');
  const [profile, setProfile] = useState<ProfileDto | null>(null);
  const [completeness, setCompleteness] = useState<CompletenessReport | null>(null);
  const [health, setHealth] = useState<{ status: string; database: string } | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadData = async () => {
    try {
      setLoading(true);
      setError(null);
      const [h, p, c] = await Promise.all([
        apiClient.getHealth().catch(() => null),
        apiClient.getProfile(),
        apiClient.getCompleteness().catch(() => null)
      ]);

      setHealth(h);
      setProfile(p);
      setCompleteness(c);
    } catch (err: any) {
      setError(err.message || 'Failed to connect to JobPilot API.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, []);

  const handleProfileUpdated = async (updated: ProfileDto) => {
    setProfile(updated);
    try {
      const c = await apiClient.getCompleteness();
      setCompleteness(c);
    } catch {
      // ignore completeness reload error
    }
  };

  return (
    <div>
      <AppHeader
        currentTab={currentTab}
        onTabChange={setCurrentTab}
        health={health}
      />

      <main className="container">
        {loading ? (
          <div style={{ textAlign: 'center', padding: '3rem', color: '#64748b' }}>
            Loading JobPilot workspace...
          </div>
        ) : error ? (
          <div className="card" style={{ borderLeft: '4px solid #dc2626' }}>
            <h3 style={{ color: '#dc2626', marginBottom: '0.5rem' }}>Connection Notice</h3>
            <p style={{ color: '#64748b', marginBottom: '1rem' }}>{error}</p>
            <p style={{ fontSize: '0.85rem', color: '#475569' }}>
              Ensure <code>JobPilot.Api</code> is running locally on <code>http://localhost:5080</code>.
            </p>
            <button className="btn btn-outline btn-sm" style={{ marginTop: '1rem' }} onClick={loadData}>
              Retry Connection
            </button>
          </div>
        ) : (
          <>
            {currentTab === 'profile' && profile && (
              <>
                {completeness && <CompletenessCard report={completeness} onRefresh={loadData} />}
                <ProfileView profile={profile} onProfileUpdated={handleProfileUpdated} />
              </>
            )}

            {currentTab === 'resumes' && (
              <ResumeManager />
            )}
          </>
        )}
      </main>
    </div>
  );
}

export default App;
