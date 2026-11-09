import React, { useState, useEffect } from 'react';
import type { ResumeDto } from '../../api/types';
import { apiClient } from '../../api/client';
import { FileText, Upload, Trash2, Download, Check, Star } from 'lucide-react';

export const ResumeManager: React.FC = () => {
  const [resumes, setResumes] = useState<ResumeDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Form states
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [label, setLabel] = useState('');
  const [resumeType, setResumeType] = useState('General');
  const [isDefault, setIsDefault] = useState(false);

  const loadResumes = async () => {
    try {
      setLoading(true);
      const list = await apiClient.listResumes();
      setResumes(list);
    } catch (err: any) {
      setError(err.message || 'Failed to load resumes');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadResumes();
  }, []);

  const handleUpload = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedFile) return;

    setUploading(true);
    setError(null);

    try {
      const formData = new FormData();
      formData.append('file', selectedFile);
      formData.append('resumeType', resumeType);
      if (label) formData.append('label', label);
      formData.append('isDefault', String(isDefault));

      await apiClient.uploadResume(formData);
      setSelectedFile(null);
      setLabel('');
      setIsDefault(false);
      await loadResumes();
    } catch (err: any) {
      setError(err.message || 'Failed to upload resume');
    } finally {
      setUploading(false);
    }
  };

  const handleDelete = async (id: string) => {
    if (!window.confirm('Are you sure you want to delete this resume?')) return;
    try {
      await apiClient.deleteResume(id);
      await loadResumes();
    } catch (err: any) {
      alert('Delete failed: ' + err.message);
    }
  };

  const handleSetDefault = async (resume: ResumeDto) => {
    try {
      await apiClient.updateResume(resume.resumeId, {
        resumeType: resume.resumeType,
        label: resume.label ?? undefined,
        isDefault: true,
        isActive: resume.isActive,
      });
      await loadResumes();
    } catch (err: any) {
      alert('Failed to set default: ' + err.message);
    }
  };

  const formatBytes = (bytes: number) => {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
  };

  return (
    <div>
      <div className="card">
        <div className="card-title">
          <span>Upload New Resume Version</span>
        </div>

        {error && (
          <div style={{ backgroundColor: '#fee2e2', color: '#dc2626', padding: '0.75rem', borderRadius: '4px', marginBottom: '1rem', fontSize: '0.875rem' }}>
            {error}
          </div>
        )}

        <form onSubmit={handleUpload}>
          <div className="grid-2">
            <div className="form-group">
              <label className="form-label">Resume Document (.pdf, .docx, max 5 MB) *</label>
              <input
                type="file"
                className="form-control"
                accept=".pdf,.docx,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                onChange={e => setSelectedFile(e.target.files?.[0] ?? null)}
                required
              />
            </div>

            <div className="form-group">
              <label className="form-label">Resume Target Track</label>
              <select
                className="form-control"
                value={resumeType}
                onChange={e => setResumeType(e.target.value)}
              >
                <option value="General">General</option>
                <option value="Backend">Backend</option>
                <option value="Frontend">Frontend</option>
                <option value="FullStack">Full-Stack</option>
                <option value="DevOps">DevOps</option>
                <option value="Management">Management</option>
              </select>
            </div>
          </div>

          <div className="grid-2">
            <div className="form-group">
              <label className="form-label">Version Label (Optional)</label>
              <input
                type="text"
                className="form-control"
                placeholder="e.g. Senior .NET Specialist (Workday optimized)"
                value={label}
                onChange={e => setLabel(e.target.value)}
              />
            </div>

            <div className="form-group" style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', paddingTop: '1.75rem' }}>
              <input
                type="checkbox"
                id="makeDefault"
                checked={isDefault}
                onChange={e => setIsDefault(e.target.checked)}
              />
              <label htmlFor="makeDefault" style={{ fontSize: '0.875rem', cursor: 'pointer', fontWeight: 500 }}>
                Set as Default Resume for new applications
              </label>
            </div>
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '0.5rem' }}>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={uploading || !selectedFile}
            >
              <Upload size={16} />
              {uploading ? 'Encrypting & Uploading...' : 'Upload & Encrypt Resume'}
            </button>
          </div>
        </form>
      </div>

      <div className="card">
        <div className="card-title">
          <span>Stored Resumes ({resumes.length})</span>
          <span style={{ fontSize: '0.85rem', color: '#64748b' }}>
            Encrypted with AES-GCM / DPAPI at rest
          </span>
        </div>

        {loading ? (
          <div style={{ padding: '2rem', textAlign: 'center', color: '#64748b' }}>Loading resumes...</div>
        ) : resumes.length === 0 ? (
          <div style={{ padding: '2rem', textAlign: 'center', color: '#64748b' }}>
            No resumes uploaded yet. Upload your PDF or DOCX resume above.
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
            {resumes.map(r => (
              <div
                key={r.resumeId}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  padding: '1rem',
                  border: r.isDefault ? '2px solid #2563eb' : '1px solid #e2e8f0',
                  borderRadius: '6px',
                  backgroundColor: r.isDefault ? '#f8faff' : 'white',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
                  <div style={{ padding: '0.5rem', borderRadius: '6px', backgroundColor: '#eff6ff', color: '#2563eb' }}>
                    <FileText size={24} />
                  </div>
                  <div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                      <span style={{ fontWeight: 600 }}>{r.fileName}</span>
                      {r.isDefault && (
                        <span className="badge badge-verified" style={{ gap: '0.2rem' }}>
                          <Check size={12} /> Default
                        </span>
                      )}
                      <span className="badge" style={{ backgroundColor: '#f1f5f9', color: '#475569' }}>
                        v{r.version} • {r.resumeType}
                      </span>
                    </div>
                    <div style={{ fontSize: '0.8rem', color: '#64748b', marginTop: '0.2rem' }}>
                      {r.label && <span>{r.label} • </span>}
                      <span>{formatBytes(r.sizeBytes)} • </span>
                      <span title={`SHA-256: ${r.sha256}`}>Hash: {r.sha256.substring(0, 12)}... • </span>
                      <span>Uploaded {new Date(r.createdAt).toLocaleDateString()}</span>
                    </div>
                  </div>
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                  {!r.isDefault && (
                    <button
                      className="btn btn-outline btn-sm"
                      onClick={() => handleSetDefault(r)}
                      title="Set as Default"
                    >
                      <Star size={14} /> Make Default
                    </button>
                  )}
                  <a
                    href={apiClient.getResumeDownloadUrl(r.resumeId)}
                    className="btn btn-outline btn-sm"
                    target="_blank"
                    rel="noreferrer"
                    title="Download Decrypted Copy"
                  >
                    <Download size={14} /> Download
                  </a>
                  <button
                    className="btn btn-outline btn-sm"
                    style={{ color: '#dc2626' }}
                    onClick={() => handleDelete(r.resumeId)}
                    title="Delete Resume"
                  >
                    <Trash2 size={14} />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};

