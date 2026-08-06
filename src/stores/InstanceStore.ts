import { create } from "zustand";
import { InstanceManager, type CreateInstanceInput, type Instance } from "../services/InstanceManager";

type InstanceState = {
  instances: Instance[];
  selectedInstance: Instance | null;
  loading: boolean;
  error: string | null;
  loadInstances: () => Promise<void>;
  createInstance: (input: CreateInstanceInput) => Promise<void>;
  deleteInstance: (name: string) => Promise<void>;
  renameInstance: (oldName: string, newName: string) => Promise<void>;
  changeInstanceFolder: (name: string, folder: string) => Promise<void>;
  selectInstance: (instance: Instance | null) => void;
  refreshInstances: () => Promise<void>;
};

export const useInstanceStore = create<InstanceState>((set, get) => ({
  instances: [], selectedInstance: null, loading: false, error: null,
  loadInstances: async () => { set({ loading: true, error: null }); try { const instances = await InstanceManager.list(); set({ instances, loading: false, selectedInstance: get().selectedInstance ?? instances[0] ?? null }); } catch (error) { set({ loading: false, error: String(error) }); } },
  createInstance: async (input) => { set({ loading: true, error: null }); try { const instance = await InstanceManager.create(input); set((state) => ({ instances: [...state.instances, instance], selectedInstance: instance, loading: false })); } catch (error) { set({ loading: false, error: String(error) }); throw error; } },
  deleteInstance: async (name) => { await InstanceManager.delete(name); set((state) => { const instances = state.instances.filter((item) => item.name !== name); return { instances, selectedInstance: state.selectedInstance?.name === name ? instances[0] ?? null : state.selectedInstance }; }); },
  renameInstance: async (oldName, newName) => { const instance = await InstanceManager.rename(oldName, newName); set((state) => ({ instances: state.instances.map((item) => item.name === oldName ? instance : item), selectedInstance: state.selectedInstance?.name === oldName ? instance : state.selectedInstance })); },
  changeInstanceFolder: async (name, folder) => { const instance = await InstanceManager.changeFolder(name, folder); set((state) => ({ instances: state.instances.map((item) => item.name === name ? instance : item), selectedInstance: state.selectedInstance?.name === name ? instance : state.selectedInstance })); },
  selectInstance: (selectedInstance) => set({ selectedInstance }),
  refreshInstances: async () => {
    const instances = await InstanceManager.list();
    set((state) => ({ instances, selectedInstance: state.selectedInstance ?? instances[0] ?? null }));
  },
}));