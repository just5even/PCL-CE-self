# PCL Community Edition (PCL-CE)

基于 PCL 开源代码二次开发的 Minecraft 启动器社区版，原作者为龙腾猫跃。

- **技术栈**: C# (.NET 8.0 WPF), C# 14 语言版本
- **目标平台**: Windows 10 1809+ (win-x64 / win-arm64)
- **UI 框架**: WPF (XAML), MVVM 模式 (CommunityToolkit.Mvvm)
- **配置文件**: 4 个构建配置 — Debug, CI, Release, Beta

## 解决方案结构

```
Plain Craft Launcher 2.slnx          ← 解决方案文件 (.slnx 新格式)
├── Plain Craft Launcher 2/          ← 主启动器项目 (WinExe, net8.0-windows)
├── PCL.Core/                        ← 核心库 (net8.0-windows)
├── PCL.Core.SourceGenerators/       ← 自定义源生成器 (Roslyn Analyzer)
└── PCL.Core.Test/                   ← 单元测试 (默认不参与构建)
```

### Plain Craft Launcher 2/ — 主项目 (启动器前端)

```
Plain Craft Launcher 2/
├── Application.xaml / App.xaml      ← WPF 应用入口
├── Program.cs                       ← Main() 静态入口
├── Controls/                        ← 自定义控件
│   ├── Behaviors/                   ← WPF Behavior
│   └── MyMsg/                       ← 消息提示控件
├── Images/                          ← 图标、皮肤、背景等图像资源
├── Modules/                         ← 业务模块 (旧 VB 模块的 C# 重写)
│   ├── Base/                        ← 基础工具 / 常量 / 配置序列化
│   ├── Minecraft/                   ← Minecraft 相关 (启动、实例管理、模组)
│   │   └── ModLaunch.cs            ← 模组启动逻辑 (当前有未提交修改)
│   ├── Network/                     ← 网络模块
│   │   ├── Downloader/             ← 下载器
│   │   ├── Facade/                 ← 外观层
│   │   ├── Http/                   ← HTTP 客户端封装
│   │   ├── Loaders/                ← 模组加载器安装 (Forge/Fabric/NeoForge 等)
│   │   ├── Management/             ← 资源包/存档管理
│   │   └── Models/                 ← 网络数据模型
│   ├── Theme/                       ← 主题管理
│   └── Updates/                     ← 更新检测
├── Pages/                           ← 页面 (每个页面 = 一个 UI 标签页)
│   ├── PageDownload/                ← 下载页 (模组/整合包/资源包)
│   ├── PageHomepage/                ← 主页
│   ├── PageInstance/                ← 实例管理页
│   │   └── PageInstanceSaves/       ← 存档管理子页
│   ├── PageLaunch/                  ← 启动页
│   ├── PageSetup/                   ← 设置页
│   └── PageTools/                   ← 工具页
└── Resources/                       ← 本地化字符串 (中文)
```

### PCL.Core/ — 核心库

```
PCL.Core/
├── App/                             ← 应用层
│   ├── Cli/                         ← 命令行接口
│   ├── Configuration/               ← 配置管理
│   ├── Database/                    ← 数据库层 (LiteDB / SQLite)
│   ├── Essentials/                  ← 环境检测 (Java 定位等)
│   ├── IoC/                         ← 依赖注入 (IoC 容器)
│   ├── Tasks/                       ← 任务系统
│   └── Tools/                       ← 工具集
├── IO/                              ← IO 层
│   ├── Download/                    ← 下载引擎
│   ├── Net/                         ← 网络 (DNS/HTTP/代理)
│   └── Storage/                     ← 文件存储
├── Link/                            ← 联机模块
│   ├── EasyTier/                    ← EasyTier 联机
│   ├── Lobby/                       ← 大厅/匹配
│   ├── McPing/                      ← Minecraft 服务器 Ping
│   ├── Natayark/                    ← NAT 穿透
│   └── Scaffolding/                 ← 联机框架
├── Logging/                         ← 日志与遥测 (Sentry)
├── Minecraft/                       ← Minecraft 核心
│   ├── IdentityModel/               ← 用户身份模型
│   ├── Java/                        ← Java 运行时管理
│   ├── Launch/                      ← 启动逻辑
│   ├── ResourceProject/             ← 资源工程
│   └── Yggdrasil/                   ← Yggdrasil 认证
├── Model/                           ← 数据模型
│   └── Homepage/                    ← 主页数据模型
├── UI/                              ← UI 组件
│   ├── Animation/                   ← 动画
│   ├── Assets/                      ← 资源文件
│   ├── Controls/                    ← UI 控件
│   ├── Converters/                  ← 值转换器
│   ├── Effects/                     ← 视觉效果
│   ├── Media/                       ← 媒体播放
│   └── Theme/                       ← 主题引擎
├── Utils/                           ← 工具类
└── ViewModel/                       ← ViewModel 层
```

## 关键架构要点

- **MVVM 模式**: View 在 `Plain Craft Launcher 2/Pages/`，ViewModel 在 `PCL.Core/ViewModel/`
- **IoC 容器**: `App/IoC/` 管理依赖注入，`LifecycleState.cs` 被 SourceGenerator 使用
- **构建配置**:
  - `Debug` — 开发调试
  - `CI` — CI 环境
  - `Release` — 正式发布
  - `Beta` — 公测版本
- **目标平台**: `AnyCPU` / `x64` / `ARM64`
- **序列化**: Newtonsoft.Json (主项目), System.Text.Json (Core)
- **数据库**: LiteDB + SQLite (Dapper)
- **网络**: 自定义 HTTP 层 + Polly 重试 + DNS 客户端
- **发布**: 单文件自包含 (PublishSingleFile=true)

## 编码约定

- 项目从 VB 逐步迁移到 C# (#2511)，`.vb` 文件已从编译中排除 (`DefaultItemExcludes`)
- `Plain Craft Launcher 2/` 使用自定义许可证，其余目录使用 Apache 2.0
- C# 源文件手动包含 (`EnableDefaultItems=false`)
- 主项目根命名空间 `PCL`，Core 命名空间 `PCL.Core`
- 语言版本 C# 14，启用 nullable
