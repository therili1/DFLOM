import { create } from "zustand";
import { JavaManager, type JavaInstallation } from "../services/java/JavaManager";

type JavaState = {
  javaInstallations: JavaInstallation[];
  selectedJava: JavaInstallation | null;
  loading: boolean;
  error: string | null;
  scanJava: () => Promise<void>;
  addJava: (path: string) => Promise<void>;
  removeJava: (path: string) => Promise<void>;
  setDefaultJava: (java: JavaInstallation) => void;
  downloadJava: (major: number) => Promise<import("../services/java/JavaManager").JavaInstallation>;
};

export const useJavaStore = create<JavaState>((set) => ({
  javaInstallations: [], selectedJava: null, loading: false, error: null,
  scanJava: async () => { set({ loading: true, error: null }); try { const javaInstallations = await JavaManager.scan(); set({ javaInstallations, selectedJava: javaInstallations[0] ?? null, loading: false }); } catch (error) { set({ loading: false, error: String(error) }); } },
  addJava: async (path) => { set({ loading: true, error: null }); try { const java = await JavaManager.save(path); set((state) => ({ javaInstallations: [...state.javaInstallations.filter((item) => item.path !== java.path), java], selectedJava: state.selectedJava ?? java, loading: false })); } catch (error) { set({ loading: false, error: String(error) }); throw error; } },
  removeJava: async (path) => { await JavaManager.remove(path); set((state) => { const javaInstallations = state.javaInstallations.filter((item) => item.path !== path); return { javaInstallations, selectedJava: state.selectedJava?.path === path ? javaInstallations[0] ?? null : state.selectedJava }; }); },
  setDefaultJava: (selectedJava) => set({ selectedJava }),
  downloadJava: async (major) => { set({ loading: true, error: null }); try { const java = await JavaManager.download(major); set((state) => ({ javaInstallations: [...state.javaInstallations.filter((item) => item.path !== java.path), java], selectedJava: java, loading: false })); return java; } catch (error) { set({ loading: false, error: String(error) }); throw error; } },
}));