# ResourceRouter

ResourceRouter 是一个本地优先的智能化资源管理和中转枢纽引擎。它的核心目标是统一接管来自全终端、剪贴板、拖拽以及外部收集通道的信息源，并通过自动化流水线为其进行打标签、归类、摘要提炼以及格式转化，最后持久化至本地数据库并可灵活推送到多端设备。

项目高度贯彻“模块化、低耦合”的代码哲学。除了框架核心的流转调度、状态机控制与数据持久化由内部实现外，一切富媒体处理（如 AI 智能摘要、语音转文字、OCR、外部存储协议）均遵循**插件优先**及接口适配的架构理念。

---

## 💡 核心概念澄清

在深入使用或二次开发前，请熟悉以下系统核心概念：

- **Resource (核心领域模型)**
  在系统中，无论是一段纯文本、一个带图片的网页书签，还是一份音视频文件，一切被收集的信息均被抽象并打平为 `Resource`。它具有跨形态的一致属性（比如统一的 ID、创建时间、标签集、类型标志），并携带了统一的状态机机制（例如处理中、已完成、失败等待）。

- **Source (信息源)**
  描述资源最初是由什么渠道捕获的。例如系统剪贴板监听、UI 界面手动拖放拖拽（Drag & Drop）、或是外部通过 API 推送的网络请求。

- **Pipeline (处理流水线)**
  资源进入系统后，并不会只留在存储介质里吃灰。流水线（`PipelineEngine`）会接管它们，针对不同的 MIME 类型指派到相应的处理器链路。例如，文本内容可能会被送到本地或云端的 AI 服务中提取 `Summary`（总结）并生成 `AutoTags`；图片会被提取指纹或进行 OCR 等。

- **Plugin API / Capability (能力切面)**
  系统核心只负责分配任务并等待处理结果返回。凡是涉及到具体重计算的特征处理（如视频生成缩略图，大模型分析内容），皆声明为独立接口（例如 `IAIProvider`、`IFormatConverter`）。默认情况若无相关插件接管或服务无响应，处理步骤将被安全跳过并禁用。

---

## 🚀 主要功能介绍

1. **统一的资源落盘与检索 (Unified Storage & Search)**
   - 采用内嵌 SQLite 结合 Dapper 与 DbUp 数据库版本管理工具，实现了高性能零配置的数据留存与优雅升级。
   - 内置强大的快速本地全文检索（基于 FTS）与条件过滤。

2. **异步全自动内容精炼 (Auto Content Refining)**
   - 后台调度流水线处理特征提取工作，并在完成后向客户端或数据库广播增量更新事件，无需阻塞 UI 及导入流程。

3. **插件化横铺 (Horizontally Pluggable)**
   - 开发人员可以通过 `ResourceRouter.PluginSdk` 提供标准实现，无缝接入新的 OCR 引擎或新的大模型 API。

4. **安全跨端分发考量 (Sync & Policy)**
   - 内置了基础的 PrivacyLevel（隐私层级）与 PersistencePolicy（持久化策略）标识，为日后支持 P2P 网络同步和自动失效等策略奠定基础。

---

## 🛠️ 普通用户使用指南 (User Guide)

ResourceRouter 作为你的桌面“万能收纳与中转站”，它的交互极度追求无感和流畅。以下是你能使用的所有核心功能及其操作方式：

### 1. 唤醒与定位 (边缘悬浮条)
- **边缘感应**：程序启动后，你可以将鼠标完全推到屏幕最**左侧边缘**。只需停留片刻，透明的边界感应条便会渐显。
- **阻尼拖动 (复位)**：你可以点住悬浮条，沿着屏幕左侧边缘上下拖动它到你顺手的位置。物理阻尼效果会让拖拉变得顺滑。
- **点击展开**：点击唤出的边缘条，主面板（资源卡片列表）即可弹出。

### 2. 资源的集纳 (入库)
- **拖拽收集**：选中任意文本、网页里的内容、甚至是一个脱机文件，直接往屏幕左侧边缘的感应条拖拽。
- **隐私/公开分区入库**：拖入时，面板会浮现收纳靶区。将资源拖转释放到“公开 (Public)”或“私密 (Private)”区域均可完成收集，**集纳完成后，程序会自动展开主面板**，方便你立刻连贯地查看处理状态和提取特征。

### 3. 卡片管理与双滑块导出 (核心交互)
打开主面板后，你所有的历史资源将以卡片流的形式展现。
- **点击交互与双滑块**：有别于普通的列表，这里的卡片被**点击**后，会浮现两个操作滑块：
  - **`Raw` 滑块**：按住它往其他应用（如微信、文件夹）拖拽，你导出的将是**完全原始的资源**（外部原文件、原本文本）。
  - **`Processed` 滑块**：按住它拖拽，你导出的是被底层插件**处理转化后**的形态（比如：从 PDF 抽出的文字、音频转化得到的文本）。如果该资源没有任何匹配处理产物路由，滑块将锁定无法拖拽。
- **左滑删除卡片**：如果需要清理某条不需要的记录，在卡片上**按住并向左滑动**，即可唤出左侧红色的删除层。松手后完成清理——这仅会清理系统数据库记录（及可能引用的内部缓存副本），但**严格遵守不删除外部资源本体原文件**的核心原则（见设计规约）。底层的架构设计同时也为资源删除预留了事件与同步抽象，便于后期接管和向云端上报清理动作。

### 4. 强大的多维搜索与 Tag 抽象
- **抽象的查询机制**：资源库自带毫秒级全文匹配搜索计算，并在核心层提取了统一的 `ISearchIndex` 接口。这意味着未来可通过插件机制，将传统的文本 SQL 匹配无缝切换为向量 (Vector) 检索或端侧大模型语义搜索。
- **全局搜索框**：在主面板上方输入任何包含在你过去的资源提取文本、标题或 AI 生成逻辑中的词，皆会无延迟显示。
- **Tag (标签) 大师**：
  - 搜索框内输入 `#标签名` 或 `tag:标签名`，搜索框下方的一行“Tag 显式栏”会自动高亮并筛选对应芯片 (Chip)。
  - **开启/关闭标签**：点击点亮的芯片进入持续过滤状态！不管搜索框内容如何变动，所有展示出来的卡片都必须包含你高亮的这些 Tag。
  - **系统级配置入口**：特殊的框架配置也被看作资源，你可以专门筛选 `#config` 或 `#framework` 来找到它们。

---

## 💻 开发者指南：部署与环境

### 环境要求
- Windows (前端 App 为专用 WPF 实现)
- .NET 8.0 SDK 或更高版本

### 构建与启动应用程序

可通过项目内提供的 PowerShell 实用脚本直接快速启停，或者使用 `dotnet` CLI：

1. **一键构建与运行**：
   在项目根目录下，执行打包执行命令：
   ```powershell
   # 运行构建指令
   dotnet build ./ResourceRouter.sln -c Release

   # 启动 WPF 桌面客户端应用
   ./scripts/run-app.ps1
   ```

2. **核心业务测试**：
   运行所有核心库（`Core` 模块）的基础与行为测试用例：
   ```powershell
   ./scripts/test-core.ps1
   ```
   *注：项目中也有针对独立场景的功能测试脚本，保证了重构不破坏原有业务假设。*

---

## 🧩 插件开发机制 (Plugin Mechanism)

本项目的灵活性极大程度得益于其贯穿全生命周期的插件化隔离设计。请注意：**插件绝不仅仅用于参与“文件格式转换”的只读流水线**。系统几乎所有的原生机制能力（包括去重、大模型 AI 通信、特征化抽象、同步拉取等）都可以通过插件来实现与替换，从而完全避免对框架底座的硬编码修改。

### 1. 概念澄清与介入环节 (Runtime Intervention)

当一个新的信息片段或文件被扔进系统后，系统本身也依赖下面这些对外暴露的接口运作：

1. **类型侦测与内容析取 (Format Conversion)**
   - 侦测文件类型，并由 `IFormatConverter` 插件执行转换，拆解出通用结构和文本。
2. **抽象的特征化操作 (Feature Extraction)**
   - 提取的重点并非单纯为了视觉识别。**特征 (Feature)** 是对于**任何可被计算资源**的抽象指纹标识，例如通过 `IResourceFeatureExtractor` 计算文件的唯一 Hash，或提取一段 JSON 的结构主键。这是系统能够实现“后续去重”等判断的前置基础。
3. **重度能力与 AI 介入 (Capability & AI)**
   - 对于高算力需要的音视频特征，系统抛给 `IProcessingCapabilityApi` 插件执行。提取的所有纯文本结构将被送进指定的 `IAIProvider` 大模型插件，解析语义并构建标签网络。
4. **后置调度：健康监测、多端同步与去重治理**
   - 资源在流转与落盘的漫长周期内，可被外挂的机制类插件所控制。例如利用 `IResourceHealthMonitor` 判定资源来源是否已经失效，依靠 `IResourceGovernanceProvider` 建立排重策略，或者依靠由远端机制抽离出的独立 `IRemoteSyncProvider` 或 `ICloudSyncProvider` 进行点对点同步通信。
5. **非 Raw 产物映射 (决定性的一环)**：
   - 资源卡片上的 `Processed` (处理态) 双滑块中绑定的导出物，本质上就是这些插件执行的业务流直接计算产物（即 `ProcessedFilePath` / `ProcessedText`）。如果没有对应的计算结果（未装载插件等），Processed 滑块将被直接锁定。

### 2. 框架原生暴露扩展的典型接口

要在系统中织入你的新机制或处理能力，只需引入 `ResourceRouter.Core.Abstractions`，并实现相对应的原生接入点：

#### 🔸 数据与资源解析组
- `IFormatConverter`：实现未适配或私有格式文件的主体和元数据解析。
- `IResourceFeatureExtractor`：负责资源的统一特征化抽象提取（如文件哈希、文档分块特征指纹计算）。

#### 🔸 系统机制与通信组
- `IAIProvider`：充当核心语义大脑抽象。你可以将 Ollama 本地模型、或是云端 OpenAI 等模型 API 接入封装于此。
- `IResourceGovernanceProvider`：编写去重、淘汰规则和特定的数据清理机制算法。
- `IRemoteSyncProvider`：完全接管跨端网路通讯与直连中继路由发送。

#### 🔸 能力与健康设施
- `IProcessingCapabilityApi`：定义繁重的外部算法或环境能力（如调用本地/云端专属模型处理的 OCR/音视频分析）。
- `IResourceHealthMonitor`：对引用的网盘地址、易移除的 U 盘文件等执行状态监测挂载与报警控制。

### 3. 开发示范 (Example)

以下是一个接入简易 Markdown 解析插件的代码示例：

```csharp
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.PluginSdk;

// 可选: 通过特性标记供 IoC / Reflection 服务发现
[Plugin(Name = "Basic Markdown Parser", Version = "1.0")]
public class MarkdownFormatConverter : IFormatConverter
{
    public string Name => "Markdown Engine";
    public IReadOnlyCollection<string> SupportedMimeTypes => new[] { "text/markdown" };

    public Task<ExtractedContent> ExtractContentAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        // 你的实际业务逻辑：读取 MD 文件去标签，返回纯文本结构
        var rawText = System.IO.File.ReadAllText(inputPath);
        var parsedText = rawText.Replace("**", "");

        return Task.FromResult(new ExtractedContent
        {
            PlainText = parsedText,
            Success = true
        });
    }

    public Task<string?> GenerateThumbnailAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
         // 文本通常没啥缩略图，或者是画一个带字的占位符
         return Task.FromResult<string?>(null);
    }
}
```

> **架构规范忠告**  
> 所有属于“重算/云端/外部依赖”的实现细节，必须止步于 `Infrastructure` 及特定的外置插件类库中。绝对不允许将网络流控、特定的第三方密钥管理、复杂的 JSON API 调用代码直接污染进入 `ResourceRouter.Core` 的领域实体之中。

## Todo：

- 几个附加机制仅做抽象接口预留作为占位，待实现。
- 在`UI`的美观和交互上还需继续打磨。
- 云或它端的同步价值和场景需要考虑，对应形态需要进行调整。