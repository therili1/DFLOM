import { GameArguments } from "./GameArguments";
import { JvmArguments } from "./JvmArguments";
import { MinecraftProcess } from "./MinecraftProcess";
import type { LaunchProfile } from "./LaunchProfile";

export type LaunchCommand = {
  command: string;
  javaPath: string;
  ram: string;
  version: string;
  arguments: string[];
  workingDirectory: string;
  libraries: string[];
};

export const LauncherService = {
  buildCommand(profile: LaunchProfile): LaunchCommand {
    const gameArguments = GameArguments.build({
      username: profile.username ?? "OfflinePlayer",
      uuid: profile.uuid ?? "00000000-0000-0000-0000-000000000000",
      accessToken: profile.accessToken ?? "Offline",
      version: profile.minecraftVersion,
      gameDirectory: profile.gameDirectory,
      assetsDirectory: `${profile.gameDirectory}/assets`,
      assetIndex: profile.minecraftVersion,
      userType: profile.userType ?? "legacy",
      versionType: "release",
    });
    const jvmArguments = JvmArguments.build({
      minMemoryMb: profile.ramMin,
      maxMemoryMb: profile.ramMax,
      nativeDirectory: `${profile.gameDirectory}/natives`,
      extraArguments: profile.jvmArguments,
    });
    const argumentsList = [...jvmArguments, "net.minecraft.client.main.Main", ...gameArguments, ...profile.gameArguments];
    return {
      command: MinecraftProcess.buildCommand({
        javaPath: profile.javaPath,
        jvmArguments: jvmArguments,
        mainClass: "net.minecraft.client.main.Main",
        gameArguments: [...gameArguments, ...profile.gameArguments],
        workingDirectory: profile.gameDirectory,
      }),
      javaPath: profile.javaPath,
      ram: `${profile.ramMin}M → ${profile.ramMax}M`,
      version: profile.minecraftVersion,
      arguments: argumentsList,
      workingDirectory: profile.gameDirectory,
      libraries: [],
    };
  },
};