# Resource Router 架构设计与抽象全量审查报告

## 1. 架构核心机制审查
本项目代码设计遵循模块化、低耦合哲学。底层依赖通过依赖注入完成，业务生命周期不直接耦合物理存储结构。具体实施的核心机制如下：

- **空载占位与防伪回落机制 (No Code-Blocking Defaults)**
  在核心管线中，未启用或未实现的能力抽象使用相应的 NoOp (No Operation) 类进行填充。管线内不存在拦截特性开关的 `if (featureEnabled)` 判断，保障主轴通过纯数据流转保持畅通。
- **纯粹领域事件机制 (Event-Driven Mutations)**
  直接落盘动作（插入/更新/删除）剥离了所有关联动作调起职责。`ResourceManager` 在保存动作结束后广播原生的 `EventHandler` 领域事件，由各职能监控者（如重建索引/UI更新等）自行响应执行。
- **级联权重路由 (Cascade Weight Routing)**
  插件与能力解析路由通过多权重的 `IComparable` 实现。权重遵循规则为特定来源类型匹配优先、MIME精细度匹配其次、最后依赖插件本身的优先级降级匹配，摒弃了常数魔数算分的数值重叠风险。

## 2. 契约层 (ResourceRouter.PluginSdk) 审查
本层提供与外部和底层通信的数据结构抽象集。

**抽象接口定义：**
- `IFormatConverter`: 定义基础的文件/内容格式解析和转换契约，要求实现转换后结果输出、文本内容提取与格式化缩略图。
- `IProcessingCapabilityApi`: 定义扩展处理特性的对外契约。

**实体与通信定义：**
- `ConversionResult`: 制式化的格式转换后文件与内容封装载体。
- `ConvertOptions`: 描述转换期要求的特定选项。
- `ExtractedContent` / `ContentParagraph`: 富文本转换后的降级结构化标准，提供正文落盘结构。
- `OcrResult` / `AudioTranscriptionResult`: 光学符号与音频提取后的标准化输出模型。
- `StructuredFeatureSet` / `FeatureSubmissionResult`: 处理能力获取的特征序列及其提交状态。
- `PluginAttribute`: 插件动态绑定的反射属性元信息基座。

## 3. 领域核心与服务层 (ResourceRouter.Core) 审查
本层掌控实体规范及全套系统级服务。

**全量数据模型 (Models)：**
- `Resource`: 系统级跨层核心实体，记录资源标识、时间戳、配置源、当前状态与处理策略（包括 Privacy, SyncPolicy 等）。
- `RawDropData`: 输入统一抽象，剥离平台界限（保留 FileDrop, Text, Bitmap, URL 结构）。
- `PendingResource`: 挂起处理状态模型，包含撤销 Token 及生命等待期。
- `AIResult`: 包装标签提取与提要分析的统一对象。
- `PermissionPreset`: 系统级资源策略模板及策略路由。
- `ResourceHealthReport`: 返回资源合规探测后的健康性分析。
- `ResourceGovernancePolicy`: 针对来源制定资源治理监控指标政策。
- `NativeCapabilityProviders`: 记录可用原生特性配置挂载状态。
- `ProcessedRouteOption`: 向前端呈现的可读路由组合映射。
- `ResourceIngestOptions`: 控制采集行为的临时附加策略参数。
- `AppConfig`: 各类路径与默认属性的反序列化落地目标实体。

**全量抽象定义 (Abstractions)：**
- `IAIProvider`: 特征认知/打标云或本地入口。
- `IAppLogger`: 标准运行时追踪。
- `ICloudSyncProvider`: 面向云端块存储的对象同步。
- `IRemoteSyncProvider`: 专用的设备侧通信对接通道。
- `IExportablePayload`: 自定义插件与数据导出的载荷获取机制。
- `IFormatConverterResolver`: 外部向系统投递转换路由配置的筛选机制。
- `IProcessingConfigurationProvider`: 提供资源生命参数控制参数集。
- `IResourceFeatureExtractor`: 生成哈希特征用以治理和同质消重的探测。
- `IResourceGovernanceProvider`: 控制前置健康约束。
- `IResourceHealthMonitor`: 执行资源损坏与黑名单识别。
- `IResourceStore`: 执行结构化与关系数据持久化。
- `ISearchIndex`: 处理反向索引和关键字高光检索。
- `IStorageProvider`: 映射块级长内容文件物理转存。
- `IThumbnailProvider`: 资源图标图表导出与挂载。

**服务管理总线 (Services)：**
- `PipelineEngine`: 管线主循环调度系统，负责按时序唤醒健康探测、特征提取、持久层保存及等待态倒计时派生逻辑。
- `ResourceManager`: 唯一实体修改收敛入口，派发 `ResourceCreated/Updated/Deleted` 事件通信。
- `PluginHost`: DLL 插件集物理装载与 Cascade 路由映射计算中心。
- `ConfigStore`: 启动配置同步管理器。
- `PermissionPresetProvider`: 策略装配路由选择预处理分发器。

**占位器集群 (Services/NoOp)：**
全量 `NoOpProcessingConfigurationProvider`, `NoOpResourceFeatureExtractor`, `NoOpResourceGovernanceProvider`, `NoOpResourceHealthMonitor` 以空载返回填补空缺的业务特性闭环。

## 4. 基础设施层 (ResourceRouter.Infrastructure) 审查
基于 Core 层提出的虚设备要求，进行与强文件和强库绑定的实现。遵循了核心隔离理念，不存在未实现能力留驻问题（全部厂商硬编码逻辑如 Ollama/WebDAV 均已按插件化要求移除）。

**持久化存储实现 (Storage)：**
- `SqliteResourceStore`: 通过 SQLite 连接注入 `IResourceStore` 的增删改查实现类，集成数据库版本迁移管控。
- `LocalFileStorage`: 依据资源 ID 原子落盘保存文件（覆盖了提取、临时转换）的业务类，负责物理磁盘块的隔离转移。
- `LocalPathProvider`: 控制全局可配置执行域的数据生成节点逻辑。
- 类型转换器 (`ResourceHealthStatusTypeHandler`, `StringListTypeHandler`): SQLite 数据驱动层 Dapper 映射。

**检索及探测衍生 (Search & Format)：**
- `SearchEngine`: 使用 SQLite FTS5 的 Match 实现关键字正交映射搜索类。
- `MimeDetector`: 检测后缀名以判定原生格式属性工具。

## 5. UI交互表现层 (ResourceRouter.App) 审查
对宿主系统的专属控制集成，负责与用户和操作系统直接通信。不直接操作底层数据基座。

**功能组件边界：**
- `AppRuntime`: 全局组合根(Composition Root)类，组装了服务注册和前置启动项管控器。
- `DragDropBridge`: OLE System Drag&Drop 中间件。分离了 Wpf Windows 传递来的 DataObject ，完成与模型 `RawDropData` 全覆盖转写和数据分发。
- `ShellThumbnailHelper` / `ThumbnailProvider`: 调用 COM 组件针对不同扩展名向系统环境寻源图解和默认占位图标。
- `Win32Helpers`: 封装非托管 Windows 核心钩子的桥接服务。

**界面视图行为控制器 (Behaviors)：**
- `DampedDragBehavior`: 向视图注入具有物理阻尼衰减特性的缓动刷新钩子。
- `ProximityFadeBehavior`: 按频检测像素落点后触发渲染呈现状态机的调度脚本。

## 6. 数据库及存储审视
**结构隔离度：**
- 数据元独立性：关系引擎 (`Resource` 及相关数据类) 单行只记录文件信息元数据和生命周期的短文本；
- 倒排搜索独立性：文本特征和段落等查询需要的内容交付给附带 FTS5 功能的独立虚表（并解耦响应 `Resource` 更新机制以重建）。
- 重负载物理独立：块级元（如录音内容、解包后的提取特征、原态备份等）与本地磁盘进行映射。由 `LocalFileStorage` 生成独占的文件路径。保证应用即使故障时不损坏被托管的用户物理静态产物。