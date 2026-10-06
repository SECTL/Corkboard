using System.Reflection;
using Corkboard;

// 只有启用 EnableGitInfoGenerator 的头程序集编译这个文件（见 Global.props）：
// 版本特性由构建期写入的 GitInfo 提供，因此这类工程必须把 GenerateAssemblyInfo 关掉，
// 否则会和 SDK 生成的同名特性冲突（CS0579）。

[assembly: AssemblyVersion(GitInfo.AssemblyVersion)]
[assembly: AssemblyInformationalVersion($"{GitInfo.Tag}+{GitInfo.CommitHash}")]
[assembly: AssemblyTitle("Corkboard")]
[assembly: AssemblyProduct("Corkboard")]
