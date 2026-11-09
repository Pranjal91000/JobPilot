import React from 'react';
import type { VerificationStatus } from '../api/types';
import { CheckCircle2, AlertCircle, Sparkles, HelpCircle } from 'lucide-react';

interface Props {
  status?: VerificationStatus | null;
  label?: string;
}

export const VerificationBadge: React.FC<Props> = ({ status, label }) => {
  if (status === 'Verified') {
    return (
      <span className="badge badge-verified" title="Verified Fact: Explicitly confirmed by user">
        <CheckCircle2 size={12} />
        {label ?? 'Verified'}
      </span>
    );
  }

  if (status === 'ExtractedUnconfirmed') {
    return (
      <span className="badge badge-unconfirmed" title="Extracted from source document; needs confirmation">
        <AlertCircle size={12} />
        {label ?? 'Unconfirmed'}
      </span>
    );
  }

  if (status === 'AiSuggested') {
    return (
      <span className="badge badge-ai" title="AI Suggested; requires user verification">
        <Sparkles size={12} />
        {label ?? 'AI Suggestion'}
      </span>
    );
  }

  return (
    <span className="badge badge-missing" title="Missing field">
      <HelpCircle size={12} />
      {label ?? 'Missing'}
    </span>
  );
};

