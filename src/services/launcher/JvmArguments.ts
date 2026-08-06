export type JvmArgumentOptions = {
  minMemoryMb: number;
  maxMemoryMb: number;
  nativeDirectory: string;
  extraArguments: string[];
};

export const JvmArguments = {
  build(options: JvmArgumentOptions): string[] {
    return [
      `-Xms${options.minMemoryMb}M`,
      `-Xmx${options.maxMemoryMb}M`,
      "-XX:+UnlockExperimentalVMOptions",
      "-XX:+UseG1GC",
      `-Djava.library.path=${options.nativeDirectory}`,
      ...options.extraArguments,
    ];
  },
};