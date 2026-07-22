# FE mod v3.0.0 全面审查报告

## 审查范围：184个CS文件，32,685行代码

---

## 🔴 严重问题

### P0: ProtocolRetrievalService 未注册存档
- **文件**: `src/Logic/Civilization/Protocols/ProtocolRetrievalService.cs`
- **问题**: 有locked (`lock(_sync)`) 但没有 Export/Import 方法，也未在 FeatureSaveRegistry 注册
- **影响**: 协议进度、完成计数在游戏存档/读档时丢失
- **修复**: 需添加 Export/Import 并注册到 FeatureSaveRegistry

### P1: AutoReplenish JSON 手动解析（5处 Substring）
- **文件**: `src/Logic/Economy/AutoReplenishManager.cs` L186, L203, L213, L223, L231
- **问题**: 手写 `IndexOf` + `Substring` 解析AI策略返回的JSON，无任何格式校验
- **影响**: AI返回格式稍有变化（空格、换行、键顺序），Substring索引就会越界→游戏崩溃
- **修复**: 用 `Newtonsoft.Json` 或 `System.Text.Json` 序列化

### P1: LDB.Select 误用风险（73处，40种模式）
- **问题**: `LDB.items.Select(itemId)` 是字典O(1)安全；但 `LDB.items.Select(e.ItemId)` 在foreach循环中每次调用都做一次O(1)查询
- **主要影响文件**: AutoReplenishManager.cs, ControlPanelPatch.cs, Rendering.cs
- **风险**: 在Update/每帧路径中反复调用会造成GC压力

---

## 🟡 中等问题

### P2: ProcessManager.cs 单文件1054行
- 29个方法挤在一个文件，包含状态机/缓存/寻路/碰撞检测
- 建议拆分为 ProcessStateMachine.cs / ProcessPathfinding.cs / ProcessCollision.cs

### P2: AutoReplenish 无Newtonsoft.Json依赖
- 项目已引用 `Newtonsoft.Json`（DSP依赖自带），但手写JSON解析不用
- 导致5处Substring越界风险

### P3: 物品ID 1120/1121/1122 硬编码
- `RelicManager.cs` 使用 1120/1121/1122 作为遗物物品ID
- 需确认不与DSP未来更新新增物品冲突（建议动态注册）

### P3: 3个文件无存档/读档
- `RelicData.cs` - 数据结构定义（通过RelicManager间接存档，OK）
- `RelicPage.cs` - UI页面（UI不存档，OK）
- `ProtocolRetrievalService.cs` - 🔴 需要存档

---

## ✅ 良好实践

| 项目 | 评价 |
|:-----|:-----|
| **编译** | 0 error, 0 warning |
| **线程安全** | ✅ 多处使用lock保护共享数据 |
| **异常处理** | ✅ 外部调用点有try-catch保护 |
| **HarmonyPatch规范** | ✅ 43个patch文件，命名清晰 |
| **存档架构** | ✅ FeatureSaveRegistry集中管理 |
| **代码结构** | ✅ 按功能模块划分清晰（Logic/UI/Compatibility） |
| **核心玩法** | ✅ 分馏/协议/遗物/自动补货四大子系统独立且可组合 |

---

## 总结

```
严重程度 数量
🔴 P0    1  (存档丢失)
🔴 P1    2  (JSON崩溃 + LDB性能)
🟡 P2    2  (ProcessManager拆分 + 缺Json库)
🟡 P3    3  (物品ID硬编码 + 存档不完整)
```

**最紧急**: 给 ProtocolRetrievalService 加 Export/Import → 否则协议进度不保存
