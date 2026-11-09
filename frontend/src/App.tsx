import { useState, useEffect } from 'react';
import type { CompletenessReport, ProfileDto } from './api/types';
import { apiClient } from './api/client';
import { AppHeader } from './components/AppHeader';
import { ProfileView } from './features/profile/ProfileView';
import { CompletenessCard } from './features/profile/CompletenessCard';
import { ResumeManager } from './features/resumes/ResumeManager';
import { ApplicationsView } from './features/applications/ApplicationsView';
import { FieldMappingReview } from './features/applications/FieldMappingReview';
import { AiSettingsModal } from './features/ai/AiSettingsModal';

export function App() {
  const [currentTab, setCurrentTab] = useState<'applications' | 'profile' | 'resumes'>('applications');
  const [selectedApplicationId, setSelectedApplicationId] = useState<string | null>(null);
  const [isAiSettingsOpen, setIsAiSettingsOpen] = useState(false);

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
        apiClient.getProfile().catch(() => null),
        apiClient.getCompleteness().catch(() => null),
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

  const handleTabChange = (tab: 'applications' | 'profile' | 'resumes') => {
    setCurrentTab(tab);
    if (tab !== 'applications') {
      setSelectedApplicationId(null);
    }
  };

  return (
    <div style={{ minHeight: '100vh', background: '#f8fafc' }}>
      <AppHeader
        currentTab={currentTab}
        onTabChange={handleTabChange}
        health={health}
        onOpenAiSettings={() => setIsAiSettingsOpen(true)}
      />

      <main style={{ paddingBottom: '40px' }}>
        {loading ? (
          <div style={{ textAlign: 'center', padding: '4rem', color: '#64748b' }}>
            Loading JobPilot workspace...
          </div>
        ) : error ? (
          <div style={{ maxWidth: '800px', margin: '40px auto', background: '#fff', borderRadius: '12px', padding: '24px', borderLeft: '4px solid #dc2626' }}>
            <h3 style={{ color: '#dc2626', margin: '0 0 8px 0' }}>Connection Notice</h3>
            <p style={{ color: '#64748b', margin: '0 0 16px 0' }}>{error}</p>
            <p style={{ fontSize: '13px', color: '#475569', margin: 0 }}>
              Ensure <code>JobPilot.Api</code> is running locally on <code>http://localhost:5080</code>.
            </p>
            <button
              onClick={loadData}
              style={{ marginTop: '16px', padding: '8px 16px', background: '#2563eb', color: '#fff', border: 'none', borderRadius: '6px', cursor: 'pointer' }}
            >
              Retry Connection
            </button>
          </div>
        ) : (
          <>
            {currentTab === 'applications' && (
              selectedApplicationId ? (
                <FieldMappingReview
                  applicationId={selectedApplicationId}
                  onBack={() => setSelectedApplicationId(null)}
                  onOpenAiSettings={() => setIsAiSettingsOpen(true)}
                />
              ) : (
                <ApplicationsView
                  onSelectApplication={(id) => setSelectedApplicationId(id)}
                  onOpenAiSettings={() => setIsAiSettingsOpen(true)}
                />
              )
            )}

            {currentTab === 'profile' && profile && (
              <div style={{ maxWidth: '1200px', margin: '0 auto', padding: '24px' }}>
                {completeness && <CompletenessCard report={completeness} onRefresh={loadData} />}
                <ProfileView profile={profile} onProfileUpdated={handleProfileUpdated} />
              </div>
            )}

            {currentTab === 'resumes' && (
              <div style={{ maxWidth: '1200px', margin: '0 auto', padding: '24px' }}>
                <ResumeManager />
              </div>
            )}
          </>
        )}
      </main>

      <AiSettingsModal
        isOpen={isAiSettingsOpen}
        onClose={() => setIsAiSettingsOpen(false)}
      />
    </div>
  );
}

export default App;
