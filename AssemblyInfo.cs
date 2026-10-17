using System.Reflection;
using SecRandom;

// 只有启用 EnableGitInfoGenerator 的头程序集编译这个文件（见 Global.props）：
// 版本特性由构建期写入的 GitInfo 提供，因此这类工程必须把 GenerateAssemblyInfo 关掉，
// 否则会和 SDK 生成的同名特性冲突（CS0579）。
//
// 版本号机制与上游 SecRandom-C 完全一致：构建期由 SecRandom.SimpleGitInfoGenerator 跑 git，
// 生成 internal 的 SecRandom.GitInfo；头程序集把 tag/commit 写进 AssemblyInformationalVersion，
// 类库通过 GlobalConstants.SetVersionAssembly 读取头程序集的元数据（不会出现第二个公开 GitInfo 类型）。

[assembly: AssemblyVersion(GitInfo.AssemblyVersion)]
[assembly: AssemblyInformationalVersion($"{GitInfo.Tag}+{GitInfo.CommitHash}")]
[assembly: AssemblyTitle("Corkboard")]
[assembly: AssemblyProduct("Corkboard")]
