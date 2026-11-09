import React from 'react';
import type { CompletenessReport } from '../../api/types';
import { ShieldCheck, AlertTriangle } from 'lucide-react';

interface Props {
  report: CompletenessReport;
  onRefresh?: () => void;
}

export const CompletenessCard: React.FC<Props> = ({ report }) => {
  return (
    <div className="card">
      <div className="card-title">
        <span style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
          <ShieldCheck color="#2563eb" size={20} />
          Profile Completeness & Verification
        </span>
        <span style={{ fontSize: '1.25rem', fontWeight: 700, color: report.completenessPercentage > 80 ? '#16a34a' : '#d97706' }}>
          {report.completenessPercentage}%
        </span>
      </div>

      <div className="progress-bar">
        <div
          className="progress-fill"
          style={{
            width: `${report.completenessPercentage}%`,
            backgroundColor: report.completenessPercentage > 80 ? '#16a34a' : '#2563eb'
          }}
        />
      </div>

      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.85rem', color: '#64748b', marginTop: '0.25rem' }}>
        <span>{report.verifiedCount} of {report.totalChecked} core profile facts verified</span>
        {report.missingCount > 0 && (
          <span style={{ color: '#d97706', display: 'flex', alignItems: 'center', gap: '0.25rem' }}>
            <AlertTriangle size={14} />
            {report.missingCount} items need attention
          </span>
        )}
      </div>

      {report.missingOrUnconfirmed.length > 0 && (
        <div style={{ marginTop: '1rem', paddingTop: '0.75rem', borderTop: '1px solid #f1f5f9' }}>
          <div style={{ fontSize: '0.85rem', fontWeight: 600, color: '#475569', marginBottom: '0.5rem' }}>
            Items Workday applications commonly require:
          </div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.5rem' }}>
            {report.missingOrUnconfirmed.map(issue => (
              <span
                key={issue.fieldKey}
                style={{
                  fontSize: '0.75rem',
                  padding: '0.25rem 0.5rem',
                  borderRadius: '4px',
                  backgroundColor: '#f8fafc',
                  border: '1px solid #e2e8f0',
                  color: '#334155'
                }}
              >
                {issue.description}
              </span>
            ))}
          </div>
        </div>
      )}
    </div>
  );
};

