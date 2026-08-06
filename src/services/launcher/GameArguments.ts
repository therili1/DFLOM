export type GameArgumentOptions = {
  username: string;
  uuid: string;
  accessToken: string;
  version: string;
  gameDirectory: string;
  assetsDirectory: string;
  assetIndex: string;
  userType: string;
  versionType: string;
};

export const GameArguments = {
  build(options: GameArgumentOptions): string[] {
    return [
      "--username", options.username,
      "--uuid", options.uuid,
      "--accessToken", options.accessToken,
      "--version", options.version,
      "--gameDir", options.gameDirectory,
      "--assetsDir", options.assetsDirectory,
      "--assetIndex", options.assetIndex,
      "--userType", options.userType,
      "--versionType", options.versionType,
    ];
  },
};