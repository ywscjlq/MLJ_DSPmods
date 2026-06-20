# MLJ_DSPmods 代码审查报告

**审查范围：** MLJ_DSPmods-master
**主项目：** FractionateEverything (FE) — 戴森球计划模组
**审查日期：** 2026年6月

---

## 一、总体评价：★★★★☆

一个架构清晰、工程纪律严明的大型模组项目。代码质量和组织方式远超一般游戏模组水准，接近商业软件标准。以下分维度详述。

---

## 二、架构设计 ★★★★★

### 优势

**1. 分层架构清晰**
```
Bootstrap (生命周期编排)
  ├── Logic/    (领域逻辑：Building, Fractionation, Gacha, Economy, Station, DarkFog...)
  ├── UI/       (表现层：MainPanel, Controls, Foundation, Layout, Shell)
  ├── Compatibility/ (跨模组适配 + 联机同步)
  ├── Utils/    (工具层：ProtoID常量, I18N, 日志, 存档)
  └── Lifecycle/ (FeatureBootstrap + FeatureSaveRegistry)
```

每个模块职责明确，`FeatureBootstrap` 做启动编排而非让主插件 `FractionateEverything.cs` 硬编码所有 manager 生命周期，这是很好的 practice。

**2. 存档系统设计精巧**
- `FeatureSaveRegistry` 统一管理多个子模块的序列化
- 各 manager 各自实现 `Import/Export/IntoOtherSave`，职责内聚
- 存档兼容性处理（如版本 < 10 的旧档降级处理）干净利落

**3. 多项目编排**
- 解决方案含 4 个项目：主 mod、数据导出工具、构建后事件、模拟器
- 构建配置通过 `Directory.Build.props` 统一管理，避免重复

**4. 可维护性考虑周到**
- AGENTS.md 系列文档遍布每个子目录，对 AI Agent 和人类开发者都非常友好
- Harmony patch 命名规范 `ClassName_MethodName_Suffix`，一目了然
- 代码风格统一（K&R 大括号、4 空格缩进、C# 12 语法）

---

## 三、代码质量 ★★★★☆

### 优势

**1. 性能意识强**
- `ProcessManager.InternalUpdate` 中用细粒度 perf 计时（`FractionatorPerfStage` / `FractionatorPerfDetail`）追踪每阶段开销
- `FractionatorComponent.GetExtraState` 避免重复分配
- `RecipeTypeArr` 用定长数组（`BaseRecipe[12000]`）而非字典（O(1) 查找）
- `ThreadStatic` 字段减少多线程冲突
- 羁绊/词缀系统使用 `ConcurrentDictionary` 保证线程安全

**2. 错误处理恰当**
- 不滥用 try-catch（遵从 Unity/BepInEx 全局异常捕获）
- 大量 guard clause 早期返回
- 空引用保护：`if (factory == null) return` 这类检查随处可见
- 通过 `MarkRuntimeSchema` 缓存运行期 schema 避免重复计算

**3. 设计模式运用合理**
- Strategy 模式：5 种塔通过 `updateHandlersByBuildingOffset` 委托数组分发
- Template Method 模式：`BaseRecipe` 定义骨架，子类（`ConversionRecipe`, `MineralCopyRecipe`）实现具体逻辑
- 静态工厂 + 注册表：`RecipeManager.AddRecipe` / `RecipeTypeArr` 实现了高效的配方注册和查询
- Switch 表达式（C# 12）用于等级驱动的属性计算

### 可改进点

**1. 方法体过长**
`ProcessManager.InternalUpdate<T>`（~300 行）是个大方法，虽逻辑连续，但可以考虑拆分为子步骤方法。当前已用注释分块标记（`#region` + 阶段注释），基本可读。

**2. `Utils` 是上帝类**
`ProtoID.cs` 定义了 1100+ 行常量，且全部放在 `Utils` 的 partial class 中。这本不坏（常量需要集中管理），但 `Utils` 类同时承担了大量杂项功能（`static using` 遍布全项目）。可以考虑严格区分：
- `ProtoID.cs` → 保持常量
- `FormatUtils.cs` / `LogUtils.cs` 等 → 已有独立文件，好
- 其他杂项方法 → 酌情拆分

**3. 部分 switch 表达式可提取为更声明式的配置**
如 `BuildingManager` 中反复出现的按 `fractionator.ID` switch 判 `MaxStack` / `SuccessBoost`，可考虑用字典/查找表：

```csharp
// 当前
return fractionator.ID switch {
    IFE交互塔 => InteractionTower.SuccessBoost,
    IFE矿物复制塔 => MineralReplicationTower.SuccessBoost,
    ...
};

// 可改为
private static readonly Dictionary<int, float> SuccessBoostMap = new() {
    [IFE交互塔] = ...,
    [IFE矿物复制塔] = ...,
    ...
};
```

虽然没有性能提升，但减少重复。

**4. 少量魔法数字**
- `500.0 / 3.0`、`300000`、`10000`、`0.02f`、`0.04f` 等散见于 ProcessManager 的方法体中，大部分是游戏平衡数值，建议抽取为命名常量以提高可读性。

**5. 代码注释以中文为主**
对于双语团队透明，但游戏模组社区国际化程度高，可以考虑在公开 API 上补充英文注释。AGENTS.md 已有英文版本，好。

---

## 四、词缀系统深度分析 ★★★★☆

词缀系统（Affix）是整个模组中复杂度最高的子系统。

### 亮点
- **六阶稀有度**（Silver → Mythic），映射 LoL Arena 风格
- **四大流派**（Speed / Output / Consumption / Chaos）构成策略深度
- **羁绊系统**（Bond）统一替代旧版共鸣/冲突/连锁三系统，减少概念数量
- **彗星机制（Comet）**：里程碑（20/40/80/160...）驱动的累计增益
- **BurstQueue**：传送带满时暂存产物的反压机制

### 关注点
- 羁绊评估 `EvaluateBonds` 方法较长（约 200 行），可以抽取每种羁绊类型的评估函数
- 奇偶校验/命定机制逻辑在代码中分散，可考虑统一抽取
- `FracAffixManager` 本身是一个巨大的静态类（~800 行），是否有拆分为多个 smaller manager 的空间？

---

## 五、抽卡系统（Gacha）★★★☆☆

### 亮点
- 原神风格软/硬保底（73→90）
- 多池（6个）+ 聚焦（Focus）系统
- 速通模式（Speedrun）与正常模式并行

### 关注点
- `GachaManager.PityCount` 和 `GachaPool` 使用定长数组，池数量固定，扩展性有限
- 抽卡统计（S/A/B 分布）代码较薄，可以考虑后期增加更细粒度的概率仪表板

---

## 六、构建与工具链 ★★★★★

### 亮点
- MSBuild 本地构建配置清晰（net472）
- AfterBuildEvent 自动打包 + 上传 R2 + 生成 zip
- DecompiledSource 目录（ilspycmd 自动反编译依赖模组）是极佳的参考源
- AGENTS.md 里对构建命令的约束非常具体（wt.exe vs powershell，路径，环境差异处理）

---

## 七、VanillaCurveSim 模拟器 ★★★★☆

### 亮点
- 独立模拟器项目，支持离线推演平衡性
- 对 vanilla 曲线的模拟细致（戴森球功率估计、瓶颈记录、阶段建造计划）
- Simulator-first 工作流保证：先在模拟器验证，再应用到实际 mod

### 关注点
- 模拟器硬编码大量游戏数值，如果游戏版本更新可能需要同步
- `Simulator.cs` 约 3000 行，存在一定的拆解空间

---

## 八、兼容性层 ★★★★★

### 亮点
- 软依赖 16 个外部模组（Nebula、创世之书、星环、BuildToolOpt 等）
- 每个模组适配有独立文件（`Mods/GenesisBook.cs`, `Mods/OrbitalRing.cs`...）
- Nebula 联机同步有独立的 packet 注册和数据同步
- `DarkFog` 兼容分支（深空来敌）单独处理

这是整个项目中组织得最好的部分之一——扩展性强，接入新模组只需加一个文件 + 一条 `[BepInDependency]`。

---

## 九、工程纪律 ★★★★★

- **快照 → 修改 → 验证 → commit** 的改前快照规则非常成熟
- 不允许 `git stash` 替代 commit（不可追溯）
- 失败 2 次回滚，3 次停止请求干预——止损机制好
- Git 串行规则（不允许并发 git 命令）考虑到了 `.git/index.lock`
- "严禁积压未提交改动" 原则

---

## 十、值得注意的风险点

| 风险 | 严重度 | 说明 |
|------|--------|------|
| `RecipeTypeArr` 定长 12000 | 低 | 如果游戏未来扩展物品 ID 超过此范围会越界，建议加边界断言 |
| `ProcessBeltInput` 每次从尾部取货 | 中 | 高负载下每 tick 多次 `TryPickItemAtRear` 可能成为瓶颈，当前已有 perf 追踪 |
| 模组间 `[HarmonyPatch]` 冲突 | 中 | `FractionateEverything.Awake` 中用 `PatchAll` 自动 patch 所有 type，可能与新安装的模组产生冲突 |
| DecompiledSource 依赖 ilspycmd 全局安装 | 低 | 新环境配置时可能遗漏，已在 AGENTS.md 中标注 |
| 无单元测试 | 中 | AGENTS.md 明确声明无测试，构建为唯一质量门禁。对于复杂逻辑（如词缀羁绊评估），手工测试覆盖不全 |

---

## 总结

**MLJ_DSPmods** 是一个高质量的游戏模组项目，其工程化程度远超同类。核心优势在：

1. **架构清晰**：分层、模块化、职责内聚
2. **工程纪律**：完善的 Git 工作流、改前快照、AI Agent 协作规范
3. **性能意识**：细粒度 perf 追踪、O(1) 查找、线程安全设计
4. **兼容性设计**：16 个软依赖模组的适配、联机同步支持
5. **文档完备**：AGENTS.md 系列覆盖每个子域

主要的改进空间是**部分方法偏长**和**少量魔法数字**，但都不影响核心质量。

**评分：4.5 / 5 ★**
