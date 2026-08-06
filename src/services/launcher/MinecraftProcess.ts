export type MinecraftProcessSpec = {
  javaPath: string;
  jvmArguments: string[];
  mainClass: string;
  gameArguments: string[];
  workingDirectory: string;
};

export const MinecraftProcess = {
  buildCommand(spec: MinecraftProcessSpec): string {
    const quote = (value: string) => `"${value.replaceAll('"', '\\"')}"`;
    return [
      quote(spec.javaPath),
      ...spec.jvmArguments.map(quote),
      spec.mainClass,
      ...spec.gameArguments.map(quote),
    ].join(" ");
  },
};