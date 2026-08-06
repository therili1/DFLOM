export type LaunchProfile = {
  instanceName: string;
  minecraftVersion: string;
  gameDirectory: string;
  javaPath: string;
  ramMin: number;
  ramMax: number;
  resolutionWidth: number;
  resolutionHeight: number;
  jvmArguments: string[];
  gameArguments: string[];
  username?: string;
  uuid?: string;
  userType?: string;
  accessToken?: string;
};