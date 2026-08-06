export type MemoryInfo = {
  systemMemoryMb: number;
  recommendedMemoryMb: number;
  maximumMemoryMb: number;
};

export const MemoryManager = {
  getInfo(): MemoryInfo {
    const deviceMemory = (navigator as Navigator & { deviceMemory?: number }).deviceMemory ?? 4;
    const systemMemoryMb = Math.max(1024, Math.round(deviceMemory * 1024));
    const maximumMemoryMb = Math.floor(systemMemoryMb * 0.8);
    return {
      systemMemoryMb,
      recommendedMemoryMb: Math.min(4096, Math.max(1024, Math.floor(maximumMemoryMb * 0.5))),
      maximumMemoryMb,
    };
  },
  clamp(value: number, systemMemoryMb: number) {
    return Math.min(Math.floor(systemMemoryMb * 0.8), Math.max(512, Math.round(value)));
  },
};