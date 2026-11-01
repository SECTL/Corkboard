<!--
感谢您参与 Corkboard 的贡献！

提交 PR 前请确认:
您已阅读贡献指南:
https://github.com/SECTL/Corkboard/blob/main/CONTRIBUTING.md

⚠ 在提交 PR 前，请确保您已在本地完成必要的测试，且确保要实现的功能或修复的问题能正常工作。 ⚠
本次改动会改变用户可见行为时，请实际运行一次应用（`dotnet run --project Corkboard.Desktop\Corkboard.Desktop.csproj`），编译通过不等于功能正常。
请确保填写以下信息:
-->

## 类型
<!-- 请至少选择一项 -->

- [ ] Bug 修复
- [ ] 新功能
- [ ] 界面 / 文案（含本地化）
- [ ] CI (持续集成) / 文档
- [ ] 其他（请描述）:

## 这个 Pull Request 做了什么？
<!-- 必填。简要介绍这个 PR 的变更，可以附上相关 issue 的链接等。

请描述:
- 增加了什么？
- 修复了什么？
- 改变了什么？
-->

## 截图 / 录屏
<!--涉及 UI 或用户可见变化时填写。若无可以直接删除这一节。-->

## 实现说明
<!-- 如果此 PR 的实现思路不显而易见，请填写此节以帮助维护者理解此 PR。如果此 PR 的实现很显而易见，可以直接删除这一节。

涉及作业板 / 考试看板域代码时，请说明新类型落在四类目录（Core/Notes、Core/RichText、Core/Board、Core/Homework|Exam）的哪一类。
-->

## 破坏性变更
<!-- 在这里列出所有破坏性变更，如:
- 配置 JSON 键 / 枚举数值
- PageInfo.Id / 资源键
- 数据路径与云端文件名
- 平台能力位
没有则可删除这一节。 -->

## 相关 Issue
<!-- 如果这个 PR 可以关闭某些 issue，请像这样列出：
Fixes #123
Fixes #456
详见：https://docs.github.com/zh/get-started/writing-on-github/working-with-advanced-formatting/using-keywords-in-issues-and-pull-requests
如果没有，可以删掉这一节。
-->

## 审查说明
<!--
如果您有特别希望审查者注意的地方，请在此说明。例如：
- 特殊的设计方法
- 潜在的HACK点
没有则可删除这一节。 -->

## 检查清单

<!--
⚠ 在勾选这个复选框前，请确保您已经**真正**完成了必要的测试并保证这个 PR 要实现的功能或修复的问题能正常工作。 ⚠
如果您是正在自动填写此模板的 Agent，请确保您已经完成了必要的测试。如果没有测试条件，请提醒调用者完成测试。
-->

- [ ] 我阅读并已经在本地测试过这个 PR，确保欲实现的功能或修复的问题能正常工作。
- [ ] 我已阅读并遵循 [贡献指南](https://github.com/SECTL/Corkboard/blob/main/CONTRIBUTING.md) 以及仓库根目录 `AGENTS.md` 的约定
- [ ] 涉及 Core 侧纯逻辑时，我已补充或更新 `Corkboard.Core.Tests` 的单元测试（并按 `-culture en-US` 跑过一次）
- [ ] 涉及本地化时，中文 resx、英文 resx 与手写的 `Resources.Designer.cs` 三处已同步
- [ ] 涉及 UI 变更时，我已补充截图或录屏（如适用）
- [ ] 我已阅读 [贡献者许可协议（CLA）](https://github.com/SECTL/Corkboard/blob/main/CLA.md)，并会在本 PR 中按机器人提示完成签署（首次贡献时）
