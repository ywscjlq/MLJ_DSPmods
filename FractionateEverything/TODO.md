# TODO

## v2.3.1 词缀系统改进 (2026-06)

- [x] 性能优化: 热路径消除DateTime.Now/HashSet/重复GetById/List分配, 零GC
- [x] 功能修复: 闪电赌局(TryTriggerLightning) + 薛定谔叠加态(SetSchrödingerIfActive) 接入ProcessManager
- [x] 突变/双子词缀模板(ID 900/901) 定义补齐
- [x] 流派锁定: 后端(字典+API+预计算roll+存档持久化) + UI(Shift+1~4锁定/Shift+R解锁)
- [x] 词缀数据预计算: 按等级+按流派预计算列表, 消除运行时查询
- [x] 代码清理: FractionateEverything.cs 4436→176行(反编译artifact消除)
- [x] 构建部署: AfterBuildEvent 自动复制DLL到r2modman profile

## 相对历史 TODO 的遗留项

- [x] ChaosAbyss存档持久化: Export/Import/IntoOtherSave (v2.3.1)
- [x] Consumption流派多样性: 新增TradeOff(27)/RuleChanger(28)/Rhythm(29)模板 (v2.3.1)
- [x] 补货系统性能: 消除File.AppendAllText磁盘IO + Keys.ToList()分配 (v2.3.1)
- [x] 词缀系统33项综合改进: 性能/功能/引擎/UI/存档/代码清理 (v2.3.1)

- [ ] 黑雾支线从"资源入口整合"升级为更完整的独立支线玩法
  - 当前主要通过成长页报价、市场板特单和循环任务目标接入
  - 仍缺少更成体系的支线阶段感、独立推进反馈和更明确的玩法闭环
- [ ] 彻底清理已冻结旧体系壳层
  - `BaseRecipe` 里仍保留旧回响 / 退火兼容字段与接口壳
  - 旧 VIP 页面已从代码层移除，后续继续检查其他冻结壳层
- [ ] 评估并决定是否彻底删除剩余旧兼容命名包装
  - 例如 `PackageUtils.GetEssenceMinCount()` / `TakeEssenceFromModData()` 这类仅保留给兼容的旧接口
  - 如果确认不再需要兼容旧调用路径，可进一步统一成当前 2.3 命名

## 动态经济系统

- [ ] Phase 5：整体验证与再平衡
  - 检查市场系统对主线抽卡、成长池、原版增强、黑雾支线是否产生意外替代
  - 只允许它提高上限，不允许它成为唯一正确玩法

## UI / 体验

- [ ] 配方筛选、排序、搜索
- [ ] 新的物品购买式封装 UI
- [ ] Mod 介绍图标
- [ ] 宣传视频
