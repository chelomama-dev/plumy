import { ConfidenceBandAccuracy } from './ai-accuracy';

/** Mirrors Plumy.Application.Firms.AutoApproval.AutoApprovalResponse. */
export interface AutoApproval {
  enabled: boolean;
  /** From 0.8 to 1: how sure the reader must be of every booked field. */
  minConfidence: number;
}

export const autoApprovalUrl = '/api/firm/auto-approval';

/**
 * The lines on offer: the edges of the accuracy report's confidence bands, so the report says exactly how often a
 * field above each one had to be corrected. The API takes anything from 0.8 to 1.
 */
export const minConfidenceOptions = [0.8, 0.9, 0.95] as const;

/**
 * The fields read with at least that confidence, and how many a person corrected, over the bands of the report.
 * Null when no field was read that sure: nothing to judge the line by.
 */
export function correctedAbove(
  bands: readonly ConfidenceBandAccuracy[],
  minConfidence: number,
): { read: number; corrected: number } | null {
  const above = bands.filter((band) => band.from >= minConfidence);
  const read = above.reduce((sum, band) => sum + band.read, 0);
  const corrected = above.reduce((sum, band) => sum + band.corrected, 0);
  return read > 0 ? { read, corrected } : null;
}
