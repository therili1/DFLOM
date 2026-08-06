export type IntegrityTarget = { sha1: string; size: number };
export const IntegrityChecker = { isValid: (size: number, expected: IntegrityTarget) => size === expected.size };