# UE4SS 更新机制核查

核查基线：上游 1.6.4（ae49c98），本次只确认机制，不修改 UE4SS 更新逻辑。

## 结论

**已经有启动自动检查 + 用户点击更新的机制，没有无人值守自动安装。**

- 启动时异步读取当前游戏的远程清单，没有周期轮询。
- 默认按顺序尝试两个 Cloudflare 域名，再尝试 GitHub raw；每源 5 秒。
- 使用维护者为游戏选择的 UE4SS 构建，不直接跟踪 UE4SS-RE 的最新 release。
- 没有 stable / beta / nightly 版本通道选择。
- 清单版本与安装记录不同（或记录未知）时，主页和 UE4SS 页面显示更新按钮。
- 关闭游戏，点击“更新到 …”下载并安装；也可以从本地压缩包安装。
- 下载优先使用清单镜像，失败再尝试原始地址；提供 SHA-256 时才校验。

## 重要局限

1. **“不同版本”不等于“更高版本”。** 当前比较只判断版本字符串是否不同，
   手动安装版本或更高版本也可能被提示替换为清单版本，存在降级可能。
2. UI 显示的 DLL / 日志检测版本，不等于用于更新判断的安装记录版本。
3. 清单离线、缺少游戏条目时可能没有更新提示；不是证明 UE4SS 已最新。
4. 安装会保留管理器部署的模组与部分已修改的设置文件，并有当次异常回滚；
   不等于保留所有手改文件，也不提供历史版本自动回退。
5. 清单 SHA-256 可选，不提供清单签名或目标游戏兼容性验证。

## 代码与测试依据

- [启动及清单检查](../src/UEManager.UI/ViewModels/MainViewModel.cs)：
  InitializeAsync / CheckUe4ssManifestAsync。
- [更新判断](../src/UEManager.Core/Mods/Ue4ssManifest.cs)：IsUpdate；
  FetchAsync 负责清单读取及回退。
- [默认源与游戏专用发布页](../src/UEManager.Core/Games/Profiles/phantom-blade-zero.json)。
- [下载及安装](../src/UEManager.UI/ViewModels/MainViewModel.cs)：DownloadAndInstallUe4ssAsync；
  [安装和文件保护](../src/UEManager.Core/Mods/ModManager.cs)：InstallUe4ss。
- [清单回归测试](../src/UEManager.Core.Tests/Ue4ssManifestTests.cs)、
  [安装保护测试](../src/UEManager.Core.Tests/Ue4ssInstallTests.cs)。

本次新增的 Nexus 模组页面检测与 UE4SS 清单检查互相独立，不影响上述行为。
